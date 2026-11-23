using System.ComponentModel.DataAnnotations;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.Validation;

/// <summary>
/// The cross-field rules shared by the create and edit requests (PRD §5.4, RF-16): the end of the
/// validity is not before its start, and the rate and the minimum fit the billing level, the
/// pricing mode and the night surcharge; the vehicle rates and the location rates are judged like the
/// rate, each under its own <c>VehicleRates[i]</c> or <c>LocationRates[i]</c> key.
/// </summary>
internal static class ContractRequestValidation
{
    /// <summary>Yields one result per broken rule, each on the field it concerns.</summary>
    public static IEnumerable<ValidationResult> Validate(
        DateOnly? validFrom,
        DateOnly? validTo,
        BillingLevel? billingLevel,
        PricingMode? pricingMode,
        bool? nightSurcharge,
        ContractRateMembers rate,
        IReadOnlyList<ContractVehicleRateRequest>? vehicleRates,
        IReadOnlyList<ContractLocationRateRequest>? locationRates)
    {
        if (validFrom is { } from && validTo is { } to && to < from)
        {
            yield return new ValidationResult(
                RegistryMessages.Registry_Contract_ValidityEndBeforeStart,
                ["ValidTo"]);
        }

        // A missing BillingLevel is not judged here: it is required and reports its own error.
        if (billingLevel is { } level)
        {
            foreach (var result in ValidateRate(level == BillingLevel.FlatRate, pricingMode, nightSurcharge, rate))
            {
                yield return result;
            }
        }

        foreach (var result in ValidateVehicleRates(billingLevel, pricingMode, nightSurcharge, vehicleRates, locationRates))
        {
            yield return result;
        }

        foreach (var result in ValidateLocationRates(billingLevel, pricingMode, nightSurcharge, locationRates))
        {
            yield return result;
        }
    }

    private static IEnumerable<ValidationResult> ValidateVehicleRates(
        BillingLevel? billingLevel,
        PricingMode? pricingMode,
        bool? nightSurcharge,
        IReadOnlyList<ContractVehicleRateRequest>? vehicleRates,
        IReadOnlyList<ContractLocationRateRequest>? locationRates)
    {
        // A missing BillingLevel is not judged here: it is required and reports its own error.
        if (vehicleRates is not { Count: > 0 } || billingLevel is not { } level)
        {
            yield break;
        }

        var isFlatRate = level == BillingLevel.FlatRate;
        var (usesPickup, usesDelivery) = level.NamedLocations();

        // The keys of the contract's location rates; unknown when the location rates are themselves
        // missing or malformed, which is reported on their own keys.
        HashSet<(Guid?, Guid?)>? locationKeys = null;
        if (!isFlatRate && locationRates is { Count: > 0 }
            && locationRates.All(item => item is not null
                && (item.PickupLocationId is not null) == usesPickup
                && (item.DeliveryLocationId is not null) == usesDelivery))
        {
            locationKeys = [.. locationRates.Select(item => (item.PickupLocationId, item.DeliveryLocationId))];
        }

        var seen = new HashSet<(Guid, Guid?, Guid?)>();
        for (var i = 0; i < vehicleRates.Count; i++)
        {
            var key = $"VehicleRates[{i}]";
            if (vehicleRates[i] is not { } item)
            {
                yield return Error(RegistryMessages.Validation_Required, key);
                continue;
            }

            if (item.VehicleId is null)
            {
                yield return Error(RegistryMessages.Validation_Required, $"{key}.VehicleId");
            }

            var pickupJudged = LocationMember(item.PickupLocationId, usesPickup, $"{key}.PickupLocationId");
            var deliveryJudged = LocationMember(item.DeliveryLocationId, usesDelivery, $"{key}.DeliveryLocationId");
            foreach (var result in new[] { pickupJudged, deliveryJudged })
            {
                if (result is not null)
                {
                    yield return result;
                }
            }

            var locationKey = usesDelivery ? $"{key}.DeliveryLocationId" : $"{key}.PickupLocationId";
            if (item.VehicleId is { } vehicleId
                && !seen.Add((vehicleId, item.PickupLocationId, item.DeliveryLocationId)))
            {
                yield return isFlatRate
                    ? Error(RegistryMessages.Registry_Contract_VehicleRateDuplicate, $"{key}.VehicleId")
                    : Error(RegistryMessages.Registry_Contract_VehicleRateLocationDuplicate, locationKey);
            }
            else if (pickupJudged is null && deliveryJudged is null
                && locationKeys is not null && !locationKeys.Contains((item.PickupLocationId, item.DeliveryLocationId)))
            {
                yield return Error(RegistryMessages.Registry_Contract_VehicleRateWithoutLocationRate, locationKey);
            }

            foreach (var result in ValidateRate(true, pricingMode, nightSurcharge, item.RateMembers()))
            {
                yield return new ValidationResult(
                    result.ErrorMessage, [.. result.MemberNames.Select(member => $"{key}.{member}")]);
            }
        }
    }

