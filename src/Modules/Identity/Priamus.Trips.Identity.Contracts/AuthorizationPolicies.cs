namespace Priamus.Trips.Identity.Contracts;

/// <summary>
/// Names of the authorization policies an endpoint may require. Every endpoint of every module
/// declares one of these constants; the policies themselves are registered by the Identity module.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Any authenticated user, whatever the role.</summary>
    public const string Authenticated = "Authenticated";

    /// <summary>Users with the Admin role.</summary>
    public const string Admin = "Admin";

    /// <summary>Users with the BackOffice or the Admin role.</summary>
    public const string BackOfficeOrAdmin = "BackOfficeOrAdmin";
}
