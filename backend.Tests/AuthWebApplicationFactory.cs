using backend.Data;
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
        });
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
        };
}
