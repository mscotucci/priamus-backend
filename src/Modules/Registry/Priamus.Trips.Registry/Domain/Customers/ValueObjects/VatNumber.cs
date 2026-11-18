using System.Text.RegularExpressions;
using Priamus.Trips.SharedKernel.Domain;

namespace Priamus.Trips.Registry.Domain.Customers.ValueObjects;

/// <summary>
/// Italian VAT number of a <see cref="Customer"/> in its normal form: spaces and a leading "IT"
/// removed, 11 ASCII digits whose last digit is the Italian check digit (PRD §4, RF-15).
/// </summary>
public sealed partial record VatNumber
{
    private VatNumber(string value) => Value = value;

    /// <summary>The VAT number in normal form.</summary>
    public string Value { get; }

    [GeneratedRegex(@"\A[0-9]{11}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ElevenDigitsRegex();

    /// <summary>
    /// Pure normalisation: spaces removed, then one leading "IT" in any case removed. Does not
    /// validate, so it also serves partial search text. A <c>null</c> input gives an empty string.
    /// </summary>
    /// <param name="raw">The raw text.</param>
    public static string Normalize(string? raw)
    {
        if (raw is null)
        {
            return string.Empty;
        }

        var withoutSpaces = raw.Replace(" ", string.Empty, StringComparison.Ordinal);

        return withoutSpaces.StartsWith("IT", StringComparison.OrdinalIgnoreCase)
            ? withoutSpaces[2..]
            : withoutSpaces;
    }

    /// <summary>
    /// Whether <paramref name="raw"/> normalises to 11 digits ending with the right check digit.
    /// Used by the request validator.
    /// </summary>
    /// <param name="raw">The raw text.</param>
    public static bool IsValid(string? raw) => IsNormalFormValid(Normalize(raw));

    /// <summary>
    /// Normalises and validates the raw VAT number.
    /// </summary>
    /// <param name="raw">The raw text.</param>
    /// <exception cref="DomainException">
    /// Thrown with <see cref="CustomerErrors.VatNumberInvalid"/> when the normal form is not 11
    /// digits with a valid check digit.
    /// </exception>
    public static VatNumber Create(string? raw)
    {
        var normalized = Normalize(raw);

        if (!IsNormalFormValid(normalized))
        {
            throw new DomainException(CustomerErrors.VatNumberInvalid);
        }

        return new VatNumber(normalized);
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static bool IsNormalFormValid(string normalized)
    {
        if (!ElevenDigitsRegex().IsMatch(normalized))
        {
            return false;
        }

        // Odd positions (1st, 3rd, ...) are summed; even positions are doubled, minus 9 above 9.
        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            var digit = normalized[i] - '0';
            if (i % 2 == 1)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        return normalized[10] - '0' == (10 - (sum % 10)) % 10;
    }
}

/// <summary>
/// Query-side helpers for the <see cref="VatNumber"/> value object.
/// </summary>
/// <remarks>
/// EF Core cannot decompose <c>c.VatNumber.Value</c> on a property mapped through a value
/// converter. <see cref="Unwrap"/> is registered as a scalar DbFunction on the Registry context,
/// translated as an identity over the <c>varchar</c> column, so that <c>Contains</c> composes as
/// an ordinary SQL <c>LIKE</c>. It throws when called client-side.
/// </remarks>
public static class VatNumberQueryExtensions
{
    /// <summary>
    /// Translated to the <c>vat_number</c> column in EF Core LINQ queries. Never call it
    /// client-side.
    /// </summary>
    /// <param name="vatNumber">The VAT number property of a customer in the query.</param>
    /// <exception cref="NotSupportedException">Always thrown outside an EF Core translation.</exception>
    public static string Unwrap(this VatNumber? vatNumber) =>
        throw new NotSupportedException(
            $"{nameof(VatNumberQueryExtensions)}.{nameof(Unwrap)} is a SQL-only translation " +
            $"marker and must not be called client-side. Use {nameof(VatNumber)}.{nameof(VatNumber.Value)}.");
}
