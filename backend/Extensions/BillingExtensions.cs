using backend.Services;

namespace backend.Extensions;

public static class BillingExtensions
{
    /// <summary>
    /// The key is optional: without one, checkout answers 503 and the trial gate still applies. A live key, or a
    /// key without its webhook secret and price, is a misconfiguration and fails at boot.
    /// </summary>
    public static IServiceCollection AddBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<StripeOptions>()
            .Bind(configuration.GetSection(StripeOptions.SectionName))
            .Validate(
                StripeOptions.IsValid,
                "Stripe:SecretKey must be a test key (sk_test_), with Stripe:WebhookSecret (whsec_) and Stripe:PriceId (price_).")
            .Validate(options => StripeOptions.IsHttpUrl(options.FrontendBaseUrl), "Stripe:FrontendBaseUrl must be an http(s) URL.")
            .ValidateOnStart();

        services.AddSingleton<IBillingGateway, StripeBillingGateway>();
        return services;
    }
}
