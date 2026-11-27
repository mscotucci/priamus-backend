namespace Priamus.Trips.Registry.Contracts;

/// <summary>What a vehicle is, for the role it can take in a trip.</summary>
public enum TripVehicleKind
{
    /// <summary>A tractor: leads a trip, with a trailer when it requires one.</summary>
    Tractor,

    /// <summary>A vehicle that works alone: leads a trip, never with a trailer.</summary>
    StandaloneVehicle,

    /// <summary>A trailer: follows a tractor, never leads.</summary>
    Trailer,
}