    private static IEnumerable<ValidationResult> ValidateLocationRates(
        BillingLevel? billingLevel,
        PricingMode? pricingMode,
        bool? nightSurcharge,
        IReadOnlyList<ContractLocationRateRequest>? locationRates)
    {
        // A missing BillingLevel is not judged here: it is required and reports its own error.
        if (billingLevel is not { } level)
        {
            yield break;
        }

        if (locationRates is not { Count: > 0 })
        {
            if (level != BillingLevel.FlatRate)
            {
                yield return Error(RegistryMessages.Registry_Contract_LocationRatesMissing, "LocationRates");
            }

            yield break;
        }

        if (level == BillingLevel.FlatRate)
        {
            yield return Error(RegistryMessages.Registry_Contract_ValueNotApplicable, "LocationRates");
            yield break;
        }

        var (usesPickup, usesDelivery) = level.NamedLocations();
        var seen = new HashSet<(Guid?, Guid?)>();
        for (var i = 0; i < locationRates.Count; i++)
        {
            var key = $"LocationRates[{i}]";
            if (locationRates[i] is not { } item)
            {
                yield return Error(RegistryMessages.Validation_Required, key);
                continue;
            }

            var pickupJudged = LocationMember(item.PickupLocationId, usesPickup, $"{key}.PickupLocationId");
            var deliveryJudged = LocationMember(item.DeliveryLocationId, usesDelivery, $"{key}.DeliveryLocationId");
            foreach (var result in new[] { pickupJudged, deliveryJudged })
            {
                if (result is not null)
                {
                    yield return result;
                }
            }

            if (pickupJudged is null && deliveryJudged is null
                && !seen.Add((item.PickupLocationId, item.DeliveryLocationId)))
            {
                var duplicateKey = usesDelivery ? $"{key}.DeliveryLocationId" : $"{key}.PickupLocationId";
                yield return Error(RegistryMessages.Registry_Contract_LocationRateDuplicate, duplicateKey);
            }

            foreach (var result in ValidateRate(true, pricingMode, nightSurcharge, item.RateMembers()))
            {
                yield return new ValidationResult(
                    result.ErrorMessage, [.. result.MemberNames.Select(member => $"{key}.{member}")]);
            }
        }
    }

    private static ValidationResult? LocationMember(Guid? value, bool used, string key) => (value, used) switch
    {
        (null, true) => Error(RegistryMessages.Validation_Required, key),
        (not null, false) => Error(RegistryMessages.Registry_Contract_ValueNotApplicable, key),
        _ => null,
    };

