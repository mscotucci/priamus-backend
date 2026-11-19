namespace Priamus.Trips.Identity.Contracts;

/// <summary>
/// Read-only view of an Identity user account, as returned by <see cref="IUserLookup"/>
/// (ADR 0013). No Identity enum crosses the module boundary: role and status are reduced to
/// the two facts Registry needs.
/// </summary>
/// <param name="Id">Identifier of the user.</param>
/// <param name="Username">Normalised login name of the user.</param>
/// <param name="IsDriver">Whether the user has the role Driver.</param>
/// <param name="IsDisabled">Whether the user is disabled.</param>
public sealed record UserAccount(Guid Id, string Username, bool IsDriver, bool IsDisabled);
