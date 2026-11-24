using backend.Data;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public enum PlaidSyncStatus
{
    Synced,
    NotFound,
    Conflict,
    PlaidFailed,
}

/// <summary>Row counts are ledger rows inserted, updated and deleted by this sync.</summary>
public sealed record PlaidSyncOutcome(
    PlaidSyncStatus Status,
    int Added = 0,
    int Modified = 0,
    int Removed = 0,
    DateTime? LastSyncedAt = null);

/// <summary>
/// Pulls an item's transaction changes from Plaid into the ledger.
///
/// Every page is fetched before anything is written, then the rows and the new cursor are saved in
/// one <c>SaveChanges</c>. So a failure at any point leaves both the ledger and the cursor where
/// they were, and the next sync asks Plaid for the same changes again. Upserts key on Plaid's
/// transaction id, which makes that replay harmless.
/// </summary>
public sealed class PlaidSyncService(
    AppDbContext db,
    IPlaidClient plaid,
    AccessTokenProtector protector,
    ILogger<PlaidSyncService> logger)
{
    /// <summary>Plaid's signal that data changed mid-pagination; the fix is to restart from the saved cursor.</summary>
    private const string MutationDuringPagination = "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION";

    private const int MaxRestarts = 3;
    private const int MaxPages = 100;
    private const int MaxDescriptionLength = 500;

    public async Task<PlaidSyncOutcome> SyncAsync(Guid userId, Guid plaidItemId, CancellationToken cancellationToken)
    {
        var item = await db.PlaidItems
            .SingleOrDefaultAsync(row => row.Id == plaidItemId && row.UserId == userId, cancellationToken);
        if (item is null)
        {
            return new PlaidSyncOutcome(PlaidSyncStatus.NotFound);
        }

        var accessToken = protector.Unprotect(item.EncryptedAccessToken, userId);

        Changes changes;
        PlaidItemDetails details;
        try
        {
            changes = await FetchAllAsync(accessToken, item.SyncCursor, cancellationToken);
            details = await plaid.GetItemDetailsAsync(accessToken, cancellationToken);
        }
        catch (PlaidException ex)
        {
            logger.LogWarning("Plaid sync failed for item {PlaidItemId}: {Reason}", item.Id, ex.Message);
            return new PlaidSyncOutcome(PlaidSyncStatus.PlaidFailed);
        }

        var accounts = await db.Accounts
            .Where(account => account.UserId == userId
                && account.PlaidItemId == item.Id
                && account.PlaidAccountId != null)
            .ToDictionaryAsync(account => account.PlaidAccountId!, cancellationToken);

        // The bank's logo and colour can change, and were not stored for banks linked before they existed.
        item.InstitutionLogo = InstitutionBranding.CleanLogo(details.Logo);
        item.InstitutionColor = InstitutionBranding.CleanColor(details.PrimaryColor);

        // Keeps names, last 4 digits and types current, including for banks linked before they were stored.
        foreach (var plaidAccount in details.Accounts)
        {
            if (accounts.TryGetValue(plaidAccount.AccountId, out var account))
            {
                account.Name = plaidAccount.Name;
                account.Mask = plaidAccount.Mask;
                account.Type = plaidAccount.Type;
                account.Subtype = plaidAccount.Subtype;
                account.CurrentBalance = plaidAccount.CurrentBalance;
                account.BalanceAsOf = plaidAccount.CurrentBalance is null ? null : DateTime.UtcNow;
            }
        }

        var (added, modified, removed) = await ApplyAsync(
            userId,
            accounts.ToDictionary(pair => pair.Key, pair => pair.Value.Id),
            changes,
            cancellationToken);

        item.SyncCursor = changes.NextCursor;
        item.LastSyncedAt = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another sync of this item committed first. Its cursor stands; ours and our rows are dropped.
            return new PlaidSyncOutcome(PlaidSyncStatus.Conflict);
        }

        return new PlaidSyncOutcome(PlaidSyncStatus.Synced, added, modified, removed, item.LastSyncedAt);
    }

    /// <summary>
    /// Walks every page from <paramref name="cursor"/>, folding them in order into one final state per
    /// transaction id: an add then a remove in a later page ends as a remove, and the reverse as an add.
    /// </summary>
    private async Task<Changes> FetchAllAsync(string accessToken, string? cursor, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var upserts = new Dictionary<string, PlaidTransaction>();
            var removals = new HashSet<string>();
            var next = cursor;
            try
            {
                for (var page = 0; page < MaxPages; page++)
                {
                    var result = await plaid.SyncTransactionsAsync(accessToken, next, cancellationToken);
                    foreach (var transaction in result.Added.Concat(result.Modified))
                    {
                        upserts[transaction.TransactionId] = transaction;
                        removals.Remove(transaction.TransactionId);
                    }

                    foreach (var transactionId in result.Removed)
                    {
                        upserts.Remove(transactionId);
                        removals.Add(transactionId);
                    }

                    next = result.NextCursor;
                    if (!result.HasMore)
                    {
                        return new Changes(upserts, removals, next ?? "");
                    }
                }

                throw new PlaidException(System.Net.HttpStatusCode.InternalServerError, "TOO_MANY_PAGES", null);
            }
            catch (PlaidException ex) when (ex.ErrorCode == MutationDuringPagination && attempt < MaxRestarts)
            {
                logger.LogInformation("Plaid data changed mid-sync; restarting from the saved cursor.");
            }
        }
    }

    /// <param name="accounts">Plaid account id to our account id, for this user's accounts on this item only.</param>
    private async Task<(int Added, int Modified, int Removed)> ApplyAsync(
        Guid userId,
        IReadOnlyDictionary<string, Guid> accounts,
        Changes changes,
        CancellationToken cancellationToken)
    {
        var ids = changes.Upserts.Keys.Concat(changes.Removals).ToList();
        var existing = await db.Transactions
            .Where(row => row.UserId == userId
                && row.Source == TransactionSource.Plaid
                && row.ExternalId != null
                && ids.Contains(row.ExternalId))
            .ToDictionaryAsync(row => row.ExternalId!, cancellationToken);

        int added = 0, modified = 0, removed = 0;

        foreach (var transactionId in changes.Removals)
        {
            if (existing.TryGetValue(transactionId, out var row))
            {
                db.Transactions.Remove(row);
                removed++;
            }
        }

        foreach (var transaction in changes.Upserts.Values)
        {
            existing.TryGetValue(transaction.TransactionId, out var row);

            // Pending rows are skipped: Plaid posts the settled version under a new id and removes
            // the pending one, so storing it would show each purchase twice for a few days.
            if (transaction.Pending || !accounts.TryGetValue(transaction.AccountId, out var accountId))
            {
                if (row is not null)
                {
                    db.Transactions.Remove(row);
                    removed++;
                }

                continue;
            }

            if (row is null)
            {
                row = new Transaction
                {
                    UserId = userId,
                    Source = TransactionSource.Plaid,
                    ExternalId = transaction.TransactionId,
                    Description = "",
                };
                db.Transactions.Add(row);
                added++;
            }
            else
            {
                modified++;
            }

            row.AccountId = accountId;
            row.Date = transaction.Date;
            row.Description = Truncate(transaction.MerchantName ?? transaction.Name);
            // Plaid: positive is money out. Ledger: negative is money out.
            row.Amount = -transaction.Amount;

            var category = TransactionCategorizer.Categorize(
                transaction.CategoryPrimary, transaction.CategoryDetailed, transaction.MerchantName, transaction.Name);
            row.Category = category.Category;
            row.Subcategory = category.Subcategory;
        }

        return (added, modified, removed);
    }

    private static string Truncate(string value) =>
        value.Length <= MaxDescriptionLength ? value : value[..MaxDescriptionLength];

    private sealed record Changes(
        IReadOnlyDictionary<string, PlaidTransaction> Upserts,
        IReadOnlySet<string> Removals,
        string NextCursor);
}
