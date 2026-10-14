using System.Buffers.Text;
using System.Security.Cryptography;
using backend.Data;
using backend.Extensions;
using backend.Models;
using backend.Services.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace backend.Services;

public enum ResetPasswordResult
{
    Reset,
    Invalid,
    Expired,
}

/// <summary>
/// Issues emailed password-reset links and consumes them. Only a hash of each token is stored.
/// </summary>
public sealed class PasswordResetService(
    AppDbContext db,
    IEmailSender emailSender,
    IPasswordHasher<User> passwordHasher,
    IOptions<EmailOptions> options)
{
    /// <summary>
    /// Emails a reset link, superseding any unused ones. A Google-only account has no password
    /// to reset, so it gets a note saying so and no token.
    /// </summary>
    public async Task RequestAsync(User user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            await emailSender.SendAsync(
                EmailTemplates.GoogleAccountNotice(user.Email),
                cancellationToken);
            return;
        }

        var settings = options.Value;
        var now = DateTime.UtcNow;

        var pending = await db.PasswordResetTokens
            .Where(token => token.UserId == user.Id && token.UsedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in pending)
        {
            token.UsedAt = now;
        }

        var rawToken = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var lifetime = TimeSpan.FromMinutes(settings.PasswordResetTokenMinutes);
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = RefreshTokenService.HashToken(rawToken),
            ExpiresAt = now + lifetime,
        });
        await db.SaveChangesAsync(cancellationToken);

        var link = $"{settings.FrontendBaseUrl.TrimEnd('/')}/reset-password?token={rawToken}";
        await emailSender.SendAsync(
            EmailTemplates.PasswordReset(user.Email, link, lifetime),
            cancellationToken);
    }

    /// <summary>
    /// Consumes a token, sets the new password, clears lockout and revokes every refresh token in
    /// one save, so a reset can never leave old sessions alive. The caller validates the password.
    /// </summary>
    public async Task<ResetPasswordResult> ResetAsync(
        string? rawToken,
        string newPassword,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return ResetPasswordResult.Invalid;
        }

        var hash = RefreshTokenService.HashToken(rawToken);
        var token = await db.PasswordResetTokens
            .Include(candidate => candidate.User)
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);

        if (token is null || token.UsedAt is not null)
        {
            return ResetPasswordResult.Invalid;
        }

        var now = DateTime.UtcNow;
        if (token.ExpiresAt <= now)
        {
            return ResetPasswordResult.Expired;
        }

        var user = token.User;
        token.UsedAt = now;
        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        AuthSecurityExtensions.ClearLoginFailures(user);
        // Following the emailed link proves control of the mailbox.
        user.EmailVerifiedAt ??= now;

        var sessions = await db.RefreshTokens
            .Where(refresh => refresh.UserId == user.Id && refresh.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedAt = now;
            session.GraceExpiresAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ResetPasswordResult.Reset;
    }
}
