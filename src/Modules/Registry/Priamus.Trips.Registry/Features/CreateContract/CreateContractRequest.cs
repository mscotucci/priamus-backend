using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Features.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.CreateContract;

/// <summary>
/// Request body for <c>POST /api/registry/customers/{customerId}/contracts</c> (PRD §5.4, RF-16).
/// Every member but the end of the validity, the seven rate members, the vehicle rates and the
/// location rates is required; an empty set of documents is valid and duplicates collapse. The
/// rate members are required for a flat-rate contract (the night ones with the night surcharge,
/// the minimum ones as its rule asks) and must be absent for any other billing level, as must the
/// vehicle rates. The location rates are required, at least one, for any other billing level and
/// must be absent for a flat-rate contract. An absent list is an empty one.
/// </summary>
[ValidatableType]
public sealed record CreateContractRequest(
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    BillingLevel? BillingLevel,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    PricingMode? PricingMode,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    IReadOnlyList<RequiredDocument>? RequiredDocuments,
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
    IReadOnlyList<ContractLocationRateRequest>? LocationRates) : IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        ContractRequestValidation.Validate(
            ValidFrom, ValidTo, BillingLevel, PricingMode, NightSurcharge, RateMembers(), VehicleRates, LocationRates);

    /// <summary>The rate members, gathered for validation and for building the rate.</summary>
    internal ContractRateMembers RateMembers() =>
        new(DayPrice, NightPrice, NightCallFee, MinimumRule, MinimumAmount, MinimumThreshold, MinimumOveragePrice);
}
