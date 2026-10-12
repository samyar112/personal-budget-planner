using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;

namespace backend.Services.Email;

public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var smtp = settings.Smtp;

        using var client = new SmtpClient { CheckCertificateRevocation = smtp.CheckCertificateRevocation };
        // Auto negotiates STARTTLS on 587 and implicit TLS on 465.
        await client.ConnectAsync(smtp.Host, smtp.Port, SecureSocketOptions.Auto, cancellationToken);
        await client.AuthenticateAsync(smtp.Username, smtp.Password, cancellationToken);
        await client.SendAsync(message.ToMimeMessage(settings.FromAddress), cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
