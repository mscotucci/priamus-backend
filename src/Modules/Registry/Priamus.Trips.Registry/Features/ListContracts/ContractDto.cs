using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;

namespace Priamus.Trips.Registry.Features.ListContracts;

/// <summary>
/// A contract as the API returns it.
/// </summary>
/// <param name="Id">The contract's identifier.</param>
/// <param name="CustomerId">The customer the contract belongs to.</param>
/// <param name="ValidFrom">First day of the validity.</param>
/// <param name="ValidTo">Last day of the validity, or <c>null</c> when the contract is open.</param>
/// <param name="Status">Where today, in Italian time, falls against the validity; derived, never stored.</param>
/// <param name="BillingLevel">The billing level.</param>
/// <param name="PricingMode">The pricing mode.</param>
/// <param name="RequiredDocuments">The required documents, in canonical order.</param>
/// <param name="NightSurcharge">Whether night trips cost more.</param>
/// <param name="DayPrice">The day price; <c>null</c> when the contract has no rate.</param>
/// <param name="NightPrice">The night price; <c>null</c> without the night surcharge or a rate.</param>
/// <param name="NightCallFee">The night call fee; <c>null</c> without the night surcharge or a rate.</param>
/// <param name="MinimumRule">The kind of minimum rule; <c>null</c> when the contract has no rate.</param>
/// <param name="MinimumAmount">The minimum amount M; <c>null</c> unless the rule has one.</param>
/// <param name="MinimumThreshold">The threshold X; <c>null</c> unless the rule is tiered overage.</param>
/// <param name="MinimumOveragePrice">The overage price Y; <c>null</c> unless the rule is tiered overage.</param>
/// <param name="VehicleRates">The vehicle rates, ordered by license plate; empty when the contract has none.</param>
/// <param name="LocationRates">The location rates, ordered by pickup then delivery location; empty when the contract has none.</param>
/// <param name="Version">Version the client sends back with its next write on this contract.</param>
public sealed record ContractDto(
    Guid Id,
    Guid CustomerId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    ContractStatus Status,
    BillingLevel BillingLevel,
    PricingMode PricingMode,
    IReadOnlyList<RequiredDocument> RequiredDocuments,
    bool NightSurcharge,
    decimal? DayPrice,
    decimal? NightPrice,
    decimal? NightCallFee,
    MinimumRuleKind? MinimumRule,
    decimal? MinimumAmount,
    decimal? MinimumThreshold,
    decimal? MinimumOveragePrice,
    IReadOnlyList<ContractVehicleRateDto> VehicleRates,
    IReadOnlyList<ContractLocationRateDto> LocationRates,
    int Version)
{
    /// <summary>Maps a contract, computing its status on <paramref name="today"/>.</summary>
    /// <param name="contract">The contract.</param>
    /// <param name="today">Today in Italian time.</param>
    /// <param name="vehicleRates">The contract's vehicle rates, as <see cref="ContractVehicleRateDto.LoadAsync"/> builds them.</param>
    /// <param name="locationRates">The contract's location rates, as <see cref="ContractLocationRateDto.LoadAsync"/> builds them.</param>
    internal static ContractDto From(
        Contract contract,
        DateOnly today,
        IReadOnlyList<ContractVehicleRateDto> vehicleRates,
        IReadOnlyList<ContractLocationRateDto> locationRates)
    {
        var rate = contract.Rate;
        return new(
            contract.Id.Value,
            contract.CustomerId.Value,
            contract.Validity.ValidFrom,
            contract.Validity.ValidTo,
            contract.Validity.StatusOn(today),
            contract.BillingLevel,
            contract.PricingMode,
            contract.RequiredDocuments,
            contract.NightSurcharge,
            rate?.DayPrice,
            rate?.NightPrice,
            rate?.NightCallFee,
            rate?.Minimum.Kind,
            rate?.Minimum.Amount,
            rate?.Minimum.Threshold,
            rate?.Minimum.OveragePrice,
            vehicleRates,
            locationRates,
            contract.Version);
    }
}
