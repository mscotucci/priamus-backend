using System.Security.Cryptography;
using System.Text;
using Priamus.Trips.Identity.Application.Abstractions;

namespace Priamus.Trips.Identity.Infrastructure;

/// <summary>
/// SHA-256 implementation of <see cref="IRefreshTokenHasher"/>.
/// Hash is uppercase hex, 64 characters. Verification uses constant-time comparison.
/// </summary>
internal sealed class Sha256RefreshTokenHasher : IRefreshTokenHasher
{
    /// <inheritdoc />
    public string Hash(string refreshToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);

        var bytes = Encoding.UTF8.GetBytes(refreshToken);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes); // uppercase, 64 chars
    }

    /// <inheritdoc />
    public bool Verify(string refreshToken, string expectedHash)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);
        ArgumentNullException.ThrowIfNull(expectedHash);

        var actualHash = Hash(refreshToken);
        var actualBytes = Encoding.UTF8.GetBytes(actualHash);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedHash);

        return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
