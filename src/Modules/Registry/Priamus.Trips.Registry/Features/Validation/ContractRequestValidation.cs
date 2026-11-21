using System.ComponentModel.DataAnnotations;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.Validation;

/// <summary>
/// The cross-field rules shared by the create and edit requests (PRD §5.4, RF-16): the end of the
/// validity is not before its start, and the rate and the minimum fit the billing level, the
/// pricing mode and the night surcharge; the vehicle rates are judged like the rate, each under its
/// own <c>VehicleRates[i]</c> key.
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
        IReadOnlyList<ContractVehicleRateRequest>? vehicleRates)
    {
        if (validFrom is { } from && validTo is { } to && to < from)
        {
            yield return new ValidationResult(
                RegistryMessages.Registry_Contract_ValidityEndBeforeStart,
                ["ValidTo"]);
        }

        foreach (var result in ValidateRate(billingLevel, pricingMode, nightSurcharge, rate))
        {
            yield return result;
        }

        foreach (var result in ValidateVehicleRates(billingLevel, pricingMode, nightSurcharge, vehicleRates))
        {
            yield return result;
        }
    }

    private static IEnumerable<ValidationResult> ValidateVehicleRates(
        BillingLevel? billingLevel,
        PricingMode? pricingMode,
        bool? nightSurcharge,
        IReadOnlyList<ContractVehicleRateRequest>? vehicleRates)
    {
        // A missing BillingLevel is not judged here: it is required and reports its own error.
        if (vehicleRates is not { Count: > 0 } || billingLevel is null)
        {
            yield break;
        }

        if (billingLevel != BillingLevel.FlatRate)
        {
            yield return Error(RegistryMessages.Registry_Contract_ValueNotApplicable, "VehicleRates");
            yield break;
        }

        var seen = new HashSet<Guid>();
        for (var i = 0; i < vehicleRates.Count; i++)
        {
            var key = $"VehicleRates[{i}]";
            if (vehicleRates[i] is not { } item)
            {
                yield return Error(RegistryMessages.Validation_Required, key);
                continue;
            }

            if (item.VehicleId is not { } vehicleId)
            {
                yield return Error(RegistryMessages.Validation_Required, $"{key}.VehicleId");
            }
            else if (!seen.Add(vehicleId))
            {
                yield return Error(RegistryMessages.Registry_Contract_VehicleRateDuplicate, $"{key}.VehicleId");
            }

            foreach (var result in ValidateRate(billingLevel, pricingMode, nightSurcharge, item.RateMembers()))
            {
                yield return new ValidationResult(
                    result.ErrorMessage, [.. result.MemberNames.Select(member => $"{key}.{member}")]);
            }
        }
    }

    private static IEnumerable<ValidationResult> ValidateRate(
        BillingLevel? billingLevel,
        PricingMode? pricingMode,
        bool? nightSurcharge,
        ContractRateMembers rate)
    {
        if (billingLevel is null)
        {
            yield break;
        }

        var flatRate = billingLevel == BillingLevel.FlatRate;
        var night = flatRate && nightSurcharge is true;
        var nightApplies = flatRate && nightSurcharge is not false;

        // A contract priced by trip has no minimum; with the pricing mode missing the minimum is
        // not judged (the mode is reported on its own).
        var minimumAsked = flatRate && pricingMode is { } mode && mode != PricingMode.ByTrip;
        var noMinimumAllowed = flatRate && pricingMode == PricingMode.ByTrip;
        var kind = rate.MinimumRule;
        var amountRequired = minimumAsked && kind is MinimumRuleKind.FixedMinimum or MinimumRuleKind.TieredOverage;
        var tieredRequired = minimumAsked && kind is MinimumRuleKind.TieredOverage;

        // With the pricing mode missing the minimum members are judged by the kind alone.
        var minimumJudged = flatRate && !noMinimumAllowed;
        var amountApplies = minimumJudged && (kind is null or MinimumRuleKind.FixedMinimum or MinimumRuleKind.TieredOverage);
        var tieredApplies = minimumJudged && (kind is null or MinimumRuleKind.TieredOverage);

        ValidationResult?[] checks =
        [
            Positive("DayPrice", rate.DayPrice, required: flatRate, applicable: flatRate),
            Positive("NightPrice", rate.NightPrice, required: night, applicable: nightApplies),
            NonNegative("NightCallFee", rate.NightCallFee, required: night, applicable: nightApplies),
            MinimumRuleMember(kind, required: minimumAsked, applicable: flatRate && (!noMinimumAllowed || kind is MinimumRuleKind.None)),
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
