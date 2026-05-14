using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Registry.Contracts;

/// <summary>
/// Lookup interface that resolves the Registry <c>Driver</c> identifier for a given
/// Identity <c>UserId</c>. Called at login time AND at refresh time per ADR 0010.
/// </summary>
/// <remarks>
/// Implementations MUST be idempotent and side-effect-free with respect to repeated
/// lookups for the same userId/> (ADR 0010 §Negative).
/// Returns <c>null</c> when the user has no linked Driver record in Registry — this
/// is a valid state during onboarding, not an error.
/// </remarks>
public interface IDriverLookup
{
    /// <summary>
    /// Returns the <c>DriverId</c> (as <see cref="Guid"/>) linked to <paramref name="userId"/>,
    /// or <c>null</c> when no link exists.
    /// </summary>
    /// <param name="userId">Identity of the user whose driver link is being resolved.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Guid?> GetDriverIdByUserIdAsync(UserId userId, CancellationToken cancellationToken);
}
