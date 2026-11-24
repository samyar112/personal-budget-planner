using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace backend.Services;

/// <summary>What Google vouches for once an ID token has been verified.</summary>
public sealed record GoogleIdentity(string Subject, string Email, string Name);

public interface IGoogleTokenVerifier
{
    /// <summary>
    /// Returns the verified identity, or null for any token that cannot be trusted: bad signature,
    /// wrong issuer or audience, expired, malformed, or an email Google has not verified.
    /// </summary>
    Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken);
}

public sealed class GoogleTokenVerifier(IOptions<GoogleOptions> options) : IGoogleTokenVerifier
{
    public async Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken)
    {
        try
        {
            // Checks the signature against Google's rotating public keys, the issuer, the expiry
            // and, through Audience, that the token was minted for this app and not another one.
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = [options.Value.ClientId] });

            // Matching accounts by email is only safe when Google itself has verified the address.
            if (!payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Email) || string.IsNullOrWhiteSpace(payload.Subject))
            {
                return null;
            }

            var email = payload.Email.Trim().ToLowerInvariant();
            return new GoogleIdentity(payload.Subject, email, string.IsNullOrWhiteSpace(payload.Name) ? email : payload.Name);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}
