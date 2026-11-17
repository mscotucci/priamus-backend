namespace Priamus.Trips.Registry.Domain.Vehicles;

/// <summary>
/// Whether a <see cref="Vehicle"/> can be chosen for new trips (PRD §6.6, RF-29).
/// </summary>
public enum VehicleStatus
{
    /// <summary>Attivo: can be chosen for new trips.</summary>
    Active,

    /// <summary>Dismesso: stays in the registry for the trips that name it.</summary>
    Decommissioned,
}
