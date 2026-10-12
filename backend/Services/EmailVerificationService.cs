using System.Buffers.Text;
using System.Security.Cryptography;
using backend.Data;
using backend.Models;
using backend.Services.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace backend.Services;

public enum VerifyEmailResult
{
    Verified,
    Invalid,
    Expired,
}

/// <summary>
/// Issues emailed verification links and consumes them. Only a hash of each token is stored.
/// </summary>
public sealed class EmailVerificationService(
    AppDbContext db,
    IEmailSender emailSender,
    IOptions<EmailOptions> options)
{
    /// <summary>
    /// Supersedes any unused tokens for the user, stores a new hashed one, and emails the link.
    /// </summary>
    public async Task SendAsync(User user, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = DateTime.UtcNow;

        var pending = await db.EmailVerificationTokens
            .Where(token => token.UserId == user.Id && token.UsedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in pending)
        {
            token.UsedAt = now;
        }

        var rawToken = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var lifetime = TimeSpan.FromMinutes(settings.VerificationTokenMinutes);
        db.EmailVerificationTokens.Add(new EmailVerificationToken
        {
            UserId = user.Id,
            TokenHash = RefreshTokenService.HashToken(rawToken),
            ExpiresAt = now + lifetime,
        });
        await db.SaveChangesAsync(cancellationToken);

        var link = $"{settings.FrontendBaseUrl.TrimEnd('/')}/verify-email?token={rawToken}";
        await emailSender.SendAsync(
            EmailTemplates.Verification(user.Email, link, lifetime),
            cancellationToken);
    }

    /// <summary>
    /// Consumes a token and stamps the user. A repeat click is rejected, so a leaked link
    /// cannot be replayed; verifying an already-verified user is harmless.
    /// </summary>
    public async Task<VerifyEmailResult> VerifyAsync(string? rawToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return VerifyEmailResult.Invalid;
        }

        var hash = RefreshTokenService.HashToken(rawToken);
        var token = await db.EmailVerificationTokens
            .Include(candidate => candidate.User)
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);

        if (token is null || token.UsedAt is not null)
        {
            return VerifyEmailResult.Invalid;
        }

        var now = DateTime.UtcNow;
        if (token.ExpiresAt <= now)
        {
            return VerifyEmailResult.Expired;
        }

        token.UsedAt = now;
        token.User.EmailVerifiedAt ??= now;
        await db.SaveChangesAsync(cancellationToken);

        return VerifyEmailResult.Verified;
    }
}
