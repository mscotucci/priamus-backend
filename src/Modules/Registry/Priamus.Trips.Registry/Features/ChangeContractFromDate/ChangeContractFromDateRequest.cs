using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Features.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.ChangeContractFromDate;

/// <summary>
/// Request body for <c>POST /api/registry/customers/{customerId}/contracts/{contractId}/change-from-date</c>:
/// the members of <see cref="UpdateContract.UpdateContractRequest"/> (the terms as written on the
/// page, the <c>Version</c> the client read) plus the day the changed terms start. The validity
/// as written is cut at <c>ChangeFrom</c>.
/// </summary>
[ValidatableType]
public sealed record ChangeContractFromDateRequest(
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
    IReadOnlyList<ContractLocationRateRequest>? LocationRates,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    int? Version,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    DateOnly? ChangeFrom) : IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in ContractRequestValidation.Validate(
            ValidFrom, ValidTo, BillingLevel, PricingMode, NightSurcharge, RateMembers(), VehicleRates, LocationRates))
        {
            yield return result;
        }

        // The new piece starts after the written start and not after the written end.
        if (ChangeFrom is { } day && ValidFrom is { } from && (day <= from || (ValidTo is { } to && day > to)))
        {
            yield return new ValidationResult(
                RegistryMessages.Registry_Contract_ChangeFromOutsideValidity,
                ["ChangeFrom"]);
        }
    }

    /// <summary>The rate members, gathered for validation and for building the rate.</summary>
    internal ContractRateMembers RateMembers() =>
        new(DayPrice, NightPrice, NightCallFee, MinimumRule, MinimumAmount, MinimumThreshold, MinimumOveragePrice);
}
