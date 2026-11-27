using backend.Data;
using backend.Extensions;
using backend.Models;
using backend.Services;
using backend.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace backend.Tests;

/// <summary>
/// Boots the real API with an isolated in-memory database for each factory instance.
/// </summary>
public class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    protected string DatabaseName { get; } = $"AuthTests-{Guid.NewGuid()}";

    protected virtual IReadOnlyDictionary<string, string?> ExtraConfig =>
        new Dictionary<string, string?>();

    /// <summary>
    /// Most tests assert on mail right after the request, so by default the queue runs jobs
    /// inline. Queue tests turn this off to exercise the real queue and worker.
    /// </summary>
    protected virtual bool UseInlineEmailQueue => true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Jwt:Key lives in gitignored Development settings; provide a test key here.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-jwt-signing-key-at-least-32-chars!!",
                ["Jwt:Issuer"] = "VaultBudget",
                ["Jwt:Audience"] = "VaultBudget",
                ["Jwt:ExpiresMinutes"] = "60",
                ["Jwt:RefreshExpiresDays"] = "7",
                ["Jwt:RefreshGraceSeconds"] = "20",
                // Idle window may not be shorter than the 60-minute access token above.
                ["Jwt:IdleTimeoutMinutes"] = "60",
                // High default so normal auth tests do not trip IP rate limiting.
                ["AuthSecurity:LoginPermitLimit"] = "1000",
                ["AuthSecurity:LoginWindowSeconds"] = "60",
                ["AuthSecurity:MaxFailedLoginAttempts"] = "5",
                ["AuthSecurity:LockoutMinutes"] = "15",
                ["AuthSecurity:ResendPermitLimit"] = "1000",
                ["AuthSecurity:ResendWindowSeconds"] = "60",
                // No cooldown by default so resend tests need not wait; cooldown tests override it.
                ["AuthSecurity:LoginCodeResendCooldownSeconds"] = "0",
                // Google verification is faked below; this only satisfies startup validation.
                ["Google:ClientId"] = "test-google-client-id.apps.googleusercontent.com",
                // Plaid itself is faked below; these only satisfy startup validation.
                ["Plaid:ClientId"] = "test-client-id",
                ["Plaid:Secret"] = "test-secret",
                ["Plaid:Environment"] = "sandbox",
                ["Plaid:TokenEncryptionKey"] = Convert.ToBase64String(new byte[32]),
                // Stripe calls are faked below; the webhook secret is real so tests can sign events.
                ["Stripe:SecretKey"] = "sk_test_fake",
                ["Stripe:WebhookSecret"] = FakeBillingGateway.WebhookSecret,
                ["Stripe:PriceId"] = "price_test",
            };

            foreach (var (key, value) in ExtraConfig)
            {
                values[key] = value;
            }

            config.AddInMemoryCollection(values);
        });

        // Runs after Program.cs service registration so we can replace Npgsql cleanly.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(DatabaseName));

            // Capture outgoing mail so tests can read the verification token.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<CapturingEmailSender>();
            services.AddSingleton<IEmailSender>(provider => provider.GetRequiredService<CapturingEmailSender>());

            if (UseInlineEmailQueue)
            {
                services.RemoveAll<IAccountEmailQueue>();
                services.AddSingleton<IAccountEmailQueue, InlineAccountEmailQueue>();
            }

            // No test may reach the real Plaid API.
            services.RemoveAll<IPlaidClient>();
            services.AddSingleton<FakePlaidClient>();
            services.AddSingleton<IPlaidClient>(provider => provider.GetRequiredService<FakePlaidClient>());

            // No test may reach an AI provider.
            services.RemoveAll<IAdvisorModel>();
            services.AddSingleton<FakeAdvisorModel>();
            services.AddSingleton<IAdvisorModel>(provider => provider.GetRequiredService<FakeAdvisorModel>());

            // No test may reach Google; each fake token maps to an identity a test registers.
            services.RemoveAll<IGoogleTokenVerifier>();
            services.AddSingleton<FakeGoogleTokenVerifier>();
            services.AddSingleton<IGoogleTokenVerifier>(provider => provider.GetRequiredService<FakeGoogleTokenVerifier>());

            // No test may reach Stripe.
            services.RemoveAll<IBillingGateway>();
            services.AddSingleton<FakeBillingGateway>();
            services.AddSingleton<IBillingGateway>(provider => provider.GetRequiredService<FakeBillingGateway>());

            // Stand-in for a ledger endpoint, which does not exist yet.
            services.AddControllers().AddApplicationPart(typeof(VerifiedOnlyProbeController).Assembly);
        });
    }

    public CapturingEmailSender Emails => Services.GetRequiredService<CapturingEmailSender>();

    public FakeGoogleTokenVerifier Google => Services.GetRequiredService<FakeGoogleTokenVerifier>();

    public FakePlaidClient Plaid => Services.GetRequiredService<FakePlaidClient>();

    public FakeAdvisorModel Advisor => Services.GetRequiredService<FakeAdvisorModel>();

    public FakeBillingGateway Billing => Services.GetRequiredService<FakeBillingGateway>();
}

