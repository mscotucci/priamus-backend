using System.Text.RegularExpressions;
using Priamus.Trips.SharedKernel.Domain;

namespace Priamus.Trips.Registry.Domain.Vehicles.ValueObjects;

/// <summary>
/// License plate of a <see cref="Vehicle"/> in its normal form: uppercase, spaces and hyphens
/// removed, 2 to 10 ASCII letters and digits (PRD §6.6, RF-26). No national format is checked.
/// </summary>
public sealed partial record LicensePlate
{
    /// <summary>Inclusive minimum length of the normal form.</summary>
    public const int MinLength = 2;

    /// <summary>Inclusive maximum length of the normal form.</summary>
    public const int MaxLength = 10;

    private LicensePlate(string value) => Value = value;

    /// <summary>The plate in normal form.</summary>
    public string Value { get; }

    [GeneratedRegex(@"\A[A-Z0-9]{2,10}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NormalFormRegex();

    /// <summary>
    /// Pure normalisation: uppercase, spaces and hyphens removed. Does not validate, so it also
    /// serves partial search text. A <c>null</c> input gives an empty string.
    /// </summary>
    /// <param name="raw">The raw text.</param>
    public static string Normalize(string? raw) =>
        raw is null
            ? string.Empty
            : raw.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .ToUpperInvariant();

    /// <summary>
    /// Whether <paramref name="raw"/> normalises to a valid plate. Used by the request validator.
    /// </summary>
    /// <param name="raw">The raw text.</param>
    public static bool IsValid(string? raw) => NormalFormRegex().IsMatch(Normalize(raw));

    /// <summary>
    /// Normalises and validates the raw plate.
    /// </summary>
    /// <param name="raw">The raw text.</param>
    /// <exception cref="DomainException">
    /// Thrown with <see cref="VehicleErrors.LicensePlateInvalid"/> when the normal form is not
    /// 2 to 10 ASCII letters and digits.
    /// </exception>
    public static LicensePlate Create(string? raw)
    {
        var normalized = Normalize(raw);

        if (!NormalFormRegex().IsMatch(normalized))
        {
            throw new DomainException(VehicleErrors.LicensePlateInvalid);
        }

        return new LicensePlate(normalized);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Query-side helpers for the <see cref="LicensePlate"/> value object.
/// </summary>
/// <remarks>
/// EF Core cannot decompose <c>v.LicensePlate.Value</c> on a property mapped through a value
/// converter. <see cref="Unwrap"/> is registered as a scalar DbFunction on the Registry context,
/// translated as an identity over the <c>varchar</c> column, so that <c>Contains</c> composes as
/// an ordinary SQL <c>LIKE</c>. It throws when called client-side.
/// </remarks>
public static class LicensePlateQueryExtensions
{
    /// <summary>
    /// Translated to the <c>license_plate</c> column in EF Core LINQ queries. Never call it
    /// client-side.
    /// </summary>
    /// <param name="licensePlate">The plate property of a vehicle in the query.</param>
    /// <exception cref="NotSupportedException">Always thrown outside an EF Core translation.</exception>
    public static string Unwrap(this LicensePlate? licensePlate) =>
        throw new NotSupportedException(
            $"{nameof(LicensePlateQueryExtensions)}.{nameof(Unwrap)} is a SQL-only translation " +
            $"marker and must not be called client-side. Use {nameof(LicensePlate)}.{nameof(LicensePlate.Value)}.");
}
