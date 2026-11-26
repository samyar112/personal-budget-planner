namespace backend.Services;

/// <summary>
/// The AI tips provider: any OpenAI-compatible chat API, OpenRouter by default. <c>ApiKey</c> is a secret:
/// git-ignored Development settings locally, the Pi's git-ignored <c>.env</c> (<c>AiAdvisor__ApiKey</c>) in
/// production. With no key the feature still works and every tip is template text.
/// </summary>
public sealed class AiAdvisorOptions
{
    public const string SectionName = "AiAdvisor";

    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1/";

    public string ApiKey { get; set; } = "";

    /// <summary>One fixed model id, never a random router, so a stored tip can be traced to what wrote it.</summary>
    public string Model { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Generations per user per UTC day. Free model tiers share a small daily quota across all users.</summary>
    public int DailyLimitPerUser { get; set; } = 5;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model);

    public static bool IsHttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
