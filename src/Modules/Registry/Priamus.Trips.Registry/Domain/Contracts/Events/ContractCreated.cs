using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.SharedKernel.Domain;

namespace Priamus.Trips.Registry.Domain.Contracts.Events;

/// <summary>
/// Raised when a <see cref="Contract"/> is created via <see cref="Contract.Create"/>.
/// Carries the full state after the change.
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant of the creation.</param>
/// <param name="ContractId">Identifier of the new contract.</param>
/// <param name="CustomerId">Customer the contract belongs to.</param>
/// <param name="ValidFrom">First day of the validity.</param>
/// <param name="ValidTo">Last day of the validity, or <c>null</c> when open.</param>
/// <param name="BillingLevel">Billing level.</param>
/// <param name="PricingMode">Pricing mode.</param>
/// <param name="RequiredDocuments">Required documents in canonical order.</param>
/// <param name="NightSurcharge">Whether night trips cost more.</param>
/// <param name="Rate">The rate; <c>null</c> when the contract has none.</param>
/// <param name="VehicleRates">The vehicle rates; empty when the contract has none.</param>
public sealed record ContractCreated(
    Guid EventId,
    DateTime OccurredAt,
    ContractId ContractId,
    CustomerId CustomerId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    BillingLevel BillingLevel,
    PricingMode PricingMode,
    IReadOnlyList<RequiredDocument> RequiredDocuments,
    bool NightSurcharge,
    Rate? Rate,
    IReadOnlyList<VehicleRate> VehicleRates) : IDomainEvent;
