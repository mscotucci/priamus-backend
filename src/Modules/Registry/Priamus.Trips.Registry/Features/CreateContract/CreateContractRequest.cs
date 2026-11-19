using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Features.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.CreateContract;

/// <summary>
/// Request body for <c>POST /api/registry/customers/{customerId}/contracts</c> (PRD §5.4, RF-16).
/// Every member but the end of the validity and the seven rate members is required; an empty set
/// of documents is valid and duplicates collapse. The rate members are required for a flat-rate
/// contract (the night ones with the night surcharge, the minimum ones as its rule asks) and must
/// be absent for any other billing level.
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
    decimal? MinimumOveragePrice) : IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        ContractRequestValidation.Validate(
            ValidFrom, ValidTo, BillingLevel, PricingMode, NightSurcharge, RateMembers());

    /// <summary>The rate members, gathered for validation and for building the rate.</summary>
    internal ContractRateMembers RateMembers() =>
        new(DayPrice, NightPrice, NightCallFee, MinimumRule, MinimumAmount, MinimumThreshold, MinimumOveragePrice);
}
