using Microsoft.EntityFrameworkCore;
using Npgsql;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Domain.Vehicles.ValueObjects;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Application.Services;

/// <summary>
/// Tells whether a license plate is already registered on another vehicle, and which error says so
/// (PRD §6.6, RF-28, RF-29). Shared by the create and edit slices, which both check upfront and again
/// after a unique-index violation.
/// </summary>
internal static class LicensePlateConflicts
{
    /// <summary>Name of the unique index on the normalised plate.</summary>
    public const string UniqueIndexName = "uq_vehicles_license_plate";

    /// <summary>
    /// Looks for another vehicle holding <paramref name="plate"/>. When found, stores the plate in
    /// <paramref name="arguments"/> for the Italian message and returns the error for an active or a
    /// decommissioned holder; otherwise returns <c>null</c>.
    /// </summary>
    /// <param name="db">The Registry context.</param>
    /// <param name="plate">The plate in normal form.</param>
    /// <param name="excluding">The vehicle being edited, which may keep its own plate; <c>null</c> on create.</param>
    /// <param name="arguments">Receives the plate for the message.</param>
    /// <param name="cancellationToken">Propagated to the query.</param>
    public static async Task<Error?> FindAsync(
        RegistryDbContext db,
        LicensePlate plate,
        VehicleId? excluding,
        ErrorMessageArguments arguments,
        CancellationToken cancellationToken)
    {
        var holders = db.Vehicles.AsNoTracking().Where(v => v.LicensePlate == plate);
        if (excluding is { } own)
        {
            holders = holders.Where(v => v.Id != own);
        }

        var status = await holders
            .Select(v => (VehicleStatus?)v.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (status is null)
        {
            return null;
        }

        arguments.Set(plate.Value);
        return status is VehicleStatus.Decommissioned
            ? VehicleErrors.LicensePlateTakenByDecommissioned
            : VehicleErrors.LicensePlateTaken;
    }

    /// <summary>Tells whether <paramref name="exception"/> is a violation of the plate's unique index.</summary>
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pgEx
        && pgEx.ConstraintName == UniqueIndexName;

    /// <summary>
    /// The error to raise after losing a race on the unique index: looks the competitor up again to
    /// tell an active holder from a decommissioned one, and falls back to
    /// <see cref="VehicleErrors.LicensePlateTaken"/>, still naming the plate, when it can no longer be found.
    /// </summary>
    public static async Task<Error> ErrorAfterLostRaceAsync(
        RegistryDbContext db,
        LicensePlate plate,
        VehicleId? excluding,
        ErrorMessageArguments arguments,
        CancellationToken cancellationToken)
    {
        if (await FindAsync(db, plate, excluding, arguments, cancellationToken) is { } raced)
        {
            return raced;
        }

        arguments.Set(plate.Value);
        return VehicleErrors.LicensePlateTaken;
    }
}
