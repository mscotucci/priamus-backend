using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.Resources;
using Priamus.Trips.SharedKernel.Paging;

namespace Priamus.Trips.Registry.Features.ListDrivers;

/// <summary>
/// Query-string parameters for <c>GET /api/registry/drivers</c>.
/// </summary>
/// <remarks>
/// Mutable class (not record) required by ASP.NET Core query-string model binding.
/// Bound via <c>[AsParameters]</c> in the endpoint delegate signature.
/// </remarks>
[ValidatableType]
public sealed class ListDriversRequest : PagedRequest
{
    /// <summary>
    /// Text matched, ignoring case, against part of the first name or of the last name; with
    /// spaces removed, against part of the phone number with spaces removed. When null or
    /// whitespace, no search is applied.
    /// </summary>
    [MaxLength(100, ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_MaxLength))]
    public string? Search { get; set; }

    /// <summary>Filter by status. When null, all statuses are included.</summary>
    public DriverStatus? Status { get; set; }
}
