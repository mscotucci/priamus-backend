using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Operations.Resources;
using Priamus.Trips.Registry.Contracts;

namespace Priamus.Trips.Operations.Features.ListTripVehicles;

/// <summary>
/// Query-string parameters for <c>GET /api/operations/driver/vehicles</c>.
/// </summary>
/// <remarks>
/// Mutable class (not record) required by ASP.NET Core query-string model binding.
/// Bound via <c>[AsParameters]</c> in the endpoint delegate signature.
/// </remarks>
[ValidatableType]
public sealed class ListTripVehiclesRequest
{
    /// <summary>
    /// The role the vehicle is chosen for: <c>Leading</c> (tractors and standalone vehicles) or
    /// <c>Trailer</c>.
    /// </summary>
    [Required(ErrorMessageResourceType = typeof(OperationsMessages), ErrorMessageResourceName = nameof(OperationsMessages.Validation_Required))]
    public TripVehicleRole? Role { get; set; }

    /// <summary>
    /// Text matched, ignoring case, against part of the plate or the description. When null or
    /// whitespace, no search is applied.
    /// </summary>
    [MaxLength(100, ErrorMessageResourceType = typeof(OperationsMessages), ErrorMessageResourceName = nameof(OperationsMessages.Validation_MaxLength))]
    public string? Search { get; set; }
}
