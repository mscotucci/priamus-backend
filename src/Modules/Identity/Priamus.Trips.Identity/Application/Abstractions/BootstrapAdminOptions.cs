namespace Priamus.Trips.Identity.Application.Abstractions;

/// <summary>
/// Credentials of the first administrator, bound from the "Identity:BootstrapAdmin" configuration
/// section. Absent or incomplete values are a valid state: they only mean that no administrator is
/// created at startup.
/// </summary>
internal sealed class BootstrapAdminOptions
{
    /// <summary>Login name of the first administrator.</summary>
    public string? Username { get; set; }

    /// <summary>Password of the first administrator. Never logged.</summary>
    public string? Password { get; set; }
}
