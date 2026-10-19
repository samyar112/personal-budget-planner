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

    public static EmailMessage LoginCode(string to, string code, TimeSpan validFor)
    {
        const string intro = "Use this code to finish logging in to VaultBudget.";
        const string ignoreNote =
            "If this was not you, someone knows your password. Reset it and do not share this code.";
        var expiry = $"This code expires in {Describe(validFor)}.";

        var text = $"{intro}\n\nCode: {code}\n\n{expiry}\n{ignoreNote}\n";
        var html = $"""
            <p>{WebUtility.HtmlEncode(intro)}</p>
            <p style="font-size:24px;letter-spacing:4px"><strong>{WebUtility.HtmlEncode(code)}</strong></p>
            <p>{WebUtility.HtmlEncode(expiry)}<br>{WebUtility.HtmlEncode(ignoreNote)}</p>
            """;

        return new EmailMessage(to, "Your VaultBudget login code", text, html);
    }

    public static EmailMessage GoogleAccountNotice(string to)
    {
        const string body =
            "Someone asked to reset the password for this VaultBudget account, but it signs in with "
            + "Google and has no password. Use \"Continue with Google\" on the log in page instead.";
        const string ignoreNote = "If you did not ask for this, you can ignore this email.";

        var text = $"{body}\n\n{ignoreNote}\n";
        var html = $"""
            <p>{WebUtility.HtmlEncode(body)}</p>
            <p>{WebUtility.HtmlEncode(ignoreNote)}</p>
            """;

        return new EmailMessage(to, "Signing in to VaultBudget", text, html);
    }

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
