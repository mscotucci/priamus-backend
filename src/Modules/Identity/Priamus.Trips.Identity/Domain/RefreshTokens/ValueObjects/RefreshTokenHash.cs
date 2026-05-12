using Priamus.Trips.SharedKernel.Domain;

namespace Priamus.Trips.Identity.Domain.RefreshTokens.ValueObjects;

/// <summary>
/// Opaque hash of a refresh-token string. Refresh tokens are high-entropy random
/// values; hashing uses SHA-256 (computed in the application layer, NOT a password
/// hasher) so lookups can be done by hash without slowing the auth path.
/// </summary>
/// <remarks>
/// <para>
/// The domain receives only the hash — never the cleartext token. The choice of
/// SHA-256 (vs. PBKDF2/Argon2 used for passwords) is documented in the Identity
/// domain brief: password hashing is the wrong primitive for high-entropy values
/// and would make lookup prohibitively slow.
/// </para>
/// <para>
/// SHA-256 produces a 32-byte digest encoded as a 64-character lowercase hex
/// string. The length is therefore validated as an exact equality, not a cap —
/// anything else indicates a bug (truncation, wrong algorithm, malformed input).
/// If the hashing algorithm is ever changed, <see cref="ExpectedLength"/> must be
/// updated deliberately and in lockstep with the new algorithm's output size.
/// </para>
/// </remarks>
/// <param name="Value">The opaque hash string. Guaranteed non-empty and of exact length.</param>
public sealed record RefreshTokenHash(string Value)
{
    // SHA-256 hex. Change deliberately if the hashing algorithm ever changes.
    /// <summary>Exact length of the encoded refresh-token hash (SHA-256 hex = 64 chars).</summary>
    public const int ExpectedLength = 64;

    /// <summary>
    /// Wraps an already-computed refresh-token hash, applying exact-length validation.
    /// </summary>
    /// <param name="raw">The hash string produced by the application-layer hashing routine.</param>
    /// <exception cref="DomainException">
    /// Thrown with <see cref="RefreshTokenErrors.HashInvalid"/> when the value is
    /// null, empty, whitespace, or not exactly <see cref="ExpectedLength"/> characters long.
    /// </exception>
    public static RefreshTokenHash Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new DomainException(RefreshTokenErrors.HashInvalid);
        }

        if (raw.Length != RefreshTokenHash.ExpectedLength)
        {
            throw new DomainException(RefreshTokenErrors.HashInvalid);
        }

        return new RefreshTokenHash(raw);
    }
}
