namespace backend.Services;

/// <summary>
/// Outbound email settings. <c>File</c> writes messages to disk for local work
/// and is refused outside Development because the messages contain live tokens.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public const string FileProvider = "File";
    public const string SmtpProvider = "Smtp";

    public string Provider { get; set; } = FileProvider;

    /// <summary>Where emailed links point. Must be https outside Development.</summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";

    /// <summary>How long an emailed verification link stays valid.</summary>
    public int VerificationTokenMinutes { get; set; } = 15;

    /// <summary>How long an emailed password-reset link stays valid.</summary>
    public int PasswordResetTokenMinutes { get; set; } = 15;

    /// <summary>Envelope sender. Gmail rewrites this to the authenticated account.</summary>
    public string FromAddress { get; set; } = "";

    /// <summary>Folder the file sink writes .eml files into.</summary>
    public string OutputDirectory { get; set; } = "email-outbox";

    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";

    /// <summary>
    /// Keep true everywhere that can. Chain and hostname validation always run; this only
    /// controls the OCSP/CRL lookup, which fails on some dev machines and networks.
    /// </summary>
    public bool CheckCertificateRevocation { get; set; } = true;

    /// <summary>Secret. User-secrets or git-ignored Development settings locally; the Pi's git-ignored `.env` in production.</summary>
    public string Password { get; set; } = "";
}
