using backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace backend.Services.Email;

/// <summary>
/// Does the work behind an <see cref="AccountEmailJob"/>: finds the account and sends the email.
/// Unknown addresses and mail failures are logged or ignored, never thrown.
/// </summary>
public sealed class AccountEmailProcessor(
    AppDbContext db,
    PasswordResetService passwordReset,
    EmailVerificationService emailVerification,
    IEmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    ILogger<AccountEmailProcessor> logger)
{
    public async Task ProcessAsync(AccountEmailJob job, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            candidate => candidate.Email == job.Email,
            cancellationToken);

        if (user is null)
        {
            return;
        }

        try
        {
            switch (job.Kind)
            {
                case AccountEmailKind.PasswordReset:
                    await passwordReset.RequestAsync(user, cancellationToken);
                    break;
                case AccountEmailKind.VerificationResend when user.EmailVerifiedAt is null:
                    await emailVerification.SendAsync(user, cancellationToken);
                    break;
                case AccountEmailKind.RegistrationNotice:
                    await emailSender.SendAsync(
                        EmailTemplates.AccountExists(user.Email, emailOptions.Value.FrontendBaseUrl),
                        cancellationToken);
                    break;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "{Kind} email failed for user {UserId}.", job.Kind, user.Id);
        }
    }
}
