using backend.Services;

namespace backend.Extensions;

public static class GoogleExtensions
{
    public static IServiceCollection AddGoogleSignIn(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<GoogleOptions>()
            .Bind(configuration.GetSection(GoogleOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ClientId),
                "Google:ClientId is required. Set it in appsettings.Development.json (the same value as VITE_GOOGLE_CLIENT_ID).")
            .ValidateOnStart();

        services.AddSingleton<IGoogleTokenVerifier, GoogleTokenVerifier>();

        return services;
    }
}
