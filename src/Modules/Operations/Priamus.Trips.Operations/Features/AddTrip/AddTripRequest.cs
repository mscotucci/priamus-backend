using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Operations.Resources;

namespace Priamus.Trips.Operations.Features.AddTrip;

/// <summary>
/// Request body for <c>PUT /api/operations/driver/trips/{tripId}</c>. The driver is the session's and
/// the trip is added on today in Italy, so the body names neither. The customer and the leading vehicle
/// are required; the trailer and the two locations are optional.
/// </summary>
/// <param name="CustomerId">The customer the trip is done for.</param>
/// <param name="VehicleId">The tractor or standalone vehicle.</param>
/// <param name="TrailerId">The trailer; <c>null</c> when none.</param>
/// <param name="PickupLocationId">The pickup location; <c>null</c> when not named.</param>
/// <param name="DeliveryLocationId">The delivery location; <c>null</c> when not named.</param>
/// <param name="Start"><c>true</c> to start the trip now, in the same act that adds it.</param>
[ValidatableType]
public sealed record AddTripRequest(
    [Required(ErrorMessageResourceType = typeof(OperationsMessages), ErrorMessageResourceName = nameof(OperationsMessages.Validation_Required))]
    Guid? CustomerId,
    [Required(ErrorMessageResourceType = typeof(OperationsMessages), ErrorMessageResourceName = nameof(OperationsMessages.Validation_Required))]
    Guid? VehicleId,
    Guid? TrailerId,
    Guid? PickupLocationId,
    Guid? DeliveryLocationId,
    bool Start);
