using Priamus.Trips.SharedKernel.Domain;

namespace Priamus.Trips.Identity.Domain.Users.ValueObjects;

/// <summary>
/// Opaque password-hash string produced by the application layer's
/// <c>IPasswordHasher&lt;User&gt;</c>. The domain treats the hash as an opaque
/// token: it never inspects the format, never verifies a password (verification
/// belongs in the application layer where the hasher is injected — see ADR 0005
/// and the Identity domain brief), and only enforces defensive bounds.
/// </summary>
/// <remarks>
/// <para>
/// We deliberately do NOT depend on <c>Microsoft.AspNetCore.Identity</c> from the
/// domain layer. The domain only owns the value-object boundary; the choice of
/// hashing algorithm (PBKDF2, Argon2, …) is an application/infrastructure concern.
/// </para>
/// </remarks>
/// <param name="Value">The opaque hash string. Guaranteed non-empty and length-bounded.</param>
public sealed record PasswordHash(string Value)
{
    /// <summary>
    /// Defensive upper bound on the encoded hash length. ASP.NET Core's default
    /// PBKDF2 produces ~84 chars; Argon2 implementations stay well under 512.
    /// 1024 leaves slack for future schemes without allowing pathological input.
    /// </summary>
    public const int MaxLength = 1024;

    /// <summary>
    /// Wraps an already-computed hash in the value object, applying defensive validation.
    /// </summary>
    /// <param name="raw">The hash string produced by the application-layer hasher.</param>
    /// <exception cref="DomainException">
    /// Thrown with <see cref="UserErrors.PasswordHashInvalid"/> when <paramref name="raw"/>
    /// is null, empty, whitespace-only, or exceeds <see cref="MaxLength"/>.
    /// </exception>
    public static PasswordHash Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new DomainException(UserErrors.PasswordHashInvalid);
        }

        if (raw.Length > MaxLength)
        {
            throw new DomainException(UserErrors.PasswordHashInvalid);
        }

        return new PasswordHash(raw);
    }
}
