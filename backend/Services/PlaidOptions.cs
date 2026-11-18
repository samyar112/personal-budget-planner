namespace backend.Services;

/// <summary>
/// Plaid API settings. <c>ClientId</c>, <c>Secret</c> and <c>TokenEncryptionKey</c> are secrets:
/// git-ignored Development settings locally, the Pi's git-ignored <c>.env</c> in production.
/// </summary>
public sealed class PlaidOptions
{
    public const string SectionName = "Plaid";
    public const string SandboxEnvironment = "sandbox";
    public const string ProductionEnvironment = "production";

    public string ClientId { get; set; } = "";
    public string Secret { get; set; } = "";

    /// <summary><c>sandbox</c> or <c>production</c>. Picks the Plaid host.</summary>
    public string Environment { get; set; } = SandboxEnvironment;

    /// <summary>Base64 of exactly 32 random bytes (AES-256). Losing it makes every stored access token unreadable.</summary>
    public string TokenEncryptionKey { get; set; } = "";

    public Uri BaseAddress => Environment == ProductionEnvironment
        ? new Uri("https://production.plaid.com/")
        : new Uri("https://sandbox.plaid.com/");

    public static bool IsAesKey(string value)
    {
        Span<byte> buffer = stackalloc byte[64];
        return Convert.TryFromBase64String(value, buffer, out var written) && written == 32;
    }
}
