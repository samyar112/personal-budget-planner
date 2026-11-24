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
    PlaidSyncService syncService,
    PlaidUnlinkService unlinkService,
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
            InstitutionLogo = InstitutionBranding.CleanLogo(details.Logo),
            InstitutionColor = InstitutionBranding.CleanColor(details.PrimaryColor),
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
            account.Mask = plaidAccount.Mask;
            account.Type = plaidAccount.Type;
            account.Subtype = plaidAccount.Subtype;
            account.CurrentBalance = plaidAccount.CurrentBalance;
            account.BalanceAsOf = plaidAccount.CurrentBalance is null ? null : DateTime.UtcNow;
            linked.Add(account);
        }

        // One SaveChanges is one database transaction: the item and its accounts land together or not at all.
        await db.SaveChangesAsync(cancellationToken);

        // First sync straight away so the dashboard fills in. Best effort: the link is already saved,
        // and a failure here leaves the cursor null, so Sync now picks up from the beginning.
        var firstSync = await syncService.SyncAsync(userId, item.Id, cancellationToken);
        if (firstSync.Status != PlaidSyncStatus.Synced)
        {
            logger.LogWarning("First sync after exchange did not complete: {Status}", firstSync.Status);
        }

        return Ok(new PlaidExchangeResponse(details.InstitutionName, linked.Select(ToResponse).ToList()));
    }

    /// <summary>The caller's linked banks and their accounts, oldest link first.</summary>
    [HttpGet("items")]
    [ProducesResponseType<IReadOnlyList<PlaidItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<PlaidItemResponse>>> ListItems(CancellationToken cancellationToken)
    {
        if (CallerId() is not { } userId)
        {
            return Unauthorized();
        }

        var items = await db.PlaidItems
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.CreatedAt)
            .Select(item => new { item.Id, item.InstitutionName, item.LastSyncedAt, item.InstitutionLogo, item.InstitutionColor })
            .ToListAsync(cancellationToken);

        var itemIds = items.Select(item => item.Id).ToList();
        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(account => account.UserId == userId
                && account.PlaidItemId != null
                && itemIds.Contains(account.PlaidItemId.Value))
            .OrderBy(account => account.Name)
            .ToListAsync(cancellationToken);
        var byItem = accounts.ToLookup(account => account.PlaidItemId!.Value);

        return Ok(items
            .Select(item => new PlaidItemResponse(
                item.Id,
                item.InstitutionName,
                item.LastSyncedAt,
                byItem[item.Id].Select(ToResponse).ToList(),
                item.InstitutionLogo,
                item.InstitutionColor))
            .ToList());
    }

    /// <summary>
    /// Pulls the item's new, changed and removed transactions into the ledger. 409 when another sync
    /// of the same item committed first; nothing from this attempt is saved.
    /// </summary>
    [HttpPost("items/{id:guid}/sync")]
    [ProducesResponseType<PlaidSyncResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PlaidSyncResponse>> Sync(Guid id, CancellationToken cancellationToken)
    {
        if (CallerId() is not { } userId)
        {
            return Unauthorized();
        }

        var outcome = await syncService.SyncAsync(userId, id, cancellationToken);
        return outcome.Status switch
        {
            PlaidSyncStatus.Synced => Ok(new PlaidSyncResponse(
                outcome.Added, outcome.Modified, outcome.Removed, outcome.LastSyncedAt!.Value)),
            PlaidSyncStatus.NotFound => NotFound(),
            PlaidSyncStatus.Conflict => Conflict(new { message = "A sync for this bank is already running." }),
            _ => StatusCode(StatusCodes.Status502BadGateway),
        };
    }

    /// <summary>
    /// Disconnects one of the caller's banks: revokes it at Plaid, then deletes its accounts and
    /// transactions. 502 when Plaid fails, in which case nothing was deleted and a retry is safe.
    /// </summary>
    [HttpDelete("items/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Unlink(Guid id, CancellationToken cancellationToken)
    {
        if (CallerId() is not { } userId)
        {
            return Unauthorized();
        }

        return await unlinkService.UnlinkAsync(userId, id, cancellationToken) switch
        {
            PlaidUnlinkStatus.Unlinked => NoContent(),
            PlaidUnlinkStatus.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status502BadGateway),
        };
    }

    private static LinkedAccountResponse ToResponse(Account account) =>
        new(account.Id, account.Name, account.Mask, account.Type, account.Subtype, account.CurrentBalance, account.BalanceAsOf);

    private Guid? CallerId()
    {
        var subject =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