/// <summary>Stands in for Stripe. Each customer id is new; every call is recorded.</summary>
public sealed class FakeBillingGateway : IBillingGateway
{
    public const string WebhookSecret = "whsec_test_secret";

    public System.Collections.Concurrent.ConcurrentQueue<string> CustomersCreated { get; } = new();

    public Task<string> CreateCustomerAsync(Guid userId, CancellationToken cancellationToken)
    {
        var id = $"cus_{Guid.NewGuid():N}";
        CustomersCreated.Enqueue(id);
        return Task.FromResult(id);
    }

    public Task<string> CreateCheckoutUrlAsync(string customerId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult($"https://checkout.stripe.test/{customerId}");

    public Task<string> CreatePortalUrlAsync(string customerId, CancellationToken cancellationToken) =>
        Task.FromResult($"https://billing.stripe.test/{customerId}");
}

/// <summary>
/// Stands in for the AI provider. By default it echoes the first three candidates' own wording, which is
/// always valid. A test can set <see cref="Reply"/> to return something else, or <see cref="Fail"/> to throw
/// the way an outage or a withdrawn free model does. Every user message is kept for privacy assertions.
/// </summary>
public sealed class FakeAdvisorModel : IAdvisorModel
{
    public const string ModelName = "fake/advisor-model";

    public System.Collections.Concurrent.ConcurrentQueue<string> Requests { get; } = new();

    public Func<string, string>? Reply { get; set; }

    public bool Fail { get; set; }

    public bool IsConfigured => true;

    public Task<AdvisorModelReply> CompleteAsync(string systemPrompt, string userContent, CancellationToken cancellationToken)
    {
        Requests.Enqueue(userContent);
        if (Fail)
        {
            throw new AdvisorModelException("AI provider returned 503.");
        }

        return Task.FromResult(new AdvisorModelReply((Reply ?? Echo)(userContent), ModelName));
    }

    /// <summary>Resets to the default echo behaviour. Call at the start of each test that uses the fake.</summary>
    public void Reset()
    {
        Reply = null;
        Fail = false;
    }

    public static string Echo(string userContent)
    {
        var candidates = System.Text.Json.Nodes.JsonNode.Parse(userContent)!["candidates"]!.AsArray();
        var tips = candidates.Take(3).Select(candidate => new
        {
            candidateId = candidate!["id"]!.GetValue<string>(),
            title = candidate["title"]!.GetValue<string>(),
            text = candidate["text"]!.GetValue<string>(),
        });
        return System.Text.Json.JsonSerializer.Serialize(new { tips });
    }
}

/// <summary>
/// Stands in for Google's token check. A token a test registered verifies to its identity; any
/// other token is untrusted, the way a forged, expired or wrong-audience token is.
/// </summary>
public sealed class FakeGoogleTokenVerifier : IGoogleTokenVerifier
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, GoogleIdentity> _tokens = new();

    public System.Collections.Concurrent.ConcurrentQueue<string> Verified { get; } = new();

    /// <summary>Registers an ID token that verifies to <paramref name="identity"/>.</summary>
    public string AddToken(GoogleIdentity identity)
    {
        var token = $"google-id-token-{Guid.NewGuid():N}";
        _tokens[token] = identity;
        return token;
    }

