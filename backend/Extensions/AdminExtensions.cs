using System.Net.Mail;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace backend.Extensions;

public static class AdminExtensions
{
    public const string AdminPolicy = "Admin";
    public const string AdminRole = "Admin";

    public static IServiceCollection AddAdminAccess(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<AdminOptions>()
            .Bind(configuration.GetSection(AdminOptions.SectionName))
            .Validate(
                options => string.IsNullOrWhiteSpace(options.Email)
                    || (MailAddress.TryCreate(options.Email.Trim(), out var address)
                        && address.Address == options.Email.Trim()),
                "Admin:Email must be a valid email address, or empty.")
            .ValidateOnStart();

        services.AddScoped<AdminSeeder>();
        services.AddHostedService<AdminSeedHostedService>();
        services.AddScoped<AdminUserService>();
        services.AddScoped<IAuthorizationHandler, CurrentAdminHandler>();

        // The role claim is a cheap first gate; the handler is the real check.
        services.AddAuthorizationBuilder().AddPolicy(AdminPolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(AdminRole)
            .AddRequirements(new CurrentAdminRequirement()));

        return services;
    }
}
