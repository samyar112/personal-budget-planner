using Microsoft.Extensions.Options;

namespace backend.Services.Email;

/// <summary>
/// Local sink: writes each message as an .eml file so the verification link can
/// be opened by hand. Development only; the files contain live tokens.
/// </summary>
public sealed class FileEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        Directory.CreateDirectory(settings.OutputDirectory);

        var path = Path.Combine(
            settings.OutputDirectory,
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.eml");

        await using var stream = File.Create(path);
        await message.ToMimeMessage(settings.FromAddress).WriteToAsync(stream, cancellationToken);
    }
}
