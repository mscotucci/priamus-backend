using System.Text.RegularExpressions;
using Priamus.Trips.SharedKernel.Domain;

namespace Priamus.Trips.Registry.Domain.Customers.ValueObjects;

/// <summary>
/// Italian tax code of a <see cref="Customer"/> in its normal form: uppercase, spaces removed,
/// 16 ASCII letters and digits or 11 ASCII digits (PRD §4, RF-15). Not unique, and the check
/// character is not verified.
/// </summary>
public sealed partial record TaxCode
{
    private TaxCode(string value) => Value = value;

    /// <summary>The tax code in normal form.</summary>
    public string Value { get; }

    [GeneratedRegex(@"\A([A-Z0-9]{16}|[0-9]{11})\z", RegexOptions.CultureInvariant)]
    private static partial Regex NormalFormRegex();

    /// <summary>
    /// Pure normalisation: uppercase, spaces removed. Does not validate, so it also serves
    /// partial search text. A <c>null</c> input gives an empty string.
    /// </summary>
    /// <param name="raw">The raw text.</param>
    public static string Normalize(string? raw) =>
        raw is null
            ? string.Empty
            : raw.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    /// <summary>
    /// Whether <paramref name="raw"/> normalises to a valid tax code. Used by the request validator.
    /// </summary>
    /// <param name="raw">The raw text.</param>
    public static bool IsValid(string? raw) => NormalFormRegex().IsMatch(Normalize(raw));

    /// <summary>
    /// Normalises and validates the raw tax code.
    /// </summary>
    /// <param name="raw">The raw text.</param>
    /// <exception cref="DomainException">
    /// Thrown with <see cref="CustomerErrors.TaxCodeInvalid"/> when the normal form is neither 16
    /// ASCII letters and digits nor 11 ASCII digits.
    /// </exception>
    public static TaxCode Create(string? raw)
    {
        var normalized = Normalize(raw);

        if (!NormalFormRegex().IsMatch(normalized))
        {
            throw new DomainException(CustomerErrors.TaxCodeInvalid);
        }

        return new TaxCode(normalized);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Query-side helpers for the <see cref="TaxCode"/> value object.
/// </summary>
/// <remarks>
/// Same role as <see cref="VatNumberQueryExtensions"/>: a scalar DbFunction marker over the
/// <c>varchar</c> column that throws when called client-side.
/// </remarks>
public static class TaxCodeQueryExtensions
{
    /// <summary>
    /// Translated to the <c>tax_code</c> column in EF Core LINQ queries. Never call it
    /// client-side.
    /// </summary>
    /// <param name="taxCode">The tax code property of a customer in the query.</param>
    /// <exception cref="NotSupportedException">Always thrown outside an EF Core translation.</exception>
    public static string Unwrap(this TaxCode? taxCode) =>
        throw new NotSupportedException(
            $"{nameof(TaxCodeQueryExtensions)}.{nameof(Unwrap)} is a SQL-only translation " +
            $"marker and must not be called client-side. Use {nameof(TaxCode)}.{nameof(TaxCode.Value)}.");
}
