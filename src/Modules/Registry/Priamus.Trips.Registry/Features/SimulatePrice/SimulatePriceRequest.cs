using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Pricing;
using Priamus.Trips.Registry.Features.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.SimulatePrice;

/// <summary>
/// Request body for <c>POST /api/registry/contracts/simulation</c>: the terms of a contract as
/// written on the page, saved or not, and a sample trip. The billing level, the rate members, the vehicle
/// rates and the location rates follow the contract requests; <c>StartTime</c> and <c>EndTime</c> are
/// <c>HH:mm</c>. The end is for a trip by hour
/// only, <c>Kilograms</c> for a trip by weight only and <c>Kilometres</c> for a trip by distance only;
/// the quantities are whole numbers, read as numbers so that a fraction is reported on its own member.
/// <c>VehicleId</c> is the trip's vehicle: <c>null</c>, or a vehicle without a vehicle rate on the trip's
/// location or pair, uses the contract's rate on a flat-rate contract and the rate of the location or pair
/// on a location level. <c>PickupLocationId</c> and <c>DeliveryLocationId</c> are the
/// trip's locations: each is required when the billing level uses it, refused when it does not, and must
/// have a rate among the written location rates.
/// </summary>
[ValidatableType]
public sealed record SimulatePriceRequest(
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    BillingLevel? BillingLevel,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    PricingMode? PricingMode,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    bool? NightSurcharge,
    decimal? DayPrice,
    decimal? NightPrice,
    decimal? NightCallFee,
    MinimumRuleKind? MinimumRule,
    decimal? MinimumAmount,
    decimal? MinimumThreshold,
    decimal? MinimumOveragePrice,
    IReadOnlyList<ContractVehicleRateRequest>? VehicleRates,
    IReadOnlyList<ContractLocationRateRequest>? LocationRates,
    Guid? VehicleId,
    Guid? PickupLocationId,
    Guid? DeliveryLocationId,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    [TimeOfDayFormat]
    string StartTime,
    [TimeOfDayFormat]
    string? EndTime,
    decimal? Kilograms,
    decimal? Kilometres) : IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in ContractRequestValidation.Validate(
            null, null, BillingLevel, PricingMode, NightSurcharge, RateMembers(), VehicleRates, LocationRates))
        {
            yield return result;
        }

        foreach (var result in ValidateTripLocations())
        {
            yield return result;
        }

        foreach (var result in ValidateTrip())
        {
            yield return result;
        }
    }

    /// <summary>The rate members, gathered for validation and for building the rate.</summary>
    internal ContractRateMembers RateMembers() =>
        new(DayPrice, NightPrice, NightCallFee, MinimumRule, MinimumAmount, MinimumThreshold, MinimumOveragePrice);

    private IEnumerable<ValidationResult> ValidateTripLocations()
    {
        // A missing BillingLevel is reported on its own: the trip's locations are not judged without it.
        if (BillingLevel is not { } level)
        {
            yield break;
        }

        var (usesPickup, usesDelivery) = level.NamedLocations();

        var memberReported = false;
        foreach (var (value, used, member) in new[]
        {
            (PickupLocationId, usesPickup, nameof(PickupLocationId)),
            (DeliveryLocationId, usesDelivery, nameof(DeliveryLocationId)),
        })
        {
            if (value is null && used)
            {
                yield return new ValidationResult(RegistryMessages.Validation_Required, [member]);
                memberReported = true;
            }
            else if (value is not null && !used)
            {
                yield return NotApplicable(member);
                memberReported = true;
            }
        }

        // Without written rates the list is reported on its own, whatever the trip names.
        if (memberReported || LocationRates is not { Count: > 0 } rates || !(usesPickup || usesDelivery))
        {
            yield break;
        }

        var written = rates.Where(r => r is not null).ToList();
        var hasRate = level switch
        {
            Domain.Contracts.BillingLevel.PerPickupLocation => written.Any(r => r.PickupLocationId == PickupLocationId),
            Domain.Contracts.BillingLevel.PerDeliveryLocation => written.Any(r => r.DeliveryLocationId == DeliveryLocationId),
            _ => written.Any(r => r.PickupLocationId == PickupLocationId && r.DeliveryLocationId == DeliveryLocationId),
        };

        if (!hasRate)
        {
            var member = usesDelivery ? nameof(DeliveryLocationId) : nameof(PickupLocationId);
            yield return new ValidationResult(RegistryMessages.Registry_Pricing_NoRateForLocation, [member]);
        }
    }

    private IEnumerable<ValidationResult> ValidateTrip()
    {
        // A missing pricing mode is reported on its own: the trip members are not judged without it.
        if (PricingMode is not { } mode)
        {
            yield break;
        }

        if (mode != Domain.Contracts.PricingMode.ByHour)
        {
            if (EndTime is not null)
            {
                yield return NotApplicable(nameof(EndTime));
            }
        }
        else if (string.IsNullOrWhiteSpace(EndTime))
        {
            yield return new ValidationResult(RegistryMessages.Validation_Required, [nameof(EndTime)]);
        }
        else if (TimeOfDayFormatAttribute.Parse(StartTime) is { } start
            && TimeOfDayFormatAttribute.Parse(EndTime) is { } end
            && TripMeasure.ByHour.Create(start, end).IsFailure)
        {
            yield return new ValidationResult(RegistryMessages.Registry_Pricing_TripEndEqualsStart, [nameof(EndTime)]);
        }

        foreach (var result in ValidateQuantity(
            Kilograms,
            mode == Domain.Contracts.PricingMode.ByWeight,
            nameof(Kilograms),
            TripMeasure.MaxKilograms,
            RegistryMessages.Registry_Pricing_KilogramsTooLarge))
        {
            yield return result;
        }

        foreach (var result in ValidateQuantity(
            Kilometres,
            mode == Domain.Contracts.PricingMode.ByDistance,
            nameof(Kilometres),
            TripMeasure.MaxKilometres,
            RegistryMessages.Registry_Pricing_KilometresTooLarge))
        {
            yield return result;
        }
    }

    private static IEnumerable<ValidationResult> ValidateQuantity(
        decimal? value, bool applies, string member, int max, string tooLargeMessage)
    {
        if (value is not { } quantity)
        {
            if (applies)
            {
                yield return new ValidationResult(RegistryMessages.Validation_Required, [member]);
            }
        }
        else if (!applies)
        {
            yield return NotApplicable(member);
        }
        else if (quantity <= 0 || quantity != decimal.Truncate(quantity))
        {
            yield return new ValidationResult(RegistryMessages.Registry_Pricing_QuantityNotPositive, [member]);
        }
        else if (quantity > max)
        {
            yield return new ValidationResult(tooLargeMessage, [member]);
        }
    }

    private static ValidationResult NotApplicable(string member) =>
        new(RegistryMessages.Registry_Contract_ValueNotApplicable, [member]);
}
