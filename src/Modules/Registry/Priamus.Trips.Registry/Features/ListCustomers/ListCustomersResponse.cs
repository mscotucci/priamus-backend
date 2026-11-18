using Priamus.Trips.Registry.Domain.Customers;

namespace Priamus.Trips.Registry.Features.ListCustomers;

/// <summary>
/// A single item in the paginated list of customers.
/// </summary>
/// <param name="Id">The customer's identifier.</param>
/// <param name="CompanyName">The company name.</param>
/// <param name="VatNumber">The VAT number in normal form, or <c>null</c>.</param>
/// <param name="TaxCode">The tax code in normal form, or <c>null</c>.</param>
/// <param name="Address">The address, or <c>null</c>.</param>
/// <param name="Status">The status.</param>
/// <param name="LastModifiedAt">UTC instant of the last change.</param>
/// <param name="Version">Version the client sends back with its next write on this customer.</param>
public sealed record CustomerListItemDto(
    Guid Id,
    string CompanyName,
    string? VatNumber,
    string? TaxCode,
    string? Address,
    CustomerStatus Status,
    DateTime LastModifiedAt,
    int Version);
