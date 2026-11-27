using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Operations.Resources;

namespace Priamus.Trips.Operations.Features.EndTrip;

/// <summary>
/// Request body for <c>POST /api/operations/driver/trips/{tripId}/end</c>. The time absent ends the trip
/// now; the measure the contract asks for is given, the other is not.
/// </summary>
/// <param name="Time">The time the trip ended; <c>null</c> to end it now.</param>
/// <param name="Kilograms">The kilograms carried, for a trip measured by weight.</param>
/// <param name="Kilometres">The kilometres driven, for a trip measured by distance.</param>
[ValidatableType]
public sealed record EndTripRequest(
    TimeOnly? Time,
    [Range(1, 99_999, ErrorMessageResourceType = typeof(OperationsMessages), ErrorMessageResourceName = nameof(OperationsMessages.Validation_Range))]
    int? Kilograms,
    [Range(1, 9_999, ErrorMessageResourceType = typeof(OperationsMessages), ErrorMessageResourceName = nameof(OperationsMessages.Validation_Range))]
    int? Kilometres);
