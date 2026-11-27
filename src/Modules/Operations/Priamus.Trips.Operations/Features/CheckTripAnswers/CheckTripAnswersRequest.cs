using Microsoft.Extensions.Validation;

namespace Priamus.Trips.Operations.Features.CheckTripAnswers;

/// <summary>
/// Query-string parameters for <c>GET /api/operations/driver/answers-check</c>.
/// </summary>
/// <remarks>
/// Mutable class (not record) required by ASP.NET Core query-string model binding.
/// Bound via <c>[AsParameters]</c> in the endpoint delegate signature.
/// </remarks>
[ValidatableType]
public sealed class CheckTripAnswersRequest
{
    /// <summary>The leading vehicle answered; <c>null</c> when none.</summary>
    public Guid? VehicleId { get; set; }

    /// <summary>The trailer answered; <c>null</c> when none.</summary>
    public Guid? TrailerId { get; set; }

    /// <summary>The customer answered; <c>null</c> when none.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>The pickup location answered; <c>null</c> when none.</summary>
    public Guid? PickupLocationId { get; set; }

    /// <summary>The delivery location answered; <c>null</c> when none.</summary>
    public Guid? DeliveryLocationId { get; set; }
}
