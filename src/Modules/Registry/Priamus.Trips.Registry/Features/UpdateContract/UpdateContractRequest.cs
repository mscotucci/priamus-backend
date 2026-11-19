using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Features.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.UpdateContract;

/// <summary>
/// Request body for <c>PUT /api/registry/customers/{customerId}/contracts/{contractId}</c>
/// (PRD §5.4, RF-16): the terms of the contract, with its rate members as in
/// <see cref="CreateContract.CreateContractRequest"/>, plus the <c>Version</c> the client read; the
/// save is refused when the contract has changed since.
/// </summary>
[ValidatableType]
public sealed record UpdateContractRequest(
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
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    int? Version) : IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        ContractRequestValidation.Validate(
            ValidFrom, ValidTo, BillingLevel, PricingMode, NightSurcharge, RateMembers());

    /// <summary>The rate members, gathered for validation and for building the rate.</summary>
    internal ContractRateMembers RateMembers() =>
        new(DayPrice, NightPrice, NightCallFee, MinimumRule, MinimumAmount, MinimumThreshold, MinimumOveragePrice);
}
