namespace Priamus.Trips.Identity.Contracts;

/// <summary>
/// Read-only lookup that lets other modules read user accounts owned by Identity without
/// referencing its implementation (ADR 0013). Implementations must be side-effect-free.
/// </summary>
public interface IUserLookup
{
    /// <summary>
    /// Returns the accounts of the given user ids, keyed by id. An id with no user is absent
    /// from the dictionary.
    /// </summary>
    /// <param name="userIds">Identifiers of the users to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, UserAccount>> GetAccountsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns every user with the role Driver, enabled or disabled.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<UserAccount>> ListDriverAccountsAsync(CancellationToken cancellationToken);
}
