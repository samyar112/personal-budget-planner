using System.Threading.RateLimiting;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace backend.Extensions;

public static class AuthSecurityExtensions
{
    public const string LoginRateLimitPolicy = "login";

    public static IServiceCollection AddAuthSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<AuthSecurityOptions>()
            .Bind(configuration.GetSection(AuthSecurityOptions.SectionName))
            .Validate(
                options => options.LoginPermitLimit > 0 && options.LoginWindowSeconds > 0,
                "AuthSecurity login rate limit values must be positive.")
            .Validate(
                options => options.MaxFailedLoginAttempts > 0 && options.LockoutMinutes > 0,
                "AuthSecurity lockout values must be positive.")
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { message = "Too many login attempts. Please try again later." },
                    cancellationToken);
            };

            options.AddPolicy(LoginRateLimitPolicy, httpContext =>
            {
                var security = httpContext.RequestServices
                    .GetRequiredService<IOptions<AuthSecurityOptions>>()
                    .Value;

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = security.LoginPermitLimit,
                        Window = TimeSpan.FromSeconds(security.LoginWindowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });
        });

        return services;
    }

    /// <summary>
    /// Records a failed password attempt and locks the account when the threshold is reached.
    /// </summary>
    public static void RegisterFailedLogin(User user, AuthSecurityOptions options)
    {
        user.FailedLoginAttempts += 1;
        if (user.FailedLoginAttempts >= options.MaxFailedLoginAttempts)
        {
            user.LockoutEnd = DateTime.UtcNow.AddMinutes(options.LockoutMinutes);
            user.FailedLoginAttempts = 0;
        }
    }

    public static void ClearLoginFailures(User user)
    {
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
    }

    public static bool IsLockedOut(User user, DateTime utcNow) =>
        user.LockoutEnd is { } lockoutEnd && lockoutEnd > utcNow;
}