    public Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken)
    {
        Verified.Enqueue(idToken);
        return Task.FromResult(_tokens.TryGetValue(idToken, out var identity) ? identity : null);
    }
}

/// <summary>
/// Stands in for Plaid. Each public token maps to an item a test registers up front; an unknown
/// public token is refused the way Plaid refuses one (400 INVALID_PUBLIC_TOKEN).
/// </summary>
public sealed class FakePlaidClient : IPlaidClient
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (PlaidExchangeResult Exchange, PlaidItemDetails Details)> _items = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PlaidItemDetails> _byAccessToken = new();

    public System.Collections.Concurrent.ConcurrentBag<Guid> LinkTokenUsers { get; } = [];

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentQueue<Func<PlaidSyncPage>>> _syncPages = new();

    /// <summary>Every sync call, in order, with the cursor it was given.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<(string AccessToken, string? Cursor)> SyncCalls { get; } = new();

    /// <summary>Registers what Plaid will return for <paramref name="publicToken"/>.</summary>
    public PlaidExchangeResult AddItem(string publicToken, string institution, params (string Id, string Name)[] accounts) =>
        AddItem(publicToken, institution, accounts.Select(account => new PlaidAccount(account.Id, account.Name)).ToArray());

    public PlaidExchangeResult AddItem(string publicToken, string institution, params PlaidAccount[] accounts)
    {
        var exchange = new PlaidExchangeResult($"access-sandbox-{Guid.NewGuid():N}", $"item-{Guid.NewGuid():N}");
        var details = new PlaidItemDetails("ins_test", institution, accounts);
        _items[publicToken] = (exchange, details);
        _byAccessToken[exchange.AccessToken] = details;
        return exchange;
    }

    /// <summary>Changes the logo and colour Plaid returns for this access token, as if the bank's branding changed.</summary>
    public void SetBranding(string accessToken, string? logo, string? primaryColor)
    {
        var current = _byAccessToken[accessToken];
        _byAccessToken[accessToken] = current with { Logo = logo, PrimaryColor = primaryColor };
    }

    /// <summary>Queues the next page <see cref="SyncTransactionsAsync"/> returns for this access token.</summary>
    public void EnqueueSync(string accessToken, PlaidSyncPage page) =>
        _syncPages.GetOrAdd(accessToken, _ => new()).Enqueue(() => page);

    /// <summary>Queues a Plaid error for the next sync call on this access token.</summary>
    public void EnqueueSyncFailure(
        string accessToken,
        string errorCode = "INTERNAL_SERVER_ERROR",
        System.Net.HttpStatusCode status = System.Net.HttpStatusCode.InternalServerError) =>
        _syncPages.GetOrAdd(accessToken, _ => new()).Enqueue(() => throw new PlaidException(status, errorCode, "req-test"));

    /// <summary>Plays queued pages in order; with none queued, reports no changes and keeps the cursor.</summary>
    public Task<PlaidSyncPage> SyncTransactionsAsync(string accessToken, string? cursor, CancellationToken cancellationToken)
    {
        SyncCalls.Enqueue((accessToken, cursor));
        if (_syncPages.TryGetValue(accessToken, out var queue) && queue.TryDequeue(out var next))
        {
            return Task.FromResult(next());
        }

        return Task.FromResult(new PlaidSyncPage([], [], [], cursor ?? "cursor-empty", false));
    }

    /// <summary>Every access token Plaid was asked to remove, in order.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<string> RemovedAccessTokens { get; } = new();

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PlaidException> _removeFailures = new();

    /// <summary>Makes the next remove for this access token fail the way Plaid would.</summary>
    public void FailRemove(string accessToken, string errorCode, System.Net.HttpStatusCode status) =>
        _removeFailures[accessToken] = new PlaidException(status, errorCode, "req-test");

    public Task RemoveItemAsync(string accessToken, CancellationToken cancellationToken)
    {
        RemovedAccessTokens.Enqueue(accessToken);
        return _removeFailures.TryRemove(accessToken, out var failure) ? throw failure : Task.CompletedTask;
    }

    public Task<string> CreateLinkTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        LinkTokenUsers.Add(userId);
        return Task.FromResult($"link-sandbox-{userId:N}");
    }

    public Task<PlaidExchangeResult> ExchangePublicTokenAsync(string publicToken, CancellationToken cancellationToken) =>
        _items.TryGetValue(publicToken, out var item)
            ? Task.FromResult(item.Exchange)
            : throw new PlaidException(System.Net.HttpStatusCode.BadRequest, "INVALID_PUBLIC_TOKEN", "req-test");

    public Task<PlaidItemDetails> GetItemDetailsAsync(string accessToken, CancellationToken cancellationToken) =>
        Task.FromResult(_byAccessToken[accessToken]);
}

