using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Resources;
using Priamus.Trips.SharedKernel.Paging;

namespace Priamus.Trips.Registry.Features.ListVehicles;

/// <summary>
/// Query-string parameters for <c>GET /api/registry/vehicles</c>.
/// </summary>
/// <remarks>
/// Mutable class (not record) required by ASP.NET Core query-string model binding.
/// Bound via <c>[AsParameters]</c> in the endpoint delegate signature.
/// </remarks>
[ValidatableType]
public sealed class ListVehiclesRequest : PagedRequest
{
    /// <summary>
    /// Text matched, normalised as a plate, against part of the plate, or, ignoring case, against
    /// part of the description. When null or whitespace, no search is applied.
    /// </summary>
    [MaxLength(100, ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_MaxLength))]
    public string? Search { get; set; }

    /// <summary>Filter by type. When null, all types are included.</summary>
    public VehicleType? Type { get; set; }

    /// <summary>Filter by status. When null, all statuses are included.</summary>
    public VehicleStatus? Status { get; set; }
}
