using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace backend.Services;

/// <summary>
/// Encrypts Plaid access tokens at rest with AES-256-GCM. The owning user id is bound in as
/// associated data, so a ciphertext copied onto another user's row fails to decrypt.
/// Output is <c>v1:</c> + base64(nonce | tag | ciphertext); the prefix leaves room for key rotation.
/// </summary>
public sealed class AccessTokenProtector(IOptions<PlaidOptions> options)
{
    private const string Version = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key = Convert.FromBase64String(options.Value.TokenEncryptionKey);

    public string Protect(string accessToken, Guid userId)
    {
        var plaintext = Encoding.UTF8.GetBytes(accessToken);
        var output = new byte[NonceSize + TagSize + plaintext.Length];
        var nonce = output.AsSpan(0, NonceSize);
        var tag = output.AsSpan(NonceSize, TagSize);
        var ciphertext = output.AsSpan(NonceSize + TagSize);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, userId.ToByteArray());

        return Version + Convert.ToBase64String(output);
    }

    /// <exception cref="CryptographicException">Wrong key, wrong user, or tampered data.</exception>
    public string Unprotect(string protectedToken, Guid userId)
    {
        if (!protectedToken.StartsWith(Version, StringComparison.Ordinal))
        {
            throw new CryptographicException("Unknown access token format.");
        }

        var input = Convert.FromBase64String(protectedToken[Version.Length..]);
        if (input.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Access token ciphertext is too short.");
        }

        var plaintext = new byte[input.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            input.AsSpan(0, NonceSize),
            input.AsSpan(NonceSize + TagSize),
            input.AsSpan(NonceSize, TagSize),
            plaintext,
            userId.ToByteArray());

        return Encoding.UTF8.GetString(plaintext);
    }
}
