using backend.Data;
using backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace backend.Services;

/// <summary>
/// Promotes the configured account to admin. It never creates an account and never touches an
/// unverified one, so naming an address you do not control gets you nothing.
/// </summary>
public sealed class AdminSeeder(
    AppDbContext db,
    IOptions<AdminOptions> options,
    ILogger<AdminSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var email = options.Value.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email))
        {
            return;
        }

        var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);
        if (user is null)
        {
            logger.LogWarning("Admin:Email has no account yet. Register it, verify it, then restart.");
            return;
        }

        if (user.EmailVerifiedAt is null)
        {
            logger.LogWarning("Admin:Email is not verified, so it was not promoted.");
            return;
        }

        if (user.Role == UserRole.Admin)
        {
            return;
        }

        user.Role = UserRole.Admin;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Promoted the Admin:Email account to admin.");
    }
}

/// <summary>Runs the seeder once at boot. A failure is logged, not fatal: the API still serves users.</summary>
public sealed class AdminSeedHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<AdminSeedHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Admin seeding failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
