using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.Resources;
using Priamus.Trips.SharedKernel.Paging;

namespace Priamus.Trips.Operations.Features.ListBackOfficeTrips;

/// <summary>
/// Query-string parameters for <c>GET /api/operations/backoffice/trips</c>. The period and the mode belong
/// to the ended view only.
/// </summary>
/// <remarks>
/// Mutable class (not record) required by ASP.NET Core query-string model binding.
/// </remarks>
[ValidatableType]
public sealed class ListBackOfficeTripsRequest : PagedRequest, IValidatableObject
{
    /// <summary>The view: the state of the trips listed; <c>Ended</c> when absent.</summary>
    public TripState? View { get; set; }

    /// <summary>First trip day of the period, included; given together with <see cref="To"/>.</summary>
    public DateOnly? From { get; set; }

    /// <summary>Last trip day of the period, included; given together with <see cref="From"/>.</summary>
    public DateOnly? To { get; set; }

    /// <summary>Only the trips of this customer.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Only the trips of this driver.</summary>
    public Guid? DriverId { get; set; }

    /// <summary>Only the trips done with this vehicle as the leading vehicle or as the trailer.</summary>
    public Guid? VehicleId { get; set; }

    /// <summary>Only the trips picking up from or delivering to this location.</summary>
    public Guid? LocationId { get; set; }

    /// <summary>Only the trips measured this way.</summary>
    public TripMeasureKind? Mode { get; set; }

    /// <summary>
    /// Text matched against the trip number (digits only) and the names of the customer, driver, vehicles
    /// and locations of the trip. When null or whitespace, no search is applied.
    /// </summary>
    [MaxLength(100, ErrorMessageResourceType = typeof(OperationsMessages), ErrorMessageResourceName = nameof(OperationsMessages.Validation_MaxLength))]
    public string? Search { get; set; }

    /// <summary>The view, <c>Ended</c> when none is given.</summary>
    public TripState EffectiveView => View ?? TripState.Ended;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // The binder accepts any integer for an enum: a value with no name is not a view nor a mode.
        if (View is { } view && !Enum.IsDefined(view))
        {
            yield return new ValidationResult(OperationsMessages.Validation_NotAllowed, [nameof(View)]);
        }

        if (Mode is { } mode && !Enum.IsDefined(mode))
        {
            yield return new ValidationResult(OperationsMessages.Validation_NotAllowed, [nameof(Mode)]);
        }

        if (From is null != To is null)
        {
            yield return new ValidationResult(
                OperationsMessages.Validation_PeriodTogether,
                [From is null ? nameof(From) : nameof(To)]);
        }
        else if (From > To)
        {
            yield return new ValidationResult(OperationsMessages.Validation_PeriodOrder, [nameof(From), nameof(To)]);
        }

        if (EffectiveView != TripState.Ended)
        {
            if (From is not null || To is not null)
            {
                yield return new ValidationResult(OperationsMessages.Validation_EndedOnly, [nameof(From), nameof(To)]);
            }

            if (Mode is not null)
            {
                yield return new ValidationResult(OperationsMessages.Validation_EndedOnly, [nameof(Mode)]);
            }
        }
    }
}
