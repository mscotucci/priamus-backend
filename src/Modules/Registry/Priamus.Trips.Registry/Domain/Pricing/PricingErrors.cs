using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Domain.Pricing;

/// <summary>Errors raised by the night band, the trip measures and the pricing engine.</summary>
/// <remarks>Codes follow the <c>Registry.{Area}.{ErrorName}</c> PascalCase convention.</remarks>
public static class PricingErrors
{
    /// <summary>The end of the night band is the same minute as its start.</summary>
    public static readonly Error NightBandEndEqualsStart = Error.Validation(
        "Registry.NightBand.EndEqualsStart",
        "The end of the night band must differ from its start.");

    /// <summary>The end of a trip by hour is the same minute as its start.</summary>
    public static readonly Error TripEndEqualsStart = Error.Validation(
        "Registry.Pricing.TripEndEqualsStart",
        "The end of the trip must differ from its start.");

    /// <summary>The kilograms or kilometres of a trip are not a whole number greater than zero.</summary>
    public static readonly Error QuantityNotPositive = Error.Validation(
        "Registry.Pricing.QuantityNotPositive",
        "The quantity must be a whole number greater than zero.");

    /// <summary>The kilograms of a trip are above the largest accepted.</summary>
    public static readonly Error KilogramsTooLarge = Error.Validation(
        "Registry.Pricing.KilogramsTooLarge",
        "The weight cannot exceed 99,999 kg.");

    /// <summary>The kilometres of a trip are above the largest accepted.</summary>
    public static readonly Error KilometresTooLarge = Error.Validation(
        "Registry.Pricing.KilometresTooLarge",
        "The distance cannot exceed 9,999 km.");

    /// <summary>A value the pricing mode needs to measure a trip (end time, kilograms or kilometres) is missing.</summary>
    public static readonly Error MeasureMissing = Error.Validation(
        "Registry.Pricing.MeasureMissing",
        "A value needed to measure the trip is missing.");

    /// <summary>The total of a computation is above the largest amount the system holds.</summary>
    public static readonly Error AmountTooLarge = Error.Domain(
        "Registry.Pricing.AmountTooLarge",
        "The computed amount is too large.");

    /// <summary>A location contract holds no rate for the location or pair of the trip.</summary>
    public static readonly Error NoRateForLocation = Error.Domain(
        "Registry.Pricing.NoRateForLocation",
        "There is no rate for the locations of the trip.");
}
