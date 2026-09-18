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

/// <summary>
/// Query-side helpers for the <see cref="Username"/> value object.
/// </summary>
/// <remarks>
/// The <see cref="Unwrap"/> extension is the canonical way to expose
/// <see cref="Username.Value"/> inside an EF Core LINQ expression that must
/// be translated to SQL. EF Core's LINQ translator cannot decompose
/// <c>u.Username.Value</c> on a property mapped via
/// <c>ValueConverter&lt;Username, string&gt;</c> — the value object is an
/// opaque atomic mapping. Calling <c>u.Username.Unwrap()</c> instead is
/// registered as a <c>HasDbFunction</c>-style scalar function on the Identity
/// <c>DbContext</c>, translated as an identity over the underlying
/// <c>varchar</c> column, so that <c>Contains</c> / <c>StartsWith</c> /
/// <c>EndsWith</c> compose on top as ordinary SQL <c>LIKE</c>.
/// <para>
/// The extension intentionally throws when invoked client-side: every call
/// must be inside an EF Core LINQ expression where the DbFunction translation
/// fires before the method body runs. This mirrors the <see cref="Microsoft.EntityFrameworkCore.DbFunctionsExtensions"/>
/// pattern and prevents silent in-memory fallbacks that would behave correctly
/// but bypass the database — masking performance and correctness issues.
/// </para>
/// </remarks>
public static class UsernameQueryExtensions
{
    /// <summary>
    /// Translated to the underlying <c>username</c> column in EF Core LINQ
    /// queries. Never call this method client-side — it throws.
    /// </summary>
    /// <param name="username">Annotated nullable so callers do not need a
    /// preceding null check inside a query lambda; EF Core ignores the
    /// annotation and treats the parameter as the bound column expression.</param>
    /// <exception cref="NotSupportedException">
    /// Always thrown when invoked outside an EF Core LINQ translation.
    /// </exception>
    public static string Unwrap(this Username? username) =>
        throw new NotSupportedException(
            $"{nameof(UsernameQueryExtensions)}.{nameof(Unwrap)} is a SQL-only " +
            "translation marker and must not be called client-side. Use " +
            $"{nameof(Username)}.{nameof(Username.Value)} for in-memory access.");
}
