using backend.Data;
using backend.Extensions;
using backend.Services;
using backend.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
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
    private readonly string _databaseName = $"AuthTests-{Guid.NewGuid()}";

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
                options.UseInMemoryDatabase(_databaseName));

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