/// <summary>Factory with a Plaid encryption key that is not 32 bytes, which must stop the app at startup.</summary>
public sealed class BadPlaidKeyWebApplicationFactory : AuthWebApplicationFactory
{
    protected override IReadOnlyDictionary<string, string?> ExtraConfig { get; } =
        new Dictionary<string, string?> { ["Plaid:TokenEncryptionKey"] = Convert.ToBase64String(new byte[16]) };
}

/// <summary>Records messages instead of sending them.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];

    public IReadOnlyList<EmailMessage> Sent
    {
        get { lock (_sent) { return [.. _sent]; } }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        lock (_sent) { _sent.Add(message); }
        return Task.CompletedTask;
    }

    public EmailMessage LastTo(string address) => Sent.Last(message => message.To == address);

    /// <summary>Pulls the raw token out of the emailed link.</summary>
    public static string TokenFrom(EmailMessage message) =>
        System.Text.RegularExpressions.Regex.Match(message.TextBody, @"token=([A-Za-z0-9_-]+)").Groups[1].Value;

    /// <summary>Pulls the 6-digit login code out of the message.</summary>
    public static string CodeFrom(EmailMessage message) =>
        System.Text.RegularExpressions.Regex.Match(message.TextBody, @"\b(\d{6})\b").Groups[1].Value;
}

/// <summary>Runs each account email job immediately, so tests need not wait for the worker.</summary>
public sealed class InlineAccountEmailQueue(IServiceScopeFactory scopeFactory) : IAccountEmailQueue
{
    public async ValueTask EnqueueAsync(AccountEmailJob job, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<AccountEmailProcessor>()
            .ProcessAsync(job, cancellationToken);
    }
}

/// <summary>Always fails, to prove signup survives a mail outage.</summary>
public sealed class FailingEmailSender : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("SMTP is down.");
}

/// <summary>Test-only route guarded by the same policy ledger endpoints will use.</summary>
[ApiController]
[Route("api/test/verified-only")]
public sealed class VerifiedOnlyProbeController : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthSecurityExtensions.VerifiedEmailPolicy)]
    public IActionResult Get() => Ok();
}

/// <summary>Factory whose mail sender always throws.</summary>
public sealed class FailingEmailWebApplicationFactory : AuthWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
            services.AddSingleton<IEmailSender, FailingEmailSender>());
    }
}

/// <summary>Factory with a low login rate limit for issue #12 tests.</summary>
public sealed class RateLimitedAuthWebApplicationFactory : AuthWebApplicationFactory
{
    protected override IReadOnlyDictionary<string, string?> ExtraConfig { get; } =
        new Dictionary<string, string?>
        {
            ["AuthSecurity:LoginPermitLimit"] = "3",
            ["AuthSecurity:LoginWindowSeconds"] = "60",
            ["AuthSecurity:ResendPermitLimit"] = "2",
        };
}

/// <summary>Factory with a real resend cooldown for issue #54 tests.</summary>
public sealed class LoginCodeCooldownWebApplicationFactory : AuthWebApplicationFactory
{
    protected override IReadOnlyDictionary<string, string?> ExtraConfig { get; } =
        new Dictionary<string, string?>
        {
            ["AuthSecurity:LoginCodeResendCooldownSeconds"] = "60",
        };
}

/// <summary>Counts password hashes and verifications so tests can prove an endpoint always does the hashing work.</summary>
public sealed class CountingPasswordHasher : IPasswordHasher<User>
{
    private readonly PasswordHasher<User> _inner = new();
    private int _verifications;
    private int _hashes;

    public int Verifications => Volatile.Read(ref _verifications);

