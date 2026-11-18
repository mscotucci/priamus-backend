using Priamus.Trips.Registry.Domain.Contracts;

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
    int Version)
{
    /// <summary>Maps a contract, computing its status on <paramref name="today"/>.</summary>
    /// <param name="contract">The contract.</param>
    /// <param name="today">Today in Italian time.</param>
    internal static ContractDto From(Contract contract, DateOnly today) =>
        new(
            contract.Id.Value,
            contract.CustomerId.Value,
            contract.Validity.ValidFrom,
            contract.Validity.ValidTo,
            contract.Validity.StatusOn(today),
            contract.BillingLevel,
            contract.PricingMode,
            contract.RequiredDocuments,
            contract.NightSurcharge,
            contract.Version);
}
