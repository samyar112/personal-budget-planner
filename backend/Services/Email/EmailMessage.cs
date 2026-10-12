using MimeKit;

namespace backend.Services.Email;

/// <summary>
/// A transactional email. Both bodies are required so clients without HTML
/// support still get a usable link.
/// </summary>
public sealed record EmailMessage(
    string To,
    string Subject,
    string TextBody,
    string HtmlBody)
{
    /// <summary>Builds the MIME form shared by every sender, so the file sink shows what SMTP would send.</summary>
    public MimeMessage ToMimeMessage(string fromAddress)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(fromAddress));
        message.To.Add(MailboxAddress.Parse(To));
        message.Subject = Subject;
        message.Body = new BodyBuilder { TextBody = TextBody, HtmlBody = HtmlBody }.ToMessageBody();
        return message;
    }
}
