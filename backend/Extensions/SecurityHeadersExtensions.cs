using Microsoft.AspNetCore.HostFiltering;

namespace backend.Extensions;

public static class SecurityHeadersExtensions
{
    /// <summary>
    /// Production must name the hosts it answers to. A wildcard would let a forged Host header reach
    /// the app, so it is refused at boot, like the other settings.
    /// </summary>
    public static IServiceCollection AddHostValidation(this IServiceCollection services)
    {
        services
            .AddOptions<HostFilteringOptions>()
            .Validate<IHostEnvironment>(
                (options, environment) => environment.IsDevelopment()
                    || (options.AllowedHosts.Count > 0
                        && options.AllowedHosts.All(host => !string.IsNullOrWhiteSpace(host) && !host.Contains('*'))),
                "AllowedHosts must list the API's hostnames (no '*') outside Development.")
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Headers for a JSON-only API: nothing here should be framed, sniffed, or leak a Referer.
    /// Skipped in Development so the Scalar UI can load its scripts.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            return app;
        }

        app.UseHsts();
        app.Use((context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            return next();
        });

        return app;
    }
}
