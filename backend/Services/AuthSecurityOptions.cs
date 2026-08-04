namespace backend.Services;

/// <summary>
/// Login abuse controls: per-IP rate limit and per-account lockout.
/// </summary>
public sealed class AuthSecurityOptions
{
    public const string SectionName = "AuthSecurity";

    /// <summary>Max login requests per IP within the window.</summary>
    public int LoginPermitLimit { get; set; } = 10;

    /// <summary>Fixed window length for the login rate limit (seconds).</summary>
    public int LoginWindowSeconds { get; set; } = 60;

    /// <summary>Failed password attempts before the account is locked.</summary>
    public int MaxFailedLoginAttempts { get; set; } = 5;

    /// <summary>How long a locked account stays locked (minutes).</summary>
    public int LockoutMinutes { get; set; } = 15;
}
