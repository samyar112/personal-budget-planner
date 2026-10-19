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

    /// <summary>Max verification-email resend requests per IP within the window.</summary>
    public int ResendPermitLimit { get; set; } = 3;

    /// <summary>Fixed window length for the resend rate limit (seconds).</summary>
    public int ResendWindowSeconds { get; set; } = 300;

    /// <summary>How long an emailed login code stays valid (minutes).</summary>
    public int LoginCodeMinutes { get; set; } = 10;

    /// <summary>Wrong codes allowed per challenge before it is discarded.</summary>
    public int LoginCodeMaxAttempts { get; set; } = 5;

    /// <summary>Minimum gap between emailing codes for one challenge (seconds).</summary>
    public int LoginCodeResendCooldownSeconds { get; set; } = 60;
}
