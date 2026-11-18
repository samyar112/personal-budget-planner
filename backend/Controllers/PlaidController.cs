using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using backend.Data;
using backend.DTOs;
using backend.Extensions;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

/// <summary>
/// Links a Plaid institution to the caller. The access token Plaid issues is encrypted before it is
/// stored and never appears in a response or a log line.
/// </summary>
[ApiController]
[Route("api/plaid")]
[Authorize(Policy = AuthSecurityExtensions.VerifiedEmailPolicy)]
public sealed class PlaidController(
    AppDbContext db,
    IPlaidClient plaid,
    AccessTokenProtector protector,
    ILogger<PlaidController> logger) : ControllerBase
{
    private const int MaxPublicTokenLength = 200;

    /// <summary>Mints a Plaid Link token for the caller. The browser uses it to open Plaid Link.</summary>
    [HttpPost("link-token")]
    [ProducesResponseType<PlaidLinkTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PlaidLinkTokenResponse>> CreateLinkToken(CancellationToken cancellationToken)
    {
        if (CallerId() is not { } userId)
        {
            return Unauthorized();
        }

        try
        {
            var linkToken = await plaid.CreateLinkTokenAsync(userId, cancellationToken);
            return Ok(new PlaidLinkTokenResponse(linkToken));
        }
        catch (PlaidException ex)
        {
            logger.LogWarning("Plaid link token failed: {Reason}", ex.Message);
            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// Swaps the public token from Plaid Link for an access token, then saves the item and its
    /// accounts against the caller. Accounts already linked by this caller are updated, not duplicated.
    /// </summary>
    [HttpPost("exchange")]
    [ProducesResponseType<PlaidExchangeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PlaidExchangeResponse>> Exchange(
        PlaidExchangeRequest request,
        CancellationToken cancellationToken)
    {
        if (CallerId() is not { } userId)
        {
            return Unauthorized();
        }

        var publicToken = request.PublicToken?.Trim();
        if (string.IsNullOrEmpty(publicToken) || publicToken.Length > MaxPublicTokenLength)
        {
            return BadRequest(new { message = "A Plaid public token is required." });
        }

        PlaidExchangeResult exchange;
        PlaidItemDetails details;
        try
        {
            exchange = await plaid.ExchangePublicTokenAsync(publicToken, cancellationToken);
            details = await plaid.GetItemDetailsAsync(exchange.AccessToken, cancellationToken);
        }
        catch (PlaidException ex) when (ex.IsClientError)
        {
            logger.LogWarning("Plaid exchange refused: {Reason}", ex.Message);
            return BadRequest(new { message = "The bank link could not be completed. Try linking again." });
        }
        catch (PlaidException ex)
        {
            logger.LogWarning("Plaid exchange failed: {Reason}", ex.Message);
            return StatusCode(StatusCodes.Status502BadGateway);
        }

        var item = new PlaidItem
        {
            UserId = userId,
            ItemId = exchange.ItemId,
            EncryptedAccessToken = protector.Protect(exchange.AccessToken, userId),
            InstitutionId = details.InstitutionId,
            InstitutionName = details.InstitutionName,
        };
        db.PlaidItems.Add(item);

        var plaidAccountIds = details.Accounts.Select(account => account.AccountId).ToList();
        var existing = await db.Accounts
            .Where(account => account.UserId == userId
                && account.PlaidAccountId != null
                && plaidAccountIds.Contains(account.PlaidAccountId))
            .ToDictionaryAsync(account => account.PlaidAccountId!, cancellationToken);

        var linked = new List<Account>();
        foreach (var plaidAccount in details.Accounts)
        {
            if (!existing.TryGetValue(plaidAccount.AccountId, out var account))
            {
                account = new Account { UserId = userId, Name = plaidAccount.Name, PlaidAccountId = plaidAccount.AccountId };
                db.Accounts.Add(account);
            }

            account.Name = plaidAccount.Name;
            account.Institution = details.InstitutionName;
            account.PlaidItemId = item.Id;
            linked.Add(account);
        }

        // One SaveChanges is one database transaction: the item and its accounts land together or not at all.
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new PlaidExchangeResponse(
            details.InstitutionName,
            linked.Select(account => new LinkedAccountResponse(account.Id, account.Name)).ToList()));
    }

    private Guid? CallerId()
    {
        var subject =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
