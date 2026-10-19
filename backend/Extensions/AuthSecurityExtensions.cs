using System.Threading.RateLimiting;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace backend.Extensions;

public static class AuthSecurityExtensions
{
    public const string LoginRateLimitPolicy = "login";
    public const string ResendVerificationRateLimitPolicy = "resend-verification";
    public const string ForgotPasswordRateLimitPolicy = "forgot-password";
    public const string VerifyLoginCodeRateLimitPolicy = "verify-login-code";
    public const string ResendLoginCodeRateLimitPolicy = "resend-login-code";
    public const string VerifiedEmailPolicy = "VerifiedEmail";

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
            .Validate(
                options => options.ResendPermitLimit > 0 && options.ResendWindowSeconds > 0,
                "AuthSecurity resend rate limit values must be positive.")
            .Validate(
                options => options.LoginCodeMinutes > 0
                    && options.LoginCodeMaxAttempts > 0
                    && options.LoginCodeResendCooldownSeconds >= 0,
                "AuthSecurity login code values must be positive (cooldown may be zero).")
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

            options.AddPolicy(ResendVerificationRateLimitPolicy, httpContext =>
            {
                var security = httpContext.RequestServices
                    .GetRequiredService<IOptions<AuthSecurityOptions>>()
                    .Value;

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = security.ResendPermitLimit,
                        Window = TimeSpan.FromSeconds(security.ResendWindowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });

            // Shares the resend limits but keeps its own per-IP counter.
            options.AddPolicy(ForgotPasswordRateLimitPolicy, httpContext =>
            {
                var security = httpContext.RequestServices
                    .GetRequiredService<IOptions<AuthSecurityOptions>>()
                    .Value;

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = security.ResendPermitLimit,
                        Window = TimeSpan.FromSeconds(security.ResendWindowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });

            // Guessing a 6-digit code is the attack here, so it is limited per IP like login.
            options.AddPolicy(VerifyLoginCodeRateLimitPolicy, httpContext =>
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

            options.AddPolicy(ResendLoginCodeRateLimitPolicy, httpContext =>
            {
                var security = httpContext.RequestServices
                    .GetRequiredService<IOptions<AuthSecurityOptions>>()
                    .Value;

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = security.ResendPermitLimit,
                        Window = TimeSpan.FromSeconds(security.ResendWindowSeconds),
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
