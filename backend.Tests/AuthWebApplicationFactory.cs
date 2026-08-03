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
public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"AuthTests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Jwt:Key lives in gitignored Development settings; provide a test key here.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-jwt-signing-key-at-least-32-chars!!",
                ["Jwt:Issuer"] = "VaultBudget",
                ["Jwt:Audience"] = "VaultBudget",
                ["Jwt:ExpiresMinutes"] = "60",
                ["Jwt:RefreshExpiresDays"] = "7",
                ["Jwt:RefreshGraceSeconds"] = "20",
            });
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
