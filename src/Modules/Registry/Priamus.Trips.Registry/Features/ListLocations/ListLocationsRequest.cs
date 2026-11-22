using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Resources;
using Priamus.Trips.SharedKernel.Paging;

namespace Priamus.Trips.Registry.Features.ListLocations;

/// <summary>
/// Query-string parameters for <c>GET /api/registry/locations</c>.
/// </summary>
/// <remarks>
/// Mutable class (not record) required by ASP.NET Core query-string model binding.
/// Bound via <c>[AsParameters]</c> in the endpoint delegate signature.
/// </remarks>
[ValidatableType]
public sealed class ListLocationsRequest : PagedRequest
{
    /// <summary>
    /// Text matched, ignoring case, against part of the name, the municipality or the address.
    /// When null or whitespace, no search is applied.
    /// </summary>
    [MaxLength(100, ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_MaxLength))]
    public string? Search { get; set; }

    /// <summary>Filter by status. When null, all statuses are included.</summary>
    public LocationStatus? Status { get; set; }
}
