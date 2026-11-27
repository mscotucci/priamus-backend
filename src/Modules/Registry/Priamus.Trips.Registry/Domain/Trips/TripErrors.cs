using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Domain.Trips;

/// <summary>Errors of judging and pricing a trip against the Registry.</summary>
/// <remarks>Codes follow the <c>Registry.Trip.{ErrorName}</c> PascalCase convention.</remarks>
public static class TripErrors
{
    /// <summary>
    /// A registry fact the trip relies on does not hold: a vehicle, customer, location or contract that is
    /// gone, inactive, unrated or not in force. The caller cannot tell "changed meanwhile" from "never valid".
    /// </summary>
    public static readonly Error AnswersChanged = Error.Conflict(
        "Registry.Trip.AnswersChanged",
        "Something changed while the trip was being filled in.");

    /// <summary>
    /// The trip cannot be priced: the customer has no contract covering its day, the contract has no rate for
    /// its location or pair, or its amount is above the largest the system holds.
    /// </summary>
    public static readonly Error NotPriceable = Error.Validation(
        "Registry.Trip.NotPriceable",
        "The trip cannot be priced.");

    /// <summary>
    /// The measure sent is not the one the pricing mode of the contract asks: the contract changed while the
    /// driver ended the trip.
    /// </summary>
    public static readonly Error MeasureChanged = Error.Conflict(
        "Registry.Trip.MeasureChanged",
        "The measure of the trip is not the one the contract asks.");
}
