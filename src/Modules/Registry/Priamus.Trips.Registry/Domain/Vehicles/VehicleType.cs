namespace Priamus.Trips.Registry.Domain.Vehicles;

/// <summary>
/// What role a <see cref="Vehicle"/> can play in a trip (PRD §5.2, RF-26).
/// </summary>
public enum VehicleType
{
    /// <summary>Motrice: leads a trip on its own or pulling a trailer.</summary>
    Tractor,

    /// <summary>Rimorchio: a towed vehicle, never leads a trip.</summary>
    Trailer,

    /// <summary>Mezzo autonomo: never takes a trailer, leads a trip alone.</summary>
    StandaloneVehicle,
}
