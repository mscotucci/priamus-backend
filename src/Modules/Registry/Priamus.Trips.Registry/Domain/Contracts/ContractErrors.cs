using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Domain.Contracts;

/// <summary>
/// Errors raised by the <see cref="Contract"/> aggregate, its validity, its rate and the
/// application-layer lookups on contracts.
/// </summary>
/// <remarks>
/// Codes follow the <c>Registry.Contract.{ErrorName}</c> PascalCase convention.
/// </remarks>
public static class ContractErrors
{
    /// <summary>The end of the validity is before its start (PRD §5.4).</summary>
    public static readonly Error ValidityEndBeforeStart = Error.Validation(
        "Registry.Contract.ValidityEndBeforeStart",
        "The validity end date cannot be before its start date.");

    /// <summary>The requested contract was not found for the customer.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "Registry.Contract.NotFound",
        "The requested contract was not found.");

    /// <summary>
    /// The validity shares at least one day with another contract of the customer; the other
    /// contract's validity is passed as the message argument (PRD §5.4, RF-18).
    /// </summary>
    public static readonly Error Overlap = Error.Conflict(
        "Registry.Contract.Overlap",
        "The validity overlaps another contract of the customer.");

    /// <summary>
    /// The validity was taken by a save that won the race on the exclusion constraint, and the
    /// other contract can no longer be named.
    /// </summary>
    public static readonly Error OverlapWithAnother = Error.Conflict(
        "Registry.Contract.OverlapWithAnother",
        "The validity overlaps another contract of the customer.");

    /// <summary>A contract cannot be created for a deactivated customer.</summary>
    public static readonly Error CustomerDeactivated = Error.Conflict(
        "Registry.Contract.CustomerDeactivated",
        "The customer is deactivated: contracts cannot be added.");

    /// <summary>
    /// The version sent with a write does not match the stored one: someone else changed the
    /// contract in the meantime.
    /// </summary>
    public static readonly Error ConcurrentUpdate = Error.Conflict(
        "Registry.Contract.ConcurrentUpdate",
        "The contract was modified by another user in the meantime.");

    /// <summary>The day price is not greater than 0.</summary>
    public static readonly Error DayPriceNotPositive = Error.Validation(
        "Registry.Contract.DayPriceNotPositive",
        "The day price must be greater than zero.");

    /// <summary>The night price is not greater than 0.</summary>
    public static readonly Error NightPriceNotPositive = Error.Validation(
        "Registry.Contract.NightPriceNotPositive",
        "The night price must be greater than zero.");

    /// <summary>The night call fee is negative.</summary>
    public static readonly Error NightCallFeeNegative = Error.Validation(
        "Registry.Contract.NightCallFeeNegative",
        "The night call fee cannot be negative.");

    /// <summary>The minimum amount is not greater than 0.</summary>
    public static readonly Error MinimumAmountNotPositive = Error.Validation(
        "Registry.Contract.MinimumAmountNotPositive",
        "The minimum amount must be greater than zero.");

    /// <summary>The threshold of the tiered overage is not greater than 0.</summary>
    public static readonly Error MinimumThresholdNotPositive = Error.Validation(
        "Registry.Contract.MinimumThresholdNotPositive",
        "The minimum threshold must be greater than zero.");

    /// <summary>The overage price of the tiered overage is not greater than 0.</summary>
    public static readonly Error MinimumOveragePriceNotPositive = Error.Validation(
        "Registry.Contract.MinimumOveragePriceNotPositive",
        "The minimum overage price must be greater than zero.");

    /// <summary>An amount of the rate has more than 2 decimals.</summary>
    public static readonly Error AmountTooPrecise = Error.Validation(
        "Registry.Contract.AmountTooPrecise",
        "An amount can have at most two decimals.");

    /// <summary>An amount of the rate is above the largest the rate holds.</summary>
    public static readonly Error AmountTooLarge = Error.Validation(
        "Registry.Contract.AmountTooLarge",
        "An amount is too large.");

    /// <summary>A flat-rate contract has no rate.</summary>
    public static readonly Error RateRequired = Error.Validation(
        "Registry.Contract.RateRequired",
        "A flat-rate contract needs a rate.");

    /// <summary>A contract of a billing level other than flat rate holds a rate.</summary>
    public static readonly Error RateNotApplicable = Error.Validation(
        "Registry.Contract.RateNotApplicable",
        "Only a flat-rate contract has a rate.");

    /// <summary>The night surcharge is on and the rate lacks the night price or the night call fee.</summary>
    public static readonly Error NightRateMissing = Error.Validation(
        "Registry.Contract.NightRateMissing",
        "A contract with the night surcharge needs a night price and a night call fee.");

    /// <summary>The night surcharge is off and the rate holds a night price or a night call fee.</summary>
    public static readonly Error NightRateNotApplicable = Error.Validation(
        "Registry.Contract.NightRateNotApplicable",
        "A contract without the night surcharge has no night price and no night call fee.");

    /// <summary>A contract priced by trip holds a minimum rule other than none.</summary>
    public static readonly Error MinimumNotApplicableByTrip = Error.Validation(
        "Registry.Contract.MinimumNotApplicableByTrip",
        "A contract priced by trip has no minimum rule.");

    /// <summary>
    /// The contract belongs to a deactivated customer, so its terms cannot be changed.
    /// </summary>
    public static readonly Error CustomerDeactivatedReadOnly = Error.Conflict(
        "Registry.Contract.CustomerDeactivatedReadOnly",
        "The customer is deactivated: its contracts cannot be modified.");
}
