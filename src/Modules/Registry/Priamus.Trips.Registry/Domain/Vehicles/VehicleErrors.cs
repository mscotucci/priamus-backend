using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Domain.Vehicles;

/// <summary>
/// Errors raised by the <see cref="Vehicle"/> aggregate, its value objects and the
/// application-layer lookups on vehicles.
/// </summary>
/// <remarks>
/// Codes follow the <c>Registry.Vehicle.{ErrorName}</c> PascalCase convention.
/// </remarks>
public static class VehicleErrors
{
    // ─── Value-object and aggregate validation ────────────────────────────

    /// <summary>
    /// The license plate, once normalised, is not 2 to 10 ASCII letters and digits (PRD §6.6, RF-26).
    /// </summary>
    public static readonly Error LicensePlateInvalid = Error.Validation(
        "Registry.Vehicle.LicensePlateInvalid",
        "License plate must be 2 to 10 letters and digits, spaces and hyphens aside.");

    /// <summary>
    /// The description is empty or longer than <see cref="Vehicle.DescriptionMaxLength"/> characters
    /// once trimmed (PRD §6.6, RF-26).
    /// </summary>
    public static readonly Error DescriptionInvalid = Error.Validation(
        "Registry.Vehicle.DescriptionInvalid",
        "Description is required and must be at most 100 characters.");

    /// <summary>
    /// Requires-trailer was set on a vehicle that is not a <see cref="VehicleType.Tractor"/>
    /// (PRD §5.2, RF-27).
    /// </summary>
    public static readonly Error RequiresTrailerNotAllowed = Error.Validation(
        "Registry.Vehicle.RequiresTrailerNotAllowed",
        "Only a tractor can require a trailer.");

    // ─── Application-layer lookups ────────────────────────────────────────

    /// <summary>The requested vehicle was not found.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "Registry.Vehicle.NotFound",
        "The requested vehicle was not found.");

    /// <summary>
    /// The license plate belongs to another active vehicle. Uniqueness is enforced by a unique
    /// index and checked upfront by the application layer (PRD §6.6, RF-28).
    /// </summary>
    public static readonly Error LicensePlateTaken = Error.Conflict(
        "Registry.Vehicle.LicensePlateTaken",
        "The license plate is already registered on another vehicle.");

    /// <summary>
    /// The license plate belongs to a decommissioned vehicle, which can be reactivated instead
    /// (PRD §6.6, RF-28, RF-29).
    /// </summary>
    public static readonly Error LicensePlateTakenByDecommissioned = Error.Conflict(
        "Registry.Vehicle.LicensePlateTakenByDecommissioned",
        "The license plate is already registered on a decommissioned vehicle.");

    /// <summary>
    /// The version sent with a write does not match the stored one: someone else changed
    /// the vehicle in the meantime.
    /// </summary>
    public static readonly Error ConcurrentUpdate = Error.Conflict(
        "Registry.Vehicle.ConcurrentUpdate",
        "The vehicle was modified by another user in the meantime.");
}
