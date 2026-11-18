using System.Linq.Expressions;
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
    int Version)
{
    /// <summary>
    /// The projection from a <see cref="Customer"/>, an expression so that EF Core translates it
    /// into the query.
    /// </summary>
    public static readonly Expression<Func<Customer, CustomerListItemDto>> Projection = c => new CustomerListItemDto(
        c.Id.Value,
        c.CompanyName,
        c.VatNumber == null ? null : c.VatNumber.Value,
        c.TaxCode == null ? null : c.TaxCode.Value,
        c.Address,
        c.Status,
        c.LastModifiedAt,
        c.Version);
}
