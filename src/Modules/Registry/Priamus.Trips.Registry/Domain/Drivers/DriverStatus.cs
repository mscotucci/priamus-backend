namespace Priamus.Trips.Registry.Domain.Drivers;

/// <summary>
/// Whether a <see cref="Driver"/> can be chosen for new trips and can use the driver area
/// (PRD §6.5, RF-24, RF-25).
/// </summary>
public enum DriverStatus
{
    /// <summary>Attivo: can be chosen for new trips.</summary>
    Active,

    /// <summary>Disattivato: stays in the registry for the trips that name it.</summary>
    Deactivated,
}
