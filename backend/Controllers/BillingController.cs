using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using backend.Data;
using backend.DTOs;
using backend.Extensions;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;

namespace backend.Controllers;

/// <summary>
/// The $2.99/month subscription through Stripe, test mode only. Checkout and the billing portal are Stripe-hosted,
/// so card details never reach this API. Whether a user has paid is decided by the signed webhook alone, never by
/// the browser returning from Checkout, because a return URL can be typed by hand.
/// </summary>
[ApiController]
[Route("api/billing")]
public sealed class BillingController(
    AppDbContext db,
    IBillingGateway gateway,
    IOptions<StripeOptions> options,
    ILogger<BillingController> logger) : ControllerBase
{
    private static readonly HashSet<string> SubscriptionEvents =
    [
        EventTypes.CustomerSubscriptionCreated,
        EventTypes.CustomerSubscriptionUpdated,
        EventTypes.CustomerSubscriptionDeleted,
    ];

    /// <summary>Starts Checkout. 409 when already subscribed; 503 when Stripe is not configured.</summary>
    [HttpPost("checkout")]
    [Authorize(Policy = AuthSecurityExtensions.VerifiedEmailPolicy)]
    [ProducesResponseType<BillingRedirectResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<BillingRedirectResponse>> Checkout(CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured)
        {
            return Problem("Payments are not set up.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var user = await FindCallerAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        if (Trial.IsSubscribed(user))
        {
            return Problem("You are already subscribed.", statusCode: StatusCodes.Status409Conflict);
        }

        // Create the customer first, so subscription webhooks always find a user to match.
        if (user.StripeCustomerId is null)
        {
            user.StripeCustomerId = await gateway.CreateCustomerAsync(user.Id, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        var url = await gateway.CreateCheckoutUrlAsync(user.StripeCustomerId, user.Id, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(new BillingRedirectResponse(url));
    }

    /// <summary>Opens Stripe's billing portal to cancel or change the card. 404 for a user who never checked out.</summary>
    [HttpPost("portal")]
    [Authorize(Policy = AuthSecurityExtensions.VerifiedEmailPolicy)]
    [ProducesResponseType<BillingRedirectResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<BillingRedirectResponse>> Portal(CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured)
        {
            return Problem("Payments are not set up.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var user = await FindCallerAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        if (user.StripeCustomerId is null)
        {
            return NotFound();
        }

        var url = await gateway.CreatePortalUrlAsync(user.StripeCustomerId, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(new BillingRedirectResponse(url));
    }

    /// <summary>
    /// Stripe's subscription events. No session: the <c>Stripe-Signature</c> header is the authentication, and a
    /// bad one is 400. Events for unknown customers or of other types are acknowledged with 200 so Stripe stops
    /// retrying them. An event older than the one already applied is ignored, because Stripe does not promise order.
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        // The signature covers the exact bytes, so read the raw body rather than a model-bound object.
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync(cancellationToken);

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                json,
                Request.Headers["Stripe-Signature"],
                options.Value.WebhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException)
        {
            logger.LogWarning("Rejected a Stripe webhook with a bad signature.");
            return BadRequest();
        }

        if (!SubscriptionEvents.Contains(stripeEvent.Type) || stripeEvent.Data.Object is not Subscription subscription)
        {
            return Ok();
        }

        var user = await db.Users.FirstOrDefaultAsync(
            candidate => candidate.StripeCustomerId == subscription.CustomerId,
            cancellationToken);
        if (user is null)
        {
            logger.LogWarning("Stripe event {EventId} is for a customer with no user.", stripeEvent.Id);
            return Ok();
        }

        if (user.SubscriptionUpdatedAt is { } applied && stripeEvent.Created < applied)
        {
            return Ok();
        }

        user.SubscriptionStatus = subscription.Status;
        user.SubscriptionUpdatedAt = stripeEvent.Created;
        await db.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    private async Task<Models.User?> FindCallerAsync(CancellationToken cancellationToken)
    {
        var subject =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            return null;
        }

        return await db.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
    }
}
