using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Registry.Application.Services;

/// <summary>
/// The rules for linking a user account to a driver (PRD §6.5, RF-24), shared by the create and
/// edit slices: the account must exist, be a driver account and be enabled, and must not be linked
/// to another driver.
/// </summary>
internal static class DriverAccountChecks
{
    /// <summary>
    /// Checks the account the request asks to link. Keeping the account the driver already has is
    /// always allowed, even when it is now disabled, and costs no Identity read. Returns the account
    /// read from Identity (<c>null</c> when no account is requested or the current one is kept) or
    /// the error that refuses the save.
    /// </summary>
    /// <param name="users">Read-only lookup of Identity accounts.</param>
    /// <param name="db">The Registry context.</param>
    /// <param name="requested">The account the request asks to link, or <c>null</c> for none.</param>
    /// <param name="current">The account the driver is linked to now; <c>null</c> on create.</param>
    /// <param name="excluding">The driver being edited; <c>null</c> on create.</param>
    /// <param name="arguments">Receives the message arguments of a conflict.</param>
    /// <param name="cancellationToken">Propagated to the queries.</param>
    public static async Task<(UserAccount? Account, Error? Error)> CheckAsync(
        IUserLookup users,
        RegistryDbContext db,
        Guid? requested,
        UserId? current,
        DriverId? excluding,
        ErrorMessageArguments arguments,
        CancellationToken cancellationToken)
    {
        if (requested is not { } userId)
        {
            return (null, null);
        }

        if (current is { } own && own.Value == userId)
        {
            return (null, null);
        }

        var accounts = await users.GetAccountsAsync([userId], cancellationToken);
        accounts.TryGetValue(userId, out var account);

        if (account is not { IsDriver: true, IsDisabled: false })
        {
            return (account, DriverErrors.AccountUnavailable);
        }

        var conflict = await DriverAccountConflicts.FindAsync(
            db, new UserId(userId), account.Username, excluding, arguments, cancellationToken);

        return (account, conflict);
    }
}
