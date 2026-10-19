using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using backend.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace backend.Services;

public sealed record TokenResult(string AccessToken, DateTime ExpiresAt);

/// <summary>
/// Creates signed JWT access tokens for authenticated users.
/// </summary>
public sealed class TokenProvider(IOptions<JwtOptions> jwtOptions)
{
    public const string EmailVerifiedClaim = "email_verified";
    public const string RoleClaim = "role";

    private readonly JwtOptions _options = jwtOptions.Value;

    public TokenResult Create(User user)
    {
        if (string.IsNullOrWhiteSpace(_options.Key) || _options.Key.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Key must be at least 32 characters. Set it in appsettings.Development.json.");
        }

        var expiresAt = DateTime.UtcNow.AddMinutes(_options.ExpiresMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.Name),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(RoleClaim, user.Role.ToString()),
        };

        // Read by the VerifiedEmail policy. Stale until the next refresh after verifying.
        if (user.EmailVerifiedAt is not null)
        {
            claims.Add(new Claim(EmailVerifiedClaim, "true"));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        return new TokenResult(accessToken, expiresAt);
    }
}
