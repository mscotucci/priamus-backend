using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Operations.Resources;

namespace Priamus.Trips.Operations.Features.ListTripCustomers;

/// <summary>
/// Query-string parameters for <c>GET /api/operations/driver/customers</c>.
/// </summary>
/// <remarks>
/// Mutable class (not record) required by ASP.NET Core query-string model binding.
/// Bound via <c>[AsParameters]</c> in the endpoint delegate signature.
/// </remarks>
[ValidatableType]
public sealed class ListTripCustomersRequest
{
    /// <summary>
    /// Text matched, ignoring case, against part of the company name or the VAT number. When null or
    /// whitespace, no search is applied.
    /// </summary>
    [MaxLength(100, ErrorMessageResourceType = typeof(OperationsMessages), ErrorMessageResourceName = nameof(OperationsMessages.Validation_MaxLength))]
    public string? Search { get; set; }
}
