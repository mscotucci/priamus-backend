using Priamus.Trips.Registry.Domain.Vehicles;

namespace Priamus.Trips.Registry.Domain.Contracts.ValueObjects;

/// <summary>
/// The stored shape of a <see cref="VehicleRate"/>: one row of <c>registry.contract_vehicle_rates</c>,
/// owned by its <see cref="Contract"/>.
/// </summary>
internal sealed class StoredVehicleRate : StoredRate
{
    private StoredVehicleRate()
    {
        // EF only.
    }

    internal StoredVehicleRate(VehicleRate vehicleRate)
        : base(vehicleRate.Rate)
    {
        VehicleId = vehicleRate.VehicleId;
    }

    internal VehicleId VehicleId { get; private set; }

    /// <summary>Rebuilds the vehicle rate from the stored columns.</summary>
    internal VehicleRate ToVehicleRate() => new(VehicleId, ToRate());
}
