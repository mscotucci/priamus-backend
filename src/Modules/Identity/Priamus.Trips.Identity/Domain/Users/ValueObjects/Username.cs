using System.Text.RegularExpressions;
using Priamus.Trips.SharedKernel.Domain;

namespace Priamus.Trips.Identity.Domain.Users.ValueObjects;

/// <summary>
/// Normalized username used as the login identifier for a <see cref="User"/>.
/// </summary>
/// <remarks>
/// <para>
/// Normalization is aggressive: the stored value is always lowercased and trimmed.
/// There is no separate display form — callers must accept that what they pass in
/// becomes <c>input.Trim().ToLowerInvariant()</c>. Normalization happens inside
/// <see cref="Create"/>, never at call sites.
/// </para>
/// <para>
/// Charset and shape after normalization:
/// <list type="bullet">
///   <item>Length: 3..64 characters (inclusive).</item>
///   <item>Allowed characters: <c>a-z</c>, <c>0-9</c>, <c>.</c>, <c>_</c>, <c>-</c>.</item>
///   <item>First character must be alphanumeric (<c>a-z</c> or <c>0-9</c>): leading separators are rejected.</item>
/// </list>
/// </para>
/// </remarks>
public sealed partial record Username
{
    public string Value { get; }

    private Username(string value) => Value = value;

    /// <summary>Inclusive minimum length after normalization.</summary>
    public const int MinLength = 3;

    /// <summary>Inclusive maximum length after normalization.</summary>
    public const int MaxLength = 64;

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernameRegex();

    /// <summary>
    /// Normalizes and validates the supplied raw username, returning the value object.
    /// </summary>
    /// <param name="raw">The raw input. May contain leading/trailing whitespace and uppercase letters.</param>
    /// <exception cref="DomainException">
    /// Thrown with <see cref="UserErrors.UsernameInvalid"/> when the normalized value
    /// fails any of the length, charset, or leading-character rules.
    /// </exception>
    public static Username Create(string? raw)
    {
        if (raw is null)
        {
            throw new DomainException(UserErrors.UsernameInvalid);
        }

        var normalized = raw.Trim().ToLowerInvariant();

        if (normalized.Length is < MinLength or > MaxLength)
        {
            throw new DomainException(UserErrors.UsernameInvalid);
        }

        if (!UsernameRegex().IsMatch(normalized))
        {
            throw new DomainException(UserErrors.UsernameInvalid);
        }

        return new Username(normalized);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
