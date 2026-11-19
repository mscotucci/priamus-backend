using Microsoft.EntityFrameworkCore;
using Npgsql;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Registry.Application.Services;

/// <summary>
/// Tells whether a user account is already linked to another driver, and which error says so
/// (PRD §6.5, RF-24). Shared by the create and edit slices, which both check upfront and again
/// after a unique-index violation.
/// </summary>
internal static class DriverAccountConflicts
{
    /// <summary>Name of the unique index on the linked user account.</summary>
    public const string UniqueIndexName = "uq_drivers_user_id";

    /// <summary>
    /// Looks for another driver linked to <paramref name="userId"/>. When found, stores the user
    /// name and the holder's "first name last name" in <paramref name="arguments"/> for the Italian
    /// message and returns the error for an active or a deactivated holder; otherwise returns
    /// <c>null</c>.
    /// </summary>
    /// <param name="db">The Registry context.</param>
    /// <param name="userId">The user account being linked.</param>
    /// <param name="username">The account's user name, for the message.</param>
    /// <param name="excluding">The driver being edited; <c>null</c> on create.</param>
    /// <param name="arguments">Receives the user name and the holder's names for the message.</param>
    /// <param name="cancellationToken">Propagated to the query.</param>
    public static async Task<Error?> FindAsync(
        RegistryDbContext db,
        UserId userId,
        string username,
        DriverId? excluding,
        ErrorMessageArguments arguments,
        CancellationToken cancellationToken)
    {
        var holders = db.Drivers.AsNoTracking().Where(d => d.UserId == userId);
        if (excluding is { } own)
        {
            holders = holders.Where(d => d.Id != own);
        }

        var holder = await holders
            .Select(d => new { d.Status, d.FirstName, d.LastName })
            .FirstOrDefaultAsync(cancellationToken);

        if (holder is null)
        {
            return null;
        }

        arguments.Set(username, $"{holder.FirstName} {holder.LastName}");
        return holder.Status is DriverStatus.Deactivated
            ? DriverErrors.AccountTakenByDeactivated
            : DriverErrors.AccountTaken;
    }

    /// <summary>Tells whether <paramref name="exception"/> is a violation of the linked account's unique index.</summary>
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pgEx
        && pgEx.ConstraintName == UniqueIndexName;

    /// <summary>
    /// The error to raise after losing a race on the unique index: looks the competitor up again to
    /// tell an active holder from a deactivated one, and falls back to
    /// <see cref="DriverErrors.AccountUnavailable"/> when it can no longer be found (its link was
    /// removed in between).
    /// </summary>
    public static async Task<Error> ErrorAfterLostRaceAsync(
        RegistryDbContext db,
        UserId userId,
        string username,
        DriverId? excluding,
        ErrorMessageArguments arguments,
        CancellationToken cancellationToken) =>
        await FindAsync(db, userId, username, excluding, arguments, cancellationToken)
        ?? DriverErrors.AccountUnavailable;
}
