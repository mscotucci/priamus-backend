using Microsoft.EntityFrameworkCore;
using Npgsql;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Application.Services;

/// <summary>
/// Tells whether a name and municipality are already registered on another location, and which
/// error says so (PRD §5.1, RF-23). Shared by the create and edit slices, which both check upfront
/// and again after a unique-index violation.
/// </summary>
internal static class LocationConflicts
{
    /// <summary>Name of the unique index on the name and municipality keys.</summary>
    public const string UniqueIndexName = "uq_locations_name_municipality_key";

    /// <summary>
    /// Looks for another location with the same name and municipality, compared through
    /// <see cref="Location.KeyOf"/>. When found, stores the holder's name and municipality, as
    /// stored, in <paramref name="arguments"/> for the Italian message and returns the error for an
    /// active or a deactivated holder; otherwise returns <c>null</c>.
    /// </summary>
    /// <param name="db">The Registry context.</param>
    /// <param name="name">The requested name, as typed.</param>
    /// <param name="municipality">The requested municipality, as typed.</param>
    /// <param name="excluding">The location being edited, which may keep its own name; <c>null</c> on create.</param>
    /// <param name="arguments">Receives the holder's name and municipality for the message.</param>
    /// <param name="cancellationToken">Propagated to the query.</param>
    public static async Task<Error?> FindAsync(
        RegistryDbContext db,
        string name,
        string municipality,
        LocationId? excluding,
        ErrorMessageArguments arguments,
        CancellationToken cancellationToken)
    {
        var nameKey = Location.KeyOf(name);
        var municipalityKey = Location.KeyOf(municipality);

        var holders = db.Locations.AsNoTracking()
            .Where(l => l.NameKey == nameKey && l.MunicipalityKey == municipalityKey);
        if (excluding is { } own)
        {
            holders = holders.Where(l => l.Id != own);
        }

        var holder = await holders
            .Select(l => new { l.Status, l.Name, l.Municipality })
            .FirstOrDefaultAsync(cancellationToken);

        if (holder is null)
        {
            return null;
        }

        arguments.Set(holder.Name, holder.Municipality);
        return holder.Status is LocationStatus.Deactivated
            ? LocationErrors.TakenByDeactivated
            : LocationErrors.Taken;
    }

    /// <summary>Tells whether <paramref name="exception"/> is a violation of the name and municipality unique index.</summary>
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pgEx
        && pgEx.ConstraintName == UniqueIndexName;

    /// <summary>
    /// The error to raise after losing a race on the unique index: looks the competitor up again to
    /// tell an active holder from a deactivated one, and falls back to
    /// <see cref="LocationErrors.Taken"/> naming the requested name and municipality, trimmed, when
    /// it can no longer be found (it was renamed in between).
    /// </summary>
    public static async Task<Error> ErrorAfterLostRaceAsync(
        RegistryDbContext db,
        string name,
        string municipality,
        LocationId? excluding,
        ErrorMessageArguments arguments,
        CancellationToken cancellationToken)
    {
        if (await FindAsync(db, name, municipality, excluding, arguments, cancellationToken) is { } raced)
        {
            return raced;
        }

        arguments.Set(name.Trim(), municipality.Trim());
        return LocationErrors.Taken;
    }
}
