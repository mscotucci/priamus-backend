namespace Priamus.Trips.SharedKernel.Security;

/// <summary>
/// Abstraction over the currently-authenticated user. Provided to handlers and
/// application services via DI; the implementation in the Identity module reads
/// from the ASP.NET Core <see cref="System.Security.Claims.ClaimsPrincipal"/>.
/// </summary>
public interface ICurrentUser
{
    /// <summary>True when the request is authenticated.</summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// The authenticated user's identifier. Throws when accessed on an
    /// unauthenticated request; callers must check <see cref="IsAuthenticated"/>
    /// first or rely on endpoint authorization to ensure authentication.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessed on an unauthenticated request.</exception>
    UserId UserId { get; }

    /// <summary>The set of roles assigned to the current user. Empty for unauthenticated requests.</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>
    /// The identifier of the driver the session names (the <c>driver_id</c> claim), or <c>null</c> when
    /// the session names none. The claim is trusted as issued.
    /// </summary>
    Guid? DriverId { get; }
}
