namespace backend.Services.Email;

public enum AccountEmailKind
{
    PasswordReset,
    VerificationResend,
}

/// <summary>
/// A request to email an account holder, queued so the HTTP request never waits for the
/// lookup or the mail server. <see cref="Email"/> is already trimmed and lower-cased.
/// </summary>
public sealed record AccountEmailJob(AccountEmailKind Kind, string Email);
