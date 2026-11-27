using System.Globalization;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Operations.Domain.Trips;

/// <summary>Errors raised by the <see cref="Trip"/> aggregate and by the rules of a driver's own trips.</summary>
/// <remarks>Codes follow the <c>Operations.Trip.{ErrorName}</c> PascalCase convention.</remarks>
public static class TripErrors
{
    /// <summary>The time typed, a start or an end, is later than the present moment in Italy.</summary>
    public static readonly Error InTheFuture = Error.Domain(
        "Operations.Trip.InTheFuture",
        "The time cannot be in the future.");

    /// <summary>
    /// The trip overlaps another trip of the driver. The start and end of the other trip are the message
    /// arguments.
    /// </summary>
    public static readonly Error Overlaps = Error.Domain(
        "Operations.Trip.Overlaps",
        "The trip overlaps another trip of the driver.");

    /// <summary>The message arguments of <see cref="Overlaps"/>: the start and end of the other trip, as HH:mm.</summary>
    /// <param name="otherStart">The start of the other trip.</param>
    /// <param name="otherEnd">The end of the other trip.</param>
    public static object[] OverlapArguments(DateTime otherStart, DateTime otherEnd) =>
    [
        TimeOnly.FromDateTime(otherStart).ToString("HH:mm", CultureInfo.InvariantCulture),
        TimeOnly.FromDateTime(otherEnd).ToString("HH:mm", CultureInfo.InvariantCulture),
    ];

    /// <summary>The driver has another trip in progress.</summary>
    public static readonly Error AnotherInProgress = Error.Domain(
        "Operations.Trip.AnotherInProgress",
        "The driver has another trip in progress.");

    /// <summary>Kilograms are given without the trip being measured by weight, or are missing with it.</summary>
    public static readonly Error KilogramsMismatch = Error.Validation(
        "Operations.Trip.KilogramsMismatch",
        "The kilograms are given only for a trip measured by weight.");

    /// <summary>Kilometres are given without the trip being measured by distance, or are missing with it.</summary>
    public static readonly Error KilometresMismatch = Error.Validation(
        "Operations.Trip.KilometresMismatch",
        "The kilometres are given only for a trip measured by distance.");

    /// <summary>The trip is started, but only a planned trip can be started.</summary>
    public static readonly Error NotPlanned = Error.Conflict(
        "Operations.Trip.NotPlanned",
        "Only a planned trip can be started.");

    /// <summary>The trip is not in progress, but only a trip in progress can be ended.</summary>
    public static readonly Error NotInProgress = Error.Conflict(
        "Operations.Trip.NotInProgress",
        "Only a trip in progress can be ended.");

    /// <summary>The trip would last less than 1 or more than 1439 minutes.</summary>
    public static readonly Error DurationOutOfRange = Error.Conflict(
        "Operations.Trip.DurationOutOfRange",
        "A trip lasts from 1 to 1439 minutes.");

    /// <summary>The trip is ended, and an ended trip cannot be deleted.</summary>
    public static readonly Error EndedTripCannotBeDeleted = Error.Conflict(
        "Operations.Trip.EndedTripCannotBeDeleted",
        "An ended trip cannot be deleted.");

    /// <summary>A trip with the identifier was deleted: its identifier is not used again.</summary>
    public static readonly Error Deleted = Error.Conflict(
        "Operations.Trip.Deleted",
        "The trip was deleted.");

    /// <summary>The driver has no such trip: it is unknown, deleted or expired.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "Operations.Trip.NotFound",
        "The trip was not found.");

    /// <summary>A trip with the identifier already exists and belongs to another driver.</summary>
    public static readonly Error OwnedByAnotherDriver = Error.Forbidden(
        "Operations.Trip.OwnedByAnotherDriver",
        "The trip belongs to another driver.");

    /// <summary>The customer has no contract in force on the day asked.</summary>
    public static readonly Error NoContractThatDay = Error.NotFound(
        "Operations.Trip.NoContractThatDay",
        "The customer has no contract in force on that day.");

    /// <summary>The session names a driver the registry does not know.</summary>
    public static readonly Error DriverNotFound = Error.NotFound(
        "Operations.Trip.DriverNotFound",
        "The driver was not found.");
}
