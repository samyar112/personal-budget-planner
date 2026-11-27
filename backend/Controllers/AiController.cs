using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using backend.Data;
using backend.DTOs;
using backend.Extensions;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

/// <summary>What the AI would receive, and the tips it returns. Every read is scoped to the user id in the validated token.</summary>
[ApiController]
[Route("api/ai")]
[Authorize(Policy = AuthSecurityExtensions.VerifiedEmailPolicy)]
public sealed class AiController(AppDbContext db, AiPayloadBuilder payloadBuilder, AiTipsService tipsService) : ControllerBase
{
    /// <summary>
    /// The payload exactly as it would be sent, and the privacy report about it. Nothing is sent to any AI
    /// provider here. The report holds the caller's own descriptions and account endings, so the response
    /// is marked no-store and never cached by the browser or a proxy.
    /// </summary>
    [HttpGet("payload-preview")]
    [ProducesResponseType<AiPayloadPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AiPayloadPreviewResponse>> PayloadPreview(CancellationToken cancellationToken)
    {
        var subject =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var result = await payloadBuilder.BuildAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(new AiPayloadPreviewResponse(
            result.Payload, PayloadPrivacyValidator.Serialize(result.Payload), result.Report));
    }

    /// <summary>
    /// Sends the previewed payload for tips. <c>Consent</c> must be true: this press is the user's agreement to
    /// send it. Refused with 422 when the privacy check finds anything, so nothing leaves the API; 429 past the
    /// daily limit; 402 once the free trial has ended without a subscription. A model that is off, down or wrong still returns 200
    /// with template tips.
    /// </summary>
    [HttpPost("tips")]
    [ProducesResponseType<AiTipsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AiTipsResponse>> Tips(GenerateTipsRequest request, CancellationToken cancellationToken)
    {
        var subject =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        if (!request.Consent)
        {
            ModelState.AddModelError(nameof(request.Consent), "Consent is required to send the payload.");
            return ValidationProblem(ModelState);
        }

        // The AI call is the one feature with a per-request cost, so the trial is enforced here, not only in the UI.
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        if (!Trial.HasAiAccess(user, DateTime.UtcNow))
        {
            return Problem(
                "Your free trial has ended. Upgrade to keep generating AI tips.",
                statusCode: StatusCodes.Status402PaymentRequired);
        }

        Response.Headers.CacheControl = "no-store";
        var result = await tipsService.GenerateAsync(userId, DateTime.UtcNow, cancellationToken);
        return result.Outcome switch
        {
            TipsOutcome.PrivacyBlocked => Problem(
                "The privacy check found identifying text, so nothing was sent.",
                statusCode: StatusCodes.Status422UnprocessableEntity),
            TipsOutcome.DailyLimitReached => Problem(
                "You have generated tips the maximum number of times today. Try again tomorrow.",
                statusCode: StatusCodes.Status429TooManyRequests),
            _ => Ok(result.Response),
        };
    }
}
