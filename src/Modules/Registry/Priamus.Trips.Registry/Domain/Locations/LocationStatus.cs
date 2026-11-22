namespace Priamus.Trips.Registry.Domain.Locations;

/// <summary>
/// Whether a <see cref="Location"/> can be chosen for new rates and trips (PRD §5.1).
/// </summary>
public enum LocationStatus
{
    /// <summary>Attivo: can be chosen for new rates and trips.</summary>
    Active,

    /// <summary>Disattivato: stays in the registry for the rates and trips that name it.</summary>
    Deactivated,
}