    public int Hashes => Volatile.Read(ref _hashes);

    public string HashPassword(User user, string password)
    {
        Interlocked.Increment(ref _hashes);
        return _inner.HashPassword(user, password);
    }

    public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
    {
        Interlocked.Increment(ref _verifications);
        return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }
}

/// <summary>Factory that swaps in <see cref="CountingPasswordHasher"/>.</summary>
public sealed class CountingHasherWebApplicationFactory : AuthWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPasswordHasher<User>>();
            services.AddSingleton<CountingPasswordHasher>();
            services.AddSingleton<IPasswordHasher<User>>(
                provider => provider.GetRequiredService<CountingPasswordHasher>());
        });
    }

    public CountingPasswordHasher Hasher => Services.GetRequiredService<CountingPasswordHasher>();
}

/// <summary>
/// Factory whose requests arrive from a fixed peer address. TestServer has no remote IP of its own,
/// and the forwarded-headers middleware does not vet an unknown peer, so a startup filter supplies one.
/// </summary>
public abstract class FixedPeerWebApplicationFactory(string peerAddress) : AuthWebApplicationFactory
{
    protected override IReadOnlyDictionary<string, string?> ExtraConfig { get; } =
        new Dictionary<string, string?>
        {
            ["AuthSecurity:LoginPermitLimit"] = "3",
            ["Proxy:KnownNetworks:0"] = "10.0.0.0/8",
        };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
            services.AddSingleton<IStartupFilter>(new FixedPeerStartupFilter(peerAddress)));
    }

    private sealed class FixedPeerStartupFilter(string peerAddress) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(peerAddress);
                return nextMiddleware();
            });
            next(app);
        };
    }
}

/// <summary>A peer inside Proxy:KnownNetworks, like Tailscale Funnel on the same network.</summary>
public sealed class TrustedProxyWebApplicationFactory() : FixedPeerWebApplicationFactory("10.1.2.3");

/// <summary>A peer outside Proxy:KnownNetworks, like a client that reached the API directly.</summary>
public sealed class UntrustedPeerWebApplicationFactory() : FixedPeerWebApplicationFactory("203.0.113.99");

/// <summary>Factory with a malformed proxy range, which must stop the app at startup.</summary>
public sealed class BadProxyRangeWebApplicationFactory : AuthWebApplicationFactory
{
    protected override IReadOnlyDictionary<string, string?> ExtraConfig { get; } =
        new Dictionary<string, string?> { ["Proxy:KnownNetworks:0"] = "not-a-network" };
}

/// <summary>
/// Lets a test run a competing write from another context in the middle of a save, to reproduce
/// two requests racing. In-memory stores cannot interleave real requests, so this stands in.
/// </summary>
public sealed class RaceInjector : SaveChangesInterceptor
{
    private bool _fired;

    public Func<DbContext, bool>? Trigger { get; set; }

    public Func<DbContext, Task>? Race { get; set; }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!_fired && eventData.Context is { } context && Trigger?.Invoke(context) == true)
        {
            _fired = true;
            await Race!(context);
        }

        return result;
    }
}

/// <summary>Factory whose database context runs <see cref="Injector"/> on every save.</summary>
public sealed class RaceWebApplicationFactory : AuthWebApplicationFactory
{
    public RaceInjector Injector { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(DatabaseName).AddInterceptors(Injector));
        });
    }
}

/// <summary>
/// Boots the API as Production would see it: Smtp mail, an https frontend URL and a chosen
/// AllowedHosts. Everything else (database, mail sink) is still swapped for test doubles.
/// </summary>
public sealed class ProductionWebApplicationFactory(string allowedHosts) : AuthWebApplicationFactory
{
    protected override IReadOnlyDictionary<string, string?> ExtraConfig { get; } =
        new Dictionary<string, string?>
        {
            ["AllowedHosts"] = allowedHosts,
            ["Email:Provider"] = "Smtp",
            ["Email:FrontendBaseUrl"] = "https://app.example.com",
            ["Email:Smtp:Host"] = "smtp.example.com",
            ["Email:Smtp:Username"] = "user",
            ["Email:Smtp:Password"] = "not-a-real-password",
        };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Production");
    }
}
