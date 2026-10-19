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
                // High default so normal auth tests do not trip IP rate limiting.
                ["AuthSecurity:LoginPermitLimit"] = "1000",
                ["AuthSecurity:LoginWindowSeconds"] = "60",
                ["AuthSecurity:MaxFailedLoginAttempts"] = "5",
                ["AuthSecurity:LockoutMinutes"] = "15",
                ["AuthSecurity:ResendPermitLimit"] = "1000",
                ["AuthSecurity:ResendWindowSeconds"] = "60",
                // No cooldown by default so resend tests need not wait; cooldown tests override it.
                ["AuthSecurity:LoginCodeResendCooldownSeconds"] = "0",
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

            // Stand-in for a ledger endpoint, which does not exist yet.
            services.AddControllers().AddApplicationPart(typeof(VerifiedOnlyProbeController).Assembly);
        });
    }

    public CapturingEmailSender Emails => Services.GetRequiredService<CapturingEmailSender>();
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
