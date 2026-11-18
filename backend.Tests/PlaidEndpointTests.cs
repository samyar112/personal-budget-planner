using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
 * 1. Link token without a session returns 401.
 * 2. An unverified user is refused with 403 on both endpoints, and Plaid is never called.
 * 3. A verified user gets a link token minted for their own user id.
 * 4. Exchange stores the item and its accounts against the caller and returns their names.
 * 5. The stored access token is ciphertext that decrypts back to the original for the owner only.
 * 6. Neither response body carries the access token, item id or Plaid account ids.
 * 7. Exchanging an item whose accounts already exist updates them instead of duplicating.
 * 8. Another user's exchange never touches the caller's item or accounts.
 * 9. A public token Plaid refuses returns 400 and stores nothing.
 * 10. A blank public token returns 400 without calling Plaid.
 * 11. A Plaid encryption key that is not 32 bytes stops the app at startup.
 * 12. A ciphertext tampered with fails to decrypt.
 */
public sealed class PlaidEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";

    private readonly AuthWebApplicationFactory _factory;

    public PlaidEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LinkToken_WithoutSession_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().PostAsync("/api/plaid/link-token", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnverifiedUser_IsForbidden_AndPlaidIsNotCalled()
    {
        var (userId, email) = await SeedUserAsync(verified: false);
        var publicToken = NewPublicToken();
        _factory.Plaid.AddItem(publicToken, "Sandbox Bank", (NewAccountId(), "Checking"));
        var client = await SignedInAsync(email);

        var linkToken = await client.PostAsync("/api/plaid/link-token", null);
        var exchange = await client.PostAsJsonAsync("/api/plaid/exchange", new PlaidExchangeRequest(publicToken));

        Assert.Equal(HttpStatusCode.Forbidden, linkToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, exchange.StatusCode);
        Assert.DoesNotContain(userId, _factory.Plaid.LinkTokenUsers);
        Assert.False(await WithDbAsync(db => db.PlaidItems.AnyAsync(item => item.UserId == userId)));
    }

    [Fact]
    public async Task LinkToken_IsMintedForCaller()
    {
        var (userId, email) = await SeedUserAsync();
        var client = await SignedInAsync(email);

        var response = await client.PostAsync("/api/plaid/link-token", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PlaidLinkTokenResponse>();
        Assert.Equal($"link-sandbox-{userId:N}", body!.LinkToken);
        Assert.Contains(userId, _factory.Plaid.LinkTokenUsers);
    }

    [Fact]
    public async Task Exchange_StoresItemAndAccounts_ForCaller()
    {
        var (userId, email) = await SeedUserAsync();
        var publicToken = NewPublicToken();
        var exchange = _factory.Plaid.AddItem(
            publicToken, "Sandbox Bank", (NewAccountId(), "Plaid Checking"), (NewAccountId(), "Plaid Saving"));
        var client = await SignedInAsync(email);

        var response = await client.PostAsJsonAsync("/api/plaid/exchange", new PlaidExchangeRequest(publicToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<PlaidExchangeResponse>())!;
        Assert.Equal("Sandbox Bank", body.Institution);
        Assert.Equal(["Plaid Checking", "Plaid Saving"], body.Accounts.Select(account => account.Name).Order());

        await WithDbAsync(async db =>
        {
            var item = await db.PlaidItems.SingleAsync(row => row.ItemId == exchange.ItemId);
            Assert.Equal(userId, item.UserId);
            Assert.Equal("Sandbox Bank", item.InstitutionName);
            var accounts = await db.Accounts.Where(account => account.PlaidItemId == item.Id).ToListAsync();
            Assert.Equal(2, accounts.Count);
            Assert.All(accounts, account => Assert.Equal(userId, account.UserId));
            Assert.All(accounts, account => Assert.Equal("Sandbox Bank", account.Institution));
            Assert.Equal(body.Accounts.Select(a => a.Id).Order(), accounts.Select(a => a.Id).Order());
            return 0;
        });
    }

    [Fact]
    public async Task Exchange_StoresAccessTokenEncrypted_BoundToOwner()
    {
        var (userId, email) = await SeedUserAsync();
        var publicToken = NewPublicToken();
        var exchange = _factory.Plaid.AddItem(publicToken, "Sandbox Bank", (NewAccountId(), "Checking"));
        var client = await SignedInAsync(email);

        await client.PostAsJsonAsync("/api/plaid/exchange", new PlaidExchangeRequest(publicToken));

        var stored = await WithDbAsync(db =>
            db.PlaidItems.Where(item => item.ItemId == exchange.ItemId).Select(item => item.EncryptedAccessToken).SingleAsync());
        Assert.DoesNotContain(exchange.AccessToken, stored);
        Assert.DoesNotContain("access-sandbox", stored);

        var protector = _factory.Services.GetRequiredService<AccessTokenProtector>();
        Assert.Equal(exchange.AccessToken, protector.Unprotect(stored, userId));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(stored, Guid.NewGuid()));
    }

    [Fact]
    public async Task Responses_NeverCarryAccessTokenOrPlaidIds()
    {
        var (_, email) = await SeedUserAsync();
        var publicToken = NewPublicToken();
        var accountId = NewAccountId();
        var exchange = _factory.Plaid.AddItem(publicToken, "Sandbox Bank", (accountId, "Checking"));
        var client = await SignedInAsync(email);

        var linkRaw = await (await client.PostAsync("/api/plaid/link-token", null)).Content.ReadAsStringAsync();
        var exchangeResponse = await client.PostAsJsonAsync("/api/plaid/exchange", new PlaidExchangeRequest(publicToken));
        var exchangeRaw = await exchangeResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, exchangeResponse.StatusCode);
        foreach (var raw in new[] { linkRaw, exchangeRaw })
        {
            Assert.DoesNotContain(exchange.AccessToken, raw);
            Assert.DoesNotContain("access", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(exchange.ItemId, raw);
            Assert.DoesNotContain(accountId, raw);
        }
    }

    [Fact]
    public async Task Exchange_ForExistingAccounts_UpdatesInsteadOfDuplicating()
    {
        var (userId, email) = await SeedUserAsync();
        var accountId = NewAccountId();
        var first = NewPublicToken();
        var second = NewPublicToken();
        _factory.Plaid.AddItem(first, "Sandbox Bank", (accountId, "Old name"));
        _factory.Plaid.AddItem(second, "Sandbox Bank", (accountId, "New name"));
        var client = await SignedInAsync(email);

        var one = await client.PostAsJsonAsync("/api/plaid/exchange", new PlaidExchangeRequest(first));
        var two = await client.PostAsJsonAsync("/api/plaid/exchange", new PlaidExchangeRequest(second));

        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.Equal(HttpStatusCode.OK, two.StatusCode);
        var accounts = await WithDbAsync(db =>
            db.Accounts.Where(account => account.UserId == userId && account.PlaidAccountId == accountId).ToListAsync());
        var account = Assert.Single(accounts);
        Assert.Equal("New name", account.Name);
    }

    [Fact]
    public async Task Exchange_ByAnotherUser_NeverTouchesCallersRows()
    {
        var (aliceId, aliceEmail) = await SeedUserAsync();
        var (bobId, bobEmail) = await SeedUserAsync();
        var sharedAccountId = NewAccountId();
        var aliceToken = NewPublicToken();
        var bobToken = NewPublicToken();
        _factory.Plaid.AddItem(aliceToken, "Alice Bank", (sharedAccountId, "Alice checking"));
        _factory.Plaid.AddItem(bobToken, "Bob Bank", (sharedAccountId, "Bob checking"));

        var alice = await SignedInAsync(aliceEmail);
        Assert.Equal(HttpStatusCode.OK,
            (await alice.PostAsJsonAsync("/api/plaid/exchange", new PlaidExchangeRequest(aliceToken))).StatusCode);
        var bob = await SignedInAsync(bobEmail);
        var bobResponse = await bob.PostAsJsonAsync("/api/plaid/exchange", new PlaidExchangeRequest(bobToken));
        Assert.Equal(HttpStatusCode.OK, bobResponse.StatusCode);

        await WithDbAsync(async db =>
        {
            var aliceAccount = await db.Accounts.SingleAsync(account => account.UserId == aliceId);
            Assert.Equal("Alice checking", aliceAccount.Name);
            Assert.Equal("Alice Bank", aliceAccount.Institution);
            var bobAccount = await db.Accounts.SingleAsync(account => account.UserId == bobId);
            Assert.Equal("Bob checking", bobAccount.Name);
            Assert.NotEqual(aliceAccount.Id, bobAccount.Id);
            Assert.Equal(1, await db.PlaidItems.CountAsync(item => item.UserId == aliceId));
            return 0;
        });

        var bobBody = (await bobResponse.Content.ReadFromJsonAsync<PlaidExchangeResponse>())!;
        Assert.DoesNotContain("Alice checking", bobBody.Accounts.Select(account => account.Name));
    }

    [Fact]
    public async Task Exchange_WithRefusedPublicToken_ReturnsBadRequest_AndStoresNothing()
    {
        var (userId, email) = await SeedUserAsync();
        var client = await SignedInAsync(email);

        var response = await client.PostAsJsonAsync("/api/plaid/exchange", new PlaidExchangeRequest("public-sandbox-unknown"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await WithDbAsync(db => db.PlaidItems.AnyAsync(item => item.UserId == userId)));
        Assert.False(await WithDbAsync(db => db.Accounts.AnyAsync(account => account.UserId == userId)));
    }

    [Fact]
    public async Task Exchange_WithBlankPublicToken_ReturnsBadRequest()
    {
        var (_, email) = await SeedUserAsync();
        var client = await SignedInAsync(email);

        var response = await client.PostAsJsonAsync("/api/plaid/exchange", new PlaidExchangeRequest("   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void Startup_WithShortEncryptionKey_Fails()
    {
        using var factory = new BadPlaidKeyWebApplicationFactory();

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void Unprotect_TamperedCiphertext_Throws()
    {
        var protector = _factory.Services.GetRequiredService<AccessTokenProtector>();
        var userId = Guid.NewGuid();
        var stored = protector.Protect("access-sandbox-secret", userId);
        var bytes = Convert.FromBase64String(stored["v1:".Length..]);
        bytes[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(
            () => protector.Unprotect("v1:" + Convert.ToBase64String(bytes), userId));
    }

    private static string NewPublicToken() => $"public-sandbox-{Guid.NewGuid():N}";

    private static string NewAccountId() => $"acct-{Guid.NewGuid():N}";

    private async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<(Guid Id, string Email)> SeedUserAsync(bool verified = true)
    {
        var email = $"user.{Guid.NewGuid():N}@example.com";
        using var scope = _factory.Services.CreateScope();
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
    private async Task<HttpClient> SignedInAsync(string email)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        if (login.StatusCode == HttpStatusCode.Accepted)
        {
            var challenge = await login.Content.ReadFromJsonAsync<LoginChallengeResponse>();
            var code = CapturingEmailSender.CodeFrom(_factory.Emails.LastTo(email));
            login = await client.PostAsJsonAsync(
                "/api/auth/login/verify-code", new VerifyLoginCodeRequest(challenge!.ChallengeId, code));
        }

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }
}
