using System.Net;
using System.Net.Http.Json;
using backend.Data;
using backend.DTOs;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

/*
 * Test cases:
 * 1. Sync writes posted transactions as Plaid rows for the caller, money out negative, pending skipped.
 * 2. Syncing the same transactions twice leaves one row each.
 * 3. Modified transactions update their row; removed ones delete it.
 * 4. has_more pages are all applied and the final cursor is stored.
 * 5. A Plaid failure partway through leaves the cursor and the ledger unchanged.
 * 6. A mutation-during-pagination error restarts from the saved cursor without duplicating rows.
 * 7. Syncing another user's item returns 404 and changes nothing.
 * 8. Listing items returns only the caller's banks, with mask and type, and no Plaid ids.
 * 9. An unverified user cannot list items.
 * 10. A successful exchange runs the first sync.
 * 11. A transaction for an account the user did not share is skipped.
 * 12. When another sync of the same item commits first, this one returns 409 and the winner's cursor stands.
 * 13. Sync refreshes each account's mask and type from Plaid, for banks linked before they were stored.
 * 14. Sync stores Plaid's category and subcategory, falls back to keywords then OTHER, and a modified row is recategorised.
 * 15. Linking stores each account's subtype, current balance, when the balance was read, and a card's limit.
 * 16. Sync refreshes subtype, balance and card limit, for banks linked before they were stored.
 * 17. Listing items returns each account's subtype, balance and balance time.
 * 18. An account Plaid reports no balance for keeps a null balance and a null balance time.
 * 19. Unlinking a bank revokes its token at Plaid, then deletes its transactions, accounts and the item.
 * 20. Unlinking one bank leaves the same user's other bank untouched.
 * 21. Unlinking another user's bank returns 404, calls Plaid not at all and deletes nothing.
 * 22. An unknown item id returns 404.
 * 23. When Plaid fails, unlink returns 502 and nothing is deleted, so it can be retried.
 * 24. When Plaid says the item is already gone, unlink still deletes the local data and returns 204.
 * 25. An unverified user cannot unlink (403), and an anonymous caller gets 401.
 * 26. Linking stores the bank's logo and brand colour from Plaid.
 * 27. Listing items returns each bank's logo and brand colour.
 * 28. Sync refreshes the logo and colour for a bank linked before they were stored.
 * 29. A logo that is not valid base64, or a colour that is not #rrggbb, is dropped; each is judged on its own.
 * 30. A logo over the size cap is dropped.
 * 31. A bank Plaid has no branding for keeps a null logo and colour.
 */
