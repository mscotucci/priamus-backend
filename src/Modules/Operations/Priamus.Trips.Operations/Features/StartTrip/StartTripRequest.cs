using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Operations.Resources;

namespace Priamus.Trips.Operations.Features.StartTrip;

/// <summary>
/// Request body for <c>POST /api/operations/driver/trips/{tripId}/start</c>. Both members absent start the
/// trip now; otherwise both are given, as the day and time the driver typed.
/// </summary>
/// <param name="Day">The day the trip started; <c>null</c> to start it now.</param>
/// <param name="Time">The time the trip started; <c>null</c> to start it now.</param>
[ValidatableType]
public sealed record StartTripRequest(DateOnly? Day, TimeOnly? Time) : IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Day is null != Time is null)
        {
            yield return new ValidationResult(
                OperationsMessages.Validation_DayAndTimeTogether,
                [Day is null ? nameof(Day) : nameof(Time)]);
        }
    }
}
