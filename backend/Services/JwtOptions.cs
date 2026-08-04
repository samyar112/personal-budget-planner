namespace backend.Services;

/// <summary>
/// JWT settings bound from the "Jwt" configuration section.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpiresMinutes { get; set; } = 15;

    /// <summary>Lifetime of opaque refresh tokens (days).</summary>
    public int RefreshExpiresDays { get; set; } = 7;

    /// <summary>
    /// After rotation, the previous refresh token may still mint an access token for this many seconds
    /// (covers multi-tab / in-flight twin refresh).
    /// </summary>
    public int RefreshGraceSeconds { get; set; } = 20;
}
