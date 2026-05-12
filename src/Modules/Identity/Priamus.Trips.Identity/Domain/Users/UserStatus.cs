namespace Priamus.Trips.Identity.Domain.Users;

/// <summary>
/// Lifecycle state of a <see cref="User"/>. A disabled user cannot log in and
/// cannot change password; the back office enables/disables users via dedicated
/// commands (per RNF-02 revocation requirement).
/// </summary>
public enum UserStatus
{
    /// <summary>The user can authenticate and operate.</summary>
    Active = 1,

    /// <summary>The user is locked out. Authentication and most state changes are rejected.</summary>
    Disabled = 2,
}
