namespace backend.Services;

/// <summary>
/// Stripe Billing, test mode only. <c>SecretKey</c> and <c>WebhookSecret</c> are secrets: git-ignored Development
/// settings locally, the Pi's git-ignored <c>.env</c> (<c>Stripe__SecretKey</c>) in production. With no key the
/// app still runs and the upgrade button answers 503.
/// </summary>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>Must be a test key (<c>sk_test_</c>). A live key fails at boot, so no real card is ever charged.</summary>
    public string SecretKey { get; set; } = "";

    /// <summary>Signing secret of the webhook endpoint (<c>whsec_</c>); from <c>stripe listen</c> locally.</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>The $2.99/month recurring price (<c>price_</c>), created in the Stripe dashboard.</summary>
    public string PriceId { get; set; } = "";

    /// <summary>Where Checkout and the billing portal send the user back to.</summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);

    public static bool IsValid(StripeOptions options) =>
        !options.IsConfigured
        || (options.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal)
            && options.WebhookSecret.StartsWith("whsec_", StringComparison.Ordinal)
            && options.PriceId.StartsWith("price_", StringComparison.Ordinal));

    public static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
