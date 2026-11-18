using Priamus.Trips.Registry.Domain.Customers.ValueObjects;
using Priamus.Trips.SharedKernel.Domain;

namespace Priamus.Trips.Registry.Domain.Customers.Events;

/// <summary>
/// Raised when a <see cref="Customer"/> is edited via <see cref="Customer.Update"/>.
/// Carries the full state after the change.
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant of the update.</param>
/// <param name="CustomerId">Identifier of the updated customer.</param>
/// <param name="CompanyName">Trimmed company name.</param>
/// <param name="VatNumber">VAT number in normal form, or <c>null</c>.</param>
/// <param name="TaxCode">Tax code in normal form, or <c>null</c>.</param>
/// <param name="Address">Trimmed address, or <c>null</c>.</param>
/// <param name="Status">Status after the change.</param>
public sealed record CustomerUpdated(
    Guid EventId,
    DateTime OccurredAt,
    CustomerId CustomerId,
    string CompanyName,
    VatNumber? VatNumber,
    TaxCode? TaxCode,
    string? Address,
    CustomerStatus Status) : IDomainEvent;
