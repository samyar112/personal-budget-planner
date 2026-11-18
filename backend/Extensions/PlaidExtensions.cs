using backend.Services;
using Microsoft.Extensions.Options;

namespace backend.Extensions;

public static class PlaidExtensions
{
    public static IServiceCollection AddPlaid(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<PlaidOptions>()
            .Bind(configuration.GetSection(PlaidOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ClientId) && !string.IsNullOrWhiteSpace(options.Secret),
                "Plaid:ClientId and Plaid:Secret are required.")
            .Validate(
                options => options.Environment is PlaidOptions.SandboxEnvironment or PlaidOptions.ProductionEnvironment,
                "Plaid:Environment must be sandbox or production.")
            .Validate(
                options => PlaidOptions.IsAesKey(options.TokenEncryptionKey),
                "Plaid:TokenEncryptionKey must be base64 of exactly 32 bytes (openssl rand -base64 32).")
            .ValidateOnStart();

        services.AddSingleton<AccessTokenProtector>();
        services.AddScoped<PlaidSyncService>();
        services.AddHttpClient<IPlaidClient, PlaidClient>((provider, http) =>
        {
            http.BaseAddress = provider.GetRequiredService<IOptions<PlaidOptions>>().Value.BaseAddress;
            http.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
