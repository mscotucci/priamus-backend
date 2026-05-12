namespace Priamus.Trips.Identity.Domain.Users;

/// <summary>
/// Functional role assigned to a <see cref="User"/>. Drives both back-office
/// authorization policies and the strict driver/back-office data boundary
/// (drivers must never see pricing — see <c>docs/context.md</c> §1).
/// </summary>
public enum UserRole
{
    /// <summary>
    /// Drives trips via the Driver PWA. The link to the corresponding <c>Driver</c>
    /// aggregate in Registry is owned by Registry (<c>Driver.UserId</c>) and resolved
    /// at login time via <c>IDriverLookup</c> — see ADR 0008.
    /// </summary>
    Driver = 1,

    /// <summary>Secretariat user operating the back-office web app.</summary>
    BackOffice = 2,

    /// <summary>System administrator. Superset of <see cref="BackOffice"/>.</summary>
    Admin = 3,
}
