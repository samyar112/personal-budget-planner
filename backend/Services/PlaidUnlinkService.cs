using backend.Data;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public enum PlaidUnlinkStatus
{
    Unlinked,
    NotFound,
    PlaidFailed,
}

/// <summary>
/// Disconnects a bank. Plaid is told first, so a Plaid failure leaves our data intact and the user
/// can retry; we never keep a live bank token we have stopped tracking. Only then are the item's
/// transactions, accounts and the item itself deleted, in one <c>SaveChanges</c>. The relations
/// do not cascade (see <c>AppDbContext</c>), so each level is removed explicitly.
/// </summary>
public sealed class PlaidUnlinkService(
    AppDbContext db,
    IPlaidClient plaid,
    AccessTokenProtector protector,
    ILogger<PlaidUnlinkService> logger)
{
    /// <summary>Plaid's answer when the item is already removed on its side; the local cleanup should still run.</summary>
    private const string ItemNotFound = "ITEM_NOT_FOUND";

    public async Task<PlaidUnlinkStatus> UnlinkAsync(Guid userId, Guid plaidItemId, CancellationToken cancellationToken)
    {
        var item = await db.PlaidItems
            .SingleOrDefaultAsync(row => row.Id == plaidItemId && row.UserId == userId, cancellationToken);
        if (item is null)
        {
            return PlaidUnlinkStatus.NotFound;
        }

        try
        {
            await plaid.RemoveItemAsync(protector.Unprotect(item.EncryptedAccessToken, userId), cancellationToken);
        }
        catch (PlaidException ex) when (ex.ErrorCode == ItemNotFound)
        {
            logger.LogInformation("Plaid item {PlaidItemId} was already removed at Plaid.", item.Id);
        }
        catch (PlaidException ex)
        {
            logger.LogWarning("Plaid unlink failed for item {PlaidItemId}: {Reason}", item.Id, ex.Message);
            return PlaidUnlinkStatus.PlaidFailed;
        }

        var accountIds = await db.Accounts
            .Where(account => account.UserId == userId && account.PlaidItemId == item.Id)
            .Select(account => account.Id)
            .ToListAsync(cancellationToken);

        db.Transactions.RemoveRange(
            await db.Transactions
                .Where(transaction => transaction.UserId == userId && accountIds.Contains(transaction.AccountId))
                .ToListAsync(cancellationToken));
        db.Accounts.RemoveRange(
            await db.Accounts.Where(account => accountIds.Contains(account.Id)).ToListAsync(cancellationToken));
        db.PlaidItems.Remove(item);

        await db.SaveChangesAsync(cancellationToken);
        return PlaidUnlinkStatus.Unlinked;
    }
}
