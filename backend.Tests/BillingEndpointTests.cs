using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using backend.Data;
using backend.DTOs;
using backend.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

/*
 * Test cases:
 * 1. Checkout without a session returns 401; an unverified user gets 403.
 * 2. Checkout creates one Stripe customer, stores it, and reuses it on the next call.
 * 3. Checkout for a subscribed user returns 409.
 * 4. Portal before any checkout returns 404.
 * 5. A webhook with a bad signature returns 400 and changes nothing.
 * 6. A signed subscription event sets the status; /me then reports subscribed, and an expired trial gets tips.
 * 7. A cancellation event removes access: tips return 402 again.
 * 8. An older event arriving after a newer one is ignored.
 * 9. An event for another customer never changes this user.
 * 10. A live secret key fails at boot.
 */
public sealed class BillingEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";

    private readonly AuthWebApplicationFactory _factory;

    public BillingEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Advisor.Reset();
    }

    [Fact]
    public async Task Checkout_RequiresVerifiedSession()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsync("/api/billing/checkout", null)).StatusCode);

        var (_, email) = await SeedUserAsync(verified: false);
        var client = await SignedInAsync(email);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/billing/checkout", null)).StatusCode);
    }

    [Fact]
    public async Task Checkout_CreatesCustomerOnce_AndReturnsStripeUrl()
    {
        var (userId, email) = await SeedUserAsync();
        var client = await SignedInAsync(email);

        var first = await client.PostAsync("/api/billing/checkout", null);
        var second = await client.PostAsync("/api/billing/checkout", null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(first.Headers.CacheControl?.NoStore);
        var user = await FindAsync(userId);
        Assert.NotNull(user.StripeCustomerId);
        Assert.Equal($"https://checkout.stripe.test/{user.StripeCustomerId}", (await first.Content.ReadFromJsonAsync<BillingRedirectResponse>())!.Url);
        Assert.Equal($"https://checkout.stripe.test/{user.StripeCustomerId}", (await second.Content.ReadFromJsonAsync<BillingRedirectResponse>())!.Url);
    }

    [Fact]
    public async Task Checkout_WhenSubscribed_ReturnsConflict()
    {
        var (userId, email) = await SeedUserAsync(customerId: NewCustomerId());
        await UpdateAsync(userId, user => user.SubscriptionStatus = "active");
        var client = await SignedInAsync(email);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/billing/checkout", null)).StatusCode);
    }

    [Fact]
    public async Task Portal_BeforeCheckout_ReturnsNotFound()
    {
        var (_, email) = await SeedUserAsync();
        var client = await SignedInAsync(email);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/billing/portal", null)).StatusCode);
    }

    [Fact]
    public async Task Webhook_WithBadSignature_IsRejected_AndChangesNothing()
    {
        var customerId = NewCustomerId();
        var (userId, _) = await SeedUserAsync(customerId: customerId);
        var json = SubscriptionEvent("customer.subscription.created", customerId, "active", DateTimeOffset.UtcNow);

        var response = await PostWebhookAsync(json, "whsec_wrong_secret");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await FindAsync(userId)).SubscriptionStatus);
    }

    [Fact]
    public async Task Webhook_Active_UnlocksTips_ForExpiredTrial()
    {
        var customerId = NewCustomerId();
        var (userId, email) = await SeedUserAsync(customerId: customerId, dateEntered: DateTime.UtcNow.AddDays(-30));
        var client = await SignedInAsync(email);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/api/ai/tips", new GenerateTipsRequest(true))).StatusCode);

        var response = await PostWebhookAsync(SubscriptionEvent("customer.subscription.created", customerId, "active", DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("active", (await FindAsync(userId)).SubscriptionStatus);
        Assert.True((await client.GetFromJsonAsync<MeResponse>("/api/auth/me"))!.Subscribed);
        Assert.NotEqual(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/api/ai/tips", new GenerateTipsRequest(true))).StatusCode);
    }

    [Fact]
    public async Task Webhook_Deleted_RemovesAccess()
    {
        var customerId = NewCustomerId();
        var (userId, email) = await SeedUserAsync(customerId: customerId, dateEntered: DateTime.UtcNow.AddDays(-30));
        var now = DateTimeOffset.UtcNow;
        await PostWebhookAsync(SubscriptionEvent("customer.subscription.created", customerId, "active", now.AddMinutes(-2)));

        await PostWebhookAsync(SubscriptionEvent("customer.subscription.deleted", customerId, "canceled", now.AddMinutes(-1)));

        Assert.Equal("canceled", (await FindAsync(userId)).SubscriptionStatus);
        var client = await SignedInAsync(email);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/api/ai/tips", new GenerateTipsRequest(true))).StatusCode);
    }

    [Fact]
    public async Task Webhook_OlderEventArrivingLate_IsIgnored()
    {
        var customerId = NewCustomerId();
        var (userId, _) = await SeedUserAsync(customerId: customerId);
        var now = DateTimeOffset.UtcNow;

        await PostWebhookAsync(SubscriptionEvent("customer.subscription.deleted", customerId, "canceled", now.AddMinutes(-1)));
        var late = await PostWebhookAsync(SubscriptionEvent("customer.subscription.updated", customerId, "active", now.AddMinutes(-2)));

        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
        Assert.Equal("canceled", (await FindAsync(userId)).SubscriptionStatus);
    }

    [Fact]
    public async Task Webhook_ForAnotherCustomer_DoesNotTouchThisUser()
    {
        var (userId, _) = await SeedUserAsync(customerId: NewCustomerId());
        var (otherId, _) = await SeedUserAsync(customerId: NewCustomerId());
        var other = await FindAsync(otherId);

        await PostWebhookAsync(SubscriptionEvent("customer.subscription.created", other.StripeCustomerId!, "active", DateTimeOffset.UtcNow));

        Assert.Null((await FindAsync(userId)).SubscriptionStatus);
        Assert.Equal("active", (await FindAsync(otherId)).SubscriptionStatus);
    }

    [Fact]
    public async Task LiveSecretKey_FailsAtBoot()
    {
        await using var factory = new LiveStripeKeyWebApplicationFactory();

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    private static string NewCustomerId() => $"cus_{Guid.NewGuid():N}";

    private static string SubscriptionEvent(string type, string customerId, string status, DateTimeOffset created) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            id = $"evt_{Guid.NewGuid():N}",
            @object = "event",
            api_version = "2025-01-01",
            created = created.ToUnixTimeSeconds(),
            type,
            data = new { @object = new { id = "sub_test", @object = "subscription", customer = customerId, status } },
        });

    /// <summary>Signs the body the way Stripe does: HMAC-SHA256 of "timestamp.body" with the endpoint secret.</summary>
    private async Task<HttpResponseMessage> PostWebhookAsync(string json, string secret = FakeBillingGateway.WebhookSecret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{json}")));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", $"t={timestamp},v1={signature}");
        return await _factory.CreateClient().SendAsync(request);
    }

    private async Task<(Guid Id, string Email)> SeedUserAsync(
        bool verified = true, string? customerId = null, DateTime? dateEntered = null)
    {
        var email = $"billing.{Guid.NewGuid():N}@example.com";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var user = new User
        {
            Email = email,
            Name = "Billing Tester",
            EmailVerifiedAt = verified ? DateTime.UtcNow : null,
            StripeCustomerId = customerId,
            DateEntered = dateEntered ?? DateTime.UtcNow,
        };
        user.PasswordHash = hasher.HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, email);
    }

    private async Task<User> FindAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.AsNoTracking().SingleAsync(user => user.Id == id);
    }

    private async Task UpdateAsync(Guid id, Action<User> change)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        change(await db.Users.SingleAsync(user => user.Id == id));
        await db.SaveChangesAsync();
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

public sealed class LiveStripeKeyWebApplicationFactory : AuthWebApplicationFactory
{
    protected override IReadOnlyDictionary<string, string?> ExtraConfig =>
        new Dictionary<string, string?> { ["Stripe:SecretKey"] = "sk_live_should_never_boot" };
}
