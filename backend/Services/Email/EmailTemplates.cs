using System.Net;

namespace backend.Services.Email;

public static class EmailTemplates
{
    public static EmailMessage Verification(string to, string link, TimeSpan validFor) =>
        Build(
            to,
            subject: "Verify your VaultBudget email",
            intro: "Welcome to VaultBudget. Confirm your email address to finish setting up your account.",
            action: "Verify email",
            link,
            validFor,
            ignoreNote: "If you did not create this account, you can ignore this email.");

    public static EmailMessage PasswordReset(string to, string link, TimeSpan validFor) =>
        Build(
            to,
            subject: "Reset your VaultBudget password",
            intro: "We received a request to reset your VaultBudget password.",
            action: "Reset password",
            link,
            validFor,
            ignoreNote: "If you did not ask for this, you can ignore this email. Your password has not changed.");

    private static EmailMessage Build(
        string to, string subject, string intro, string action, string link, TimeSpan validFor, string ignoreNote)
    {
        var expiry = $"This link expires in {Describe(validFor)}.";

        var text = $"{intro}\n\n{action}: {link}\n\n{expiry}\n{ignoreNote}\n";

        var html = $"""
            <p>{WebUtility.HtmlEncode(intro)}</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">{WebUtility.HtmlEncode(action)}</a></p>
            <p>{WebUtility.HtmlEncode(expiry)}<br>{WebUtility.HtmlEncode(ignoreNote)}</p>
            """;

        return new EmailMessage(to, subject, text, html);
    }

    private static string Describe(TimeSpan span) =>
        span.TotalHours >= 1
            ? $"{(int)span.TotalHours} hour(s)"
            : $"{(int)span.TotalMinutes} minute(s)";
}