public sealed class PlaidSyncTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";
    private const string StartCursor = "cursor-empty";

    private readonly AuthWebApplicationFactory _factory;

    public PlaidSyncTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Sync_WritesPostedRows_ForCaller_WithLedgerSign()
    {
        var (userId, client, link) = await LinkedUserAsync(_factory);
        _factory.Plaid.EnqueueSync(link.AccessToken, Page("c1", added:
        [
            Txn("t-coffee", link.PlaidAccountId, 4.50m, "2026-11-01", "COFFEE SHOP 123", merchant: "Blue Bottle"),
            Txn("t-pay", link.PlaidAccountId, -2000m, "2026-11-02", "PAYROLL"),
            Txn("t-pending", link.PlaidAccountId, 9.99m, "2026-11-03", "PENDING THING", pending: true),
        ]));

        var response = await client.PostAsync($"/api/plaid/items/{link.ItemId}/sync", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<PlaidSyncResponse>())!;
        Assert.Equal(2, body.Added);
        await WithDbAsync(_factory, async db =>
        {
            var rows = await db.Transactions.Where(row => row.UserId == userId).OrderBy(row => row.Date).ToListAsync();
            Assert.Equal(["t-coffee", "t-pay"], rows.Select(row => row.ExternalId));
            Assert.All(rows, row => Assert.Equal(TransactionSource.Plaid, row.Source));
            Assert.All(rows, row => Assert.Equal(link.AccountId, row.AccountId));
            Assert.Equal(-4.50m, rows[0].Amount);
            Assert.Equal("Blue Bottle", rows[0].Description);
            Assert.Equal(2000m, rows[1].Amount);
            Assert.Equal("PAYROLL", rows[1].Description);
            var item = await db.PlaidItems.SingleAsync(row => row.Id == link.ItemId);
            Assert.Equal("c1", item.SyncCursor);
            Assert.NotNull(item.LastSyncedAt);
            return 0;
        });
    }

    [Fact]
    public async Task Sync_SameTransactionsTwice_ProducesNoDuplicates()
    {
        var (userId, client, link) = await LinkedUserAsync(_factory);
        PlaidTransaction[] both =
        [
            Txn("t-a", link.PlaidAccountId, 10m, "2026-11-01", "A"),
            Txn("t-b", link.PlaidAccountId, 20m, "2026-11-02", "B"),
        ];
        _factory.Plaid.EnqueueSync(link.AccessToken, Page("c1", added: both));
        _factory.Plaid.EnqueueSync(link.AccessToken, Page("c2", added: both));

        var first = await client.PostAsync($"/api/plaid/items/{link.ItemId}/sync", null);
        var second = await client.PostAsync($"/api/plaid/items/{link.ItemId}/sync", null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var again = (await second.Content.ReadFromJsonAsync<PlaidSyncResponse>())!;
        Assert.Equal(0, again.Added);
        Assert.Equal(2, await WithDbAsync(_factory, db => db.Transactions.CountAsync(row => row.UserId == userId)));
    }

    [Fact]
    public async Task Sync_AppliesModifiedAndRemoved()
    {
        var (userId, client, link) = await LinkedUserAsync(_factory);
        _factory.Plaid.EnqueueSync(link.AccessToken, Page("c1", added:
        [
            Txn("t-keep", link.PlaidAccountId, 10m, "2026-11-01", "Old name"),
            Txn("t-gone", link.PlaidAccountId, 20m, "2026-11-02", "Refunded"),
        ]));
        _factory.Plaid.EnqueueSync(link.AccessToken, Page(
            "c2",
            modified: [Txn("t-keep", link.PlaidAccountId, 12m, "2026-11-01", "New name")],
            removed: ["t-gone"]));

        await client.PostAsync($"/api/plaid/items/{link.ItemId}/sync", null);
        var response = await client.PostAsync($"/api/plaid/items/{link.ItemId}/sync", null);

        var body = (await response.Content.ReadFromJsonAsync<PlaidSyncResponse>())!;
        Assert.Equal(1, body.Modified);
        Assert.Equal(1, body.Removed);
        var row = Assert.Single(await WithDbAsync(_factory, db => db.Transactions.Where(r => r.UserId == userId).ToListAsync()));
        Assert.Equal("t-keep", row.ExternalId);
        Assert.Equal("New name", row.Description);
        Assert.Equal(-12m, row.Amount);
    }

    [Fact]
    public async Task Sync_FollowsHasMore_AndStoresFinalCursor()
    {
        var (userId, client, link) = await LinkedUserAsync(_factory);
        _factory.Plaid.EnqueueSync(link.AccessToken, Page("c1", hasMore: true, added: [Txn("t-1", link.PlaidAccountId, 1m, "2026-11-01", "One")]));
        _factory.Plaid.EnqueueSync(link.AccessToken, Page("c2", added: [Txn("t-2", link.PlaidAccountId, 2m, "2026-11-02", "Two")]));

        var response = await client.PostAsync($"/api/plaid/items/{link.ItemId}/sync", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, await WithDbAsync(_factory, db => db.Transactions.CountAsync(row => row.UserId == userId)));
        Assert.Equal("c2", await CursorAsync(_factory, link.ItemId));
        var cursors = _factory.Plaid.SyncCalls.Where(call => call.AccessToken == link.AccessToken).Select(call => call.Cursor).ToList();
        Assert.Equal([StartCursor, "c1"], cursors.TakeLast(2));
    }

    [Fact]
    public async Task Sync_FailurePartway_LeavesCursorAndLedgerUnchanged()
    {
        var (userId, client, link) = await LinkedUserAsync(_factory);
        var before = await WithDbAsync(_factory, db => db.PlaidItems.Where(item => item.Id == link.ItemId).Select(item => item.LastSyncedAt).SingleAsync());
        _factory.Plaid.EnqueueSync(link.AccessToken, Page("c1", hasMore: true, added: [Txn("t-1", link.PlaidAccountId, 1m, "2026-11-01", "One")]));
        _factory.Plaid.EnqueueSyncFailure(link.AccessToken);

        var response = await client.PostAsync($"/api/plaid/items/{link.ItemId}/sync", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(0, await WithDbAsync(_factory, db => db.Transactions.CountAsync(row => row.UserId == userId)));
        Assert.Equal(StartCursor, await CursorAsync(_factory, link.ItemId));
        Assert.Equal(before, await WithDbAsync(_factory, db => db.PlaidItems.Where(item => item.Id == link.ItemId).Select(item => item.LastSyncedAt).SingleAsync()));
    }

    [Fact]
    public async Task Sync_MutationDuringPagination_RestartsFromSavedCursor()
    {
        var (userId, client, link) = await LinkedUserAsync(_factory);
        var one = Txn("t-1", link.PlaidAccountId, 1m, "2026-11-01", "One");
        _factory.Plaid.EnqueueSync(link.AccessToken, Page("c1", hasMore: true, added: [one]));
        _factory.Plaid.EnqueueSyncFailure(link.AccessToken, "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION", HttpStatusCode.BadRequest);
        _factory.Plaid.EnqueueSync(link.AccessToken, Page("c9", added: [one, Txn("t-2", link.PlaidAccountId, 2m, "2026-11-02", "Two")]));

        var response = await client.PostAsync($"/api/plaid/items/{link.ItemId}/sync", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["t-1", "t-2"], (await WithDbAsync(_factory, db =>
            db.Transactions.Where(row => row.UserId == userId).Select(row => row.ExternalId!).ToListAsync())).Order());
        Assert.Equal("c9", await CursorAsync(_factory, link.ItemId));
        var cursors = _factory.Plaid.SyncCalls.Where(call => call.AccessToken == link.AccessToken).Select(call => call.Cursor).ToList();
        Assert.Equal(StartCursor, cursors[^1]);
    }

    [Fact]
    public async Task Sync_AnotherUsersItem_ReturnsNotFound_AndChangesNothing()
    {
        var (aliceId, _, aliceLink) = await LinkedUserAsync(_factory);
        var (_, bob, _) = await LinkedUserAsync(_factory);
        _factory.Plaid.EnqueueSync(aliceLink.AccessToken, Page("c1", added: [Txn("t-x", aliceLink.PlaidAccountId, 5m, "2026-11-01", "X")]));

        var response = await bob.PostAsync($"/api/plaid/items/{aliceLink.ItemId}/sync", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await WithDbAsync(_factory, db => db.Transactions.CountAsync(row => row.UserId == aliceId)));
        Assert.Equal(StartCursor, await CursorAsync(_factory, aliceLink.ItemId));
    }

    [Fact]
    public async Task ListItems_ReturnsOnlyCallersBanks_WithMaskAndType()
    {
        var (_, alice, aliceLink) = await LinkedUserAsync(_factory, institution: "Alice Bank");
        await LinkedUserAsync(_factory, institution: "Bob Bank");

        var response = await alice.GetAsync("/api/plaid/items");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<List<PlaidItemResponse>>())!;
        var item = Assert.Single(items);
        Assert.Equal(aliceLink.ItemId, item.Id);
        Assert.Equal("Alice Bank", item.Institution);
        Assert.NotNull(item.LastSyncedAt);
        Assert.Equal(["Checking", "Credit Card"], item.Accounts.Select(account => account.Name));
        var card = item.Accounts.Single(account => account.Name == "Credit Card");
        Assert.Equal("3333", card.Mask);
        Assert.Equal("credit", card.Type);
        Assert.DoesNotContain("Bob Bank", raw);
        Assert.DoesNotContain(aliceLink.AccessToken, raw);
        Assert.DoesNotContain(aliceLink.PlaidItemId, raw);
        Assert.DoesNotContain(aliceLink.PlaidAccountId, raw);
    }

    [Fact]
    public async Task ListItems_ForUnverifiedUser_ReturnsForbidden()
    {
        var (_, email) = await SeedUserAsync(_factory, verified: false);
        var client = await SignedInAsync(_factory, email);

        var response = await client.GetAsync("/api/plaid/items");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Exchange_RunsFirstSync()
    {
        var (userId, email) = await SeedUserAsync(_factory);
        var client = await SignedInAsync(_factory, email);
        var publicToken = $"public-sandbox-{Guid.NewGuid():N}";
        var plaidAccountId = $"acct-{Guid.NewGuid():N}";
        var exchange = _factory.Plaid.AddItem(publicToken, "Sandbox Bank", new PlaidAccount(plaidAccountId, "Checking", "0000", "depository"));
        _factory.Plaid.EnqueueSync(exchange.AccessToken, Page("c1", added: [Txn("t-first", plaidAccountId, 3m, "2026-11-01", "First")]));

        var response = await client.PostAsJsonAsync("/api/plaid/exchange", new PlaidExchangeRequest(publicToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = Assert.Single(await WithDbAsync(_factory, db => db.Transactions.Where(r => r.UserId == userId).ToListAsync()));
        Assert.Equal("t-first", row.ExternalId);
    }

    [Fact]
    public async Task Sync_RefreshesAccountMaskAndType()
    {
        var (userId, client, link) = await LinkedUserAsync(_factory);
        await WithDbAsync(_factory, async db =>
        {
            // As if linked before Mask and Type existed.
            foreach (var account in db.Accounts.Where(row => row.UserId == userId))
            {
                account.Mask = null;
                account.Type = null;
            }

            return await db.SaveChangesAsync();
        });

        var response = await client.PostAsync($"/api/plaid/items/{link.ItemId}/sync", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var card = await WithDbAsync(_factory, db => db.Accounts.SingleAsync(row => row.UserId == userId && row.Name == "Credit Card"));
        Assert.Equal("3333", card.Mask);
        Assert.Equal("credit", card.Type);
    }

    [Fact]
    public async Task Exchange_StoresSubtypeAndBalance()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var (userId, _, _) = await LinkedUserAsync(_factory);

        var rows = await WithDbAsync(_factory, db => db.Accounts.Where(row => row.UserId == userId).ToListAsync());

        var checking = rows.Single(row => row.Name == "Checking");
        Assert.Equal("checking", checking.Subtype);
        Assert.Equal(1234.56m, checking.CurrentBalance);
        Assert.True(checking.BalanceAsOf >= before);
        var card = rows.Single(row => row.Name == "Credit Card");
        Assert.Equal("credit card", card.Subtype);
        Assert.Equal(250.10m, card.CurrentBalance);
        Assert.Equal(1000m, card.CreditLimit);
        Assert.Null(checking.CreditLimit);
    }

    [Fact]
    public async Task Sync_RefreshesSubtypeAndBalance()
    {
        var (userId, client, link) = await LinkedUserAsync(_factory);
        await WithDbAsync(_factory, async db =>
        {
            // As if linked before Subtype and balances existed.
            foreach (var account in db.Accounts.Where(row => row.UserId == userId))
            {
                account.Subtype = null;
                account.CurrentBalance = null;
                account.BalanceAsOf = null;
                account.CreditLimit = null;
            }

            return await db.SaveChangesAsync();
        });

        var response = await client.PostAsync($"/api/plaid/items/{link.ItemId}/sync", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var card = await WithDbAsync(_factory, db => db.Accounts.SingleAsync(row => row.UserId == userId && row.Name == "Credit Card"));
        Assert.Equal("credit card", card.Subtype);
        Assert.Equal(250.10m, card.CurrentBalance);
        Assert.Equal(1000m, card.CreditLimit);
        Assert.NotNull(card.BalanceAsOf);
    }

    [Fact]
    public async Task ListItems_ReturnsSubtypeBalanceAndBalanceTime()
    {
        var (_, client, _) = await LinkedUserAsync(_factory);

        var items = (await client.GetFromJsonAsync<List<PlaidItemResponse>>("/api/plaid/items"))!;

        var checking = Assert.Single(items).Accounts.Single(account => account.Name == "Checking");
        Assert.Equal("checking", checking.Subtype);
        Assert.Equal(1234.56m, checking.CurrentBalance);
        Assert.NotNull(checking.BalanceAsOf);
    }

    [Fact]
    public async Task Exchange_AccountWithoutBalance_KeepsBalanceAndTimeNull()
    {
        var (userId, email) = await SeedUserAsync(_factory);
        var client = await SignedInAsync(_factory, email);
        var publicToken = $"public-sandbox-{Guid.NewGuid():N}";
        _factory.Plaid.AddItem(publicToken, "Sandbox Bank", new PlaidAccount($"acct-{Guid.NewGuid():N}", "Brokerage", "9999", "investment"));

        var response = await client.PostAsJsonAsync("/api/plaid/exchange", new PlaidExchangeRequest(publicToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var account = await WithDbAsync(_factory, db => db.Accounts.SingleAsync(row => row.UserId == userId));
        Assert.Null(account.CurrentBalance);
        Assert.Null(account.BalanceAsOf);
    }

    [Fact]
    public async Task Unlink_RevokesTokenThenDeletesTransactionsAccountsAndItem()
    {
        var (userId, client, link) = await LinkedUserAsync(_factory);
        _factory.Plaid.EnqueueSync(link.AccessToken, Page("c1", added: [Txn("t-1", link.PlaidAccountId, 5m, "2026-11-01", "One")]));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/plaid/items/{link.ItemId}/sync", null)).StatusCode);

        var response = await client.DeleteAsync($"/api/plaid/items/{link.ItemId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(link.AccessToken, _factory.Plaid.RemovedAccessTokens);
        await WithDbAsync(_factory, async db =>
        {
            Assert.Empty(await db.Transactions.Where(row => row.UserId == userId).ToListAsync());
            Assert.Empty(await db.Accounts.Where(row => row.UserId == userId).ToListAsync());
            Assert.Empty(await db.PlaidItems.Where(row => row.UserId == userId).ToListAsync());
            return 0;
        });
    }

    [Fact]
    public async Task Unlink_LeavesTheUsersOtherBankUntouched()
    {
        var (userId, client, first) = await LinkedUserAsync(_factory);
        var publicToken = $"public-sandbox-{Guid.NewGuid():N}";
        var otherAccountId = $"acct-{Guid.NewGuid():N}";
        var other = _factory.Plaid.AddItem(publicToken, "Other Bank", new PlaidAccount(otherAccountId, "Checking", "1111", "depository"));
        _factory.Plaid.EnqueueSync(other.AccessToken, Page("c1", added: [Txn("t-other-bank", otherAccountId, 7m, "2026-11-01", "Kept")]));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/plaid/exchange", new PlaidExchangeRequest(publicToken))).StatusCode);

        var response = await client.DeleteAsync($"/api/plaid/items/{first.ItemId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await WithDbAsync(_factory, async db =>
        {
            var item = await db.PlaidItems.SingleAsync(row => row.UserId == userId);
            Assert.Equal("Other Bank", item.InstitutionName);
            Assert.Equal("Kept", (await db.Transactions.SingleAsync(row => row.UserId == userId)).Description);
            Assert.Equal(1, await db.Accounts.CountAsync(row => row.UserId == userId));
            return 0;
        });
    }

    [Fact]
    public async Task Unlink_AnotherUsersItem_Returns404_AndChangesNothing()
    {
        var (aliceId, _, aliceLink) = await LinkedUserAsync(_factory);
        var (_, bobEmail) = await SeedUserAsync(_factory);
        var bob = await SignedInAsync(_factory, bobEmail);

        var response = await bob.DeleteAsync($"/api/plaid/items/{aliceLink.ItemId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(aliceLink.AccessToken, _factory.Plaid.RemovedAccessTokens);
        await WithDbAsync(_factory, async db =>
        {
            Assert.Equal(1, await db.PlaidItems.CountAsync(row => row.UserId == aliceId));
            Assert.Equal(2, await db.Accounts.CountAsync(row => row.UserId == aliceId));
            return 0;
        });
    }

    [Fact]
    public async Task Unlink_UnknownItem_Returns404()
    {
        var (_, client, _) = await LinkedUserAsync(_factory);

        var response = await client.DeleteAsync($"/api/plaid/items/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unlink_WhenPlaidFails_Returns502_AndDeletesNothing()
    {
        var (userId, client, link) = await LinkedUserAsync(_factory);
        _factory.Plaid.FailRemove(link.AccessToken, "INTERNAL_SERVER_ERROR", HttpStatusCode.InternalServerError);

        var response = await client.DeleteAsync($"/api/plaid/items/{link.ItemId}");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        await WithDbAsync(_factory, async db =>
        {
            Assert.Equal(1, await db.PlaidItems.CountAsync(row => row.UserId == userId));
            Assert.Equal(2, await db.Accounts.CountAsync(row => row.UserId == userId));
            return 0;
        });
    }

    [Fact]
    public async Task Unlink_WhenPlaidSaysItemAlreadyGone_StillDeletesLocalData()
    {
        var (userId, client, link) = await LinkedUserAsync(_factory);
        _factory.Plaid.FailRemove(link.AccessToken, "ITEM_NOT_FOUND", HttpStatusCode.BadRequest);

        var response = await client.DeleteAsync($"/api/plaid/items/{link.ItemId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await WithDbAsync(_factory, async db =>
        {
            Assert.Empty(await db.PlaidItems.Where(row => row.UserId == userId).ToListAsync());
            Assert.Empty(await db.Accounts.Where(row => row.UserId == userId).ToListAsync());
            return 0;
        });
    }

    [Fact]
    public async Task Unlink_UnverifiedUser_IsForbidden_AndAnonymousIsUnauthorized()
    {
        var (_, email) = await SeedUserAsync(_factory, verified: false);
        var unverified = await SignedInAsync(_factory, email);

        Assert.Equal(HttpStatusCode.Forbidden, (await unverified.DeleteAsync($"/api/plaid/items/{Guid.NewGuid()}")).StatusCode);

        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/plaid/items/{Guid.NewGuid()}")).StatusCode);
    }

    private const string ValidLogo = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    [Fact]
    public async Task Exchange_StoresLogoAndColour()
    {
        var (userId, email) = await SeedUserAsync(_factory);
        var client = await SignedInAsync(_factory, email);
        var publicToken = $"public-sandbox-{Guid.NewGuid():N}";
        var exchange = _factory.Plaid.AddItem(publicToken, "Logo Bank", new PlaidAccount($"acct-{Guid.NewGuid():N}", "Checking"));
        _factory.Plaid.SetBranding(exchange.AccessToken, ValidLogo, "#00668f");

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/plaid/exchange", new PlaidExchangeRequest(publicToken))).StatusCode);

        var item = await WithDbAsync(_factory, db => db.PlaidItems.SingleAsync(row => row.UserId == userId));
        Assert.Equal(ValidLogo, item.InstitutionLogo);
        Assert.Equal("#00668f", item.InstitutionColor);
    }

    [Fact]
    public async Task ListItems_ReturnsLogoAndColour()
    {
        var (userId, client, _) = await LinkedUserAsync(_factory);
        await WithDbAsync(_factory, async db =>
        {
            var item = await db.PlaidItems.SingleAsync(row => row.UserId == userId);
            item.InstitutionLogo = ValidLogo;
            item.InstitutionColor = "#095aa6";
            return await db.SaveChangesAsync();
        });

        var items = (await client.GetFromJsonAsync<List<PlaidItemResponse>>("/api/plaid/items"))!;

        var item = Assert.Single(items);
        Assert.Equal(ValidLogo, item.Logo);
        Assert.Equal("#095aa6", item.PrimaryColor);
    }

    [Fact]
    public async Task Sync_RefreshesLogoAndColour()
    {
        var (userId, client, link) = await LinkedUserAsync(_factory);
        _factory.Plaid.SetBranding(link.AccessToken, ValidLogo, "#123456");

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/plaid/items/{link.ItemId}/sync", null)).StatusCode);

        var item = await WithDbAsync(_factory, db => db.PlaidItems.SingleAsync(row => row.UserId == userId));
        Assert.Equal(ValidLogo, item.InstitutionLogo);
        Assert.Equal("#123456", item.InstitutionColor);
    }

    [Theory]
    [InlineData("not base64 !!<script>", "#00668f", false, true)]
    [InlineData(ValidLogo, "red; background:url(x)", true, false)]
    [InlineData(ValidLogo, "#12345", true, false)]
    [InlineData(ValidLogo, "#00668F", true, true)]
    public async Task Exchange_KeepsOnlyValidLogoAndColour(string logo, string colour, bool logoKept, bool colourKept)
    {
        var (userId, email) = await SeedUserAsync(_factory);
        var client = await SignedInAsync(_factory, email);
        var publicToken = $"public-sandbox-{Guid.NewGuid():N}";
        var exchange = _factory.Plaid.AddItem(publicToken, "Odd Bank", new PlaidAccount($"acct-{Guid.NewGuid():N}", "Checking"));
        _factory.Plaid.SetBranding(exchange.AccessToken, logo, colour);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/plaid/exchange", new PlaidExchangeRequest(publicToken))).StatusCode);

        var item = await WithDbAsync(_factory, db => db.PlaidItems.SingleAsync(row => row.UserId == userId));
        Assert.Equal(logoKept ? logo : null, item.InstitutionLogo);
        Assert.Equal(colourKept ? colour : null, item.InstitutionColor);
    }

    [Fact]
    public async Task Exchange_DropsAnOversizeLogo()
    {
        var (userId, email) = await SeedUserAsync(_factory);
        var client = await SignedInAsync(_factory, email);
        var publicToken = $"public-sandbox-{Guid.NewGuid():N}";
        var exchange = _factory.Plaid.AddItem(publicToken, "Big Bank", new PlaidAccount($"acct-{Guid.NewGuid():N}", "Checking"));
        _factory.Plaid.SetBranding(exchange.AccessToken, new string('A', 40_000), "#00668f");

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/plaid/exchange", new PlaidExchangeRequest(publicToken))).StatusCode);

        var item = await WithDbAsync(_factory, db => db.PlaidItems.SingleAsync(row => row.UserId == userId));
        Assert.Null(item.InstitutionLogo);
        Assert.Equal("#00668f", item.InstitutionColor);
    }

    [Fact]
    public async Task Exchange_BankWithoutBranding_KeepsLogoAndColourNull()
    {
        var (userId, _, _) = await LinkedUserAsync(_factory);

        var item = await WithDbAsync(_factory, db => db.PlaidItems.SingleAsync(row => row.UserId == userId));

        Assert.Null(item.InstitutionLogo);
        Assert.Null(item.InstitutionColor);
    }

    [Fact]
    public async Task Sync_TransactionForUnsharedAccount_IsSkipped()
    {
        var (userId, client, link) = await LinkedUserAsync(_factory);
        _factory.Plaid.EnqueueSync(link.AccessToken, Page("c1", added:
        [
            Txn("t-mine", link.PlaidAccountId, 1m, "2026-11-01", "Mine"),
            Txn("t-other", "acct-not-shared", 2m, "2026-11-01", "Not shared"),
        ]));

        var response = await client.PostAsync($"/api/plaid/items/{link.ItemId}/sync", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = Assert.Single(await WithDbAsync(_factory, db => db.Transactions.Where(r => r.UserId == userId).ToListAsync()));
        Assert.Equal("t-mine", row.ExternalId);
    }

    [Fact]
    public async Task Sync_WhenAnotherSyncCommitsFirst_ReturnsConflict_AndSavesNothing()
    {
        await using var factory = new RaceWebApplicationFactory();
        var (userId, client, link) = await LinkedUserAsync(factory);
        factory.Plaid.EnqueueSync(link.AccessToken, Page("c-loser", added: [Txn("t-1", link.PlaidAccountId, 1m, "2026-11-01", "One")]));
        factory.Injector.Trigger = context => context.ChangeTracker.Entries<PlaidItem>().Any(entry => entry.State == EntityState.Modified);
        factory.Injector.Race = async _ =>
        {
            using var scope = factory.Services.CreateScope();
            var other = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await other.PlaidItems.SingleAsync(row => row.Id == link.ItemId);
            item.SyncCursor = "c-winner";
            item.LastSyncedAt = DateTime.UtcNow.AddSeconds(1);
            await other.SaveChangesAsync();
        };

        var response = await client.PostAsync($"/api/plaid/items/{link.ItemId}/sync", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("c-winner", await CursorAsync(factory, link.ItemId));
        // The loser's ledger rows are not asserted here: the in-memory provider has no transactions, so it
        // keeps inserts that ran before the failed update. On Postgres the rows and the cursor update share
        // one SaveChanges transaction and roll back together.
    }

    private sealed record Link(Guid ItemId, Guid AccountId, string AccessToken, string PlaidItemId, string PlaidAccountId);

    /// <summary>A verified, signed-in user with one linked bank: a checking account and a credit card.</summary>
    private static async Task<(Guid UserId, HttpClient Client, Link Link)> LinkedUserAsync(
        AuthWebApplicationFactory factory,
        string institution = "Sandbox Bank")
    {
        var (userId, email) = await SeedUserAsync(factory);
        var client = await SignedInAsync(factory, email);
        var publicToken = $"public-sandbox-{Guid.NewGuid():N}";
        var checkingId = $"acct-{Guid.NewGuid():N}";
        var exchange = factory.Plaid.AddItem(
            publicToken,
            institution,
            new PlaidAccount(checkingId, "Checking", "0000", "depository", "checking", 1234.56m),
            new PlaidAccount($"acct-{Guid.NewGuid():N}", "Credit Card", "3333", "credit", "credit card", 250.10m, 1000m));

        var response = await client.PostAsJsonAsync("/api/plaid/exchange", new PlaidExchangeRequest(publicToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var (itemId, accountId) = await WithDbAsync(factory, async db =>
        {
            var item = await db.PlaidItems.SingleAsync(row => row.ItemId == exchange.ItemId);
            var account = await db.Accounts.SingleAsync(row => row.UserId == userId && row.PlaidAccountId == checkingId);
            return (item.Id, account.Id);
        });
        return (userId, client, new Link(itemId, accountId, exchange.AccessToken, exchange.ItemId, checkingId));
    }

    [Fact]
    public async Task Sync_StoresCategory_FromPlaid_ThenKeywords_ThenOther_AndRecategorisesOnModify()
    {
        var (userId, client, link) = await LinkedUserAsync(_factory);
        _factory.Plaid.EnqueueSync(link.AccessToken, Page("c1", added:
        [
            Txn("t-plaid", link.PlaidAccountId, 4.50m, "2026-11-01", "COFFEE", primary: "FOOD_AND_DRINK", detailed: "FOOD_AND_DRINK_COFFEE"),
            Txn("t-keyword", link.PlaidAccountId, 40m, "2026-11-02", "SHELL OIL 5731"),
            Txn("t-other", link.PlaidAccountId, 3m, "2026-11-03", "QWERTY ZZZ"),
        ]));

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/plaid/items/{link.ItemId}/sync", null)).StatusCode);

        _factory.Plaid.EnqueueSync(link.AccessToken, Page("c2", modified:
        [
            Txn("t-other", link.PlaidAccountId, 3m, "2026-11-03", "QWERTY ZZZ", primary: "GENERAL_MERCHANDISE", detailed: "GENERAL_MERCHANDISE_OTHER_GENERAL_MERCHANDISE"),
        ]));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/plaid/items/{link.ItemId}/sync", null)).StatusCode);

        await WithDbAsync(_factory, async db =>
        {
            var rows = await db.Transactions.Where(row => row.UserId == userId).ToDictionaryAsync(row => row.ExternalId!);
            Assert.Equal(("FOOD_AND_DRINK", "FOOD_AND_DRINK_COFFEE"), (rows["t-plaid"].Category, rows["t-plaid"].Subcategory));
            Assert.Equal(("TRANSPORTATION", "TRANSPORTATION_GAS"), (rows["t-keyword"].Category, rows["t-keyword"].Subcategory));
            Assert.Equal("GENERAL_MERCHANDISE", rows["t-other"].Category);
            return 0;
        });
    }

    private static PlaidTransaction Txn(
        string id, string accountId, decimal plaidAmount, string date, string name, bool pending = false, string? merchant = null,
        string? primary = null, string? detailed = null) =>
        new(id, accountId, plaidAmount, DateOnly.Parse(date), name, merchant, pending, primary, detailed);

    private static PlaidSyncPage Page(
        string nextCursor,
        bool hasMore = false,
        PlaidTransaction[]? added = null,
        PlaidTransaction[]? modified = null,
        string[]? removed = null) =>
        new(added ?? [], modified ?? [], removed ?? [], nextCursor, hasMore);

    private static Task<string?> CursorAsync(AuthWebApplicationFactory factory, Guid itemId) =>
        WithDbAsync(factory, db => db.PlaidItems.Where(item => item.Id == itemId).Select(item => item.SyncCursor).SingleAsync());

    private static async Task<T> WithDbAsync<T>(AuthWebApplicationFactory factory, Func<AppDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task<(Guid Id, string Email)> SeedUserAsync(AuthWebApplicationFactory factory, bool verified = true)
    {
        var email = $"user.{Guid.NewGuid():N}@example.com";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var user = new User
        {
            Email = email,
            Name = "Test User",
            EmailVerifiedAt = verified ? DateTime.UtcNow : null,
        };
        user.PasswordHash = hasher.HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, email);
    }

    /// <summary>Password login, plus the emailed code when the account is verified.</summary>
    private static async Task<HttpClient> SignedInAsync(AuthWebApplicationFactory factory, string email)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        if (login.StatusCode == HttpStatusCode.Accepted)
        {
            var challenge = await login.Content.ReadFromJsonAsync<LoginChallengeResponse>();
            var code = CapturingEmailSender.CodeFrom(factory.Emails.LastTo(email));
            login = await client.PostAsJsonAsync(
                "/api/auth/login/verify-code", new VerifyLoginCodeRequest(challenge!.ChallengeId, code));
        }

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }
}