    /// <summary>
    /// Judges one rate; <paramref name="rateApplies"/> is whether the rate applies at all: the
    /// contract's own rate only to a flat-rate contract, a vehicle or location rate always.
    /// </summary>
    private static IEnumerable<ValidationResult> ValidateRate(
        bool rateApplies,
        PricingMode? pricingMode,
        bool? nightSurcharge,
        ContractRateMembers rate)
    {
        var night = rateApplies && nightSurcharge is true;
        var nightApplies = rateApplies && nightSurcharge is not false;

        // A contract priced by trip has no minimum; with the pricing mode missing the minimum is
        // not judged (the mode is reported on its own).
        var minimumAsked = rateApplies && pricingMode is { } mode && mode != PricingMode.ByTrip;
        var noMinimumAllowed = rateApplies && pricingMode == PricingMode.ByTrip;
        var kind = rate.MinimumRule;
        var amountRequired = minimumAsked && kind is MinimumRuleKind.FixedMinimum or MinimumRuleKind.TieredOverage;
        var tieredRequired = minimumAsked && kind is MinimumRuleKind.TieredOverage;

        // With the pricing mode missing the minimum members are judged by the kind alone.
        var minimumJudged = rateApplies && !noMinimumAllowed;
        var amountApplies = minimumJudged && (kind is null or MinimumRuleKind.FixedMinimum or MinimumRuleKind.TieredOverage);
        var tieredApplies = minimumJudged && (kind is null or MinimumRuleKind.TieredOverage);

        ValidationResult?[] checks =
        [
            Positive("DayPrice", rate.DayPrice, required: rateApplies, applicable: rateApplies),
            Positive("NightPrice", rate.NightPrice, required: night, applicable: nightApplies),
            NonNegative("NightCallFee", rate.NightCallFee, required: night, applicable: nightApplies),
            MinimumRuleMember(kind, required: minimumAsked, applicable: rateApplies && (!noMinimumAllowed || kind is MinimumRuleKind.None)),
            Positive("MinimumAmount", rate.MinimumAmount, required: amountRequired, applicable: amountApplies),
            Positive("MinimumThreshold", rate.MinimumThreshold, required: tieredRequired, applicable: tieredApplies),
            Positive("MinimumOveragePrice", rate.MinimumOveragePrice, required: tieredRequired, applicable: tieredApplies),
        ];

        foreach (var check in checks)
        {
            if (check is not null)
            {
                yield return check;
            }
        }

        // Only a night price that passed its own checks is compared with the day price.
        if (checks[1] is null && rate is { DayPrice: { } day, NightPrice: { } nightPrice } && nightPrice < day)
        {
            yield return Error(RegistryMessages.Registry_Contract_NightPriceBelowDayPrice, "NightPrice");
        }
    }

    private static ValidationResult? Positive(string member, decimal? value, bool required, bool applicable) =>
        Check(member, value, required, applicable, isOutOfRange: v => v <= 0, RegistryMessages.Validation_PositiveAmount);

    private static ValidationResult? NonNegative(string member, decimal? value, bool required, bool applicable) =>
        Check(member, value, required, applicable, isOutOfRange: v => v < 0, RegistryMessages.Validation_NonNegativeAmount);

    private static ValidationResult? Check(
        string member,
        decimal? value,
        bool required,
        bool applicable,
        Func<decimal, bool> isOutOfRange,
        string outOfRangeMessage)
    {
        if (value is not { } amount)
        {
            return required ? Error(RegistryMessages.Validation_Required, member) : null;
        }

        if (!applicable)
        {
            return Error(RegistryMessages.Registry_Contract_ValueNotApplicable, member);
        }

        if (isOutOfRange(amount))
        {
            return Error(outOfRangeMessage, member);
        }

        if (Rate.HasMoreThanTwoDecimals(amount))
        {
            return Error(RegistryMessages.Validation_AmountDecimals, member);
        }

        return amount > Rate.MaxAmount ? Error(RegistryMessages.Validation_AmountTooLarge, member) : null;
    }

    private static ValidationResult? MinimumRuleMember(MinimumRuleKind? value, bool required, bool applicable)
    {
        if (value is null)
        {
            return required ? Error(RegistryMessages.Validation_Required, "MinimumRule") : null;
        }

        return applicable ? null : Error(RegistryMessages.Registry_Contract_ValueNotApplicable, "MinimumRule");
    }

    private static ValidationResult Error(string message, string member) => new(message, [member]);
}
