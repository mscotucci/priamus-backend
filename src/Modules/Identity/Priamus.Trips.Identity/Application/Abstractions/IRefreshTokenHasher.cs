namespace Priamus.Trips.Identity.Application.Abstractions;

/// <summary>
/// Hashes and verifies opaque refresh-token strings.
/// Refresh tokens are 32 cryptographically random bytes base64url-encoded (43 chars).
/// The hash is SHA-256 of the UTF-8 bytes of that string, hex uppercase, 64 chars.
/// </summary>
/// <remarks>
/// Do NOT reuse <c>IPasswordHasher&lt;User&gt;</c> — refresh tokens are high-entropy by
/// construction; key-stretching is the wrong primitive here. SHA-256 is sufficient and
/// fast for lookup purposes. This is intentional (see Identity domain brief §2).
/// </remarks>
public interface IRefreshTokenHasher
{
    /// <summary>
    /// Computes the SHA-256 hex hash of the given refresh token string.
    /// </summary>
    /// <param name="refreshToken">The cleartext opaque refresh token (base64url, 43 chars).</param>
    /// <returns>Uppercase hex SHA-256 string, exactly 64 characters.</returns>
    string Hash(string refreshToken);

    /// <summary>
    /// Verifies that <paramref name="refreshToken"/> hashes to <paramref name="expectedHash"/>
    /// using a constant-time comparison to prevent timing attacks.
    /// </summary>
    /// <param name="refreshToken">The cleartext token to verify.</param>
    /// <param name="expectedHash">The stored uppercase hex SHA-256 hash.</param>
    /// <returns><c>true</c> if the token matches the expected hash; otherwise <c>false</c>.</returns>
    bool Verify(string refreshToken, string expectedHash);
}
