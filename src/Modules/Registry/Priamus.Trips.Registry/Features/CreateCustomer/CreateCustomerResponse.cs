using Priamus.Trips.Registry.Domain.Customers;

namespace Priamus.Trips.Registry.Features.CreateCustomer;

/// <summary>
/// Response body for a successful <c>POST /api/registry/customers</c> request.
/// </summary>
/// <param name="Id">The newly created customer's identifier.</param>
/// <param name="CompanyName">The trimmed company name.</param>
/// <param name="VatNumber">The VAT number in normal form, or <c>null</c>.</param>
/// <param name="TaxCode">The tax code in normal form, or <c>null</c>.</param>
/// <param name="Address">The trimmed address, or <c>null</c>.</param>
/// <param name="Status">The status (always <see cref="CustomerStatus.Active"/> on creation).</param>
/// <param name="LastModifiedAt">UTC instant of the last change.</param>
/// <param name="Version">Version the client sends back with its next write on this customer.</param>
public sealed record CreateCustomerResponse(
    Guid Id,
    string CompanyName,
    string? VatNumber,
    string? TaxCode,
    string? Address,
    CustomerStatus Status,
    DateTime LastModifiedAt,
    int Version);
