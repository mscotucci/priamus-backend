using Priamus.Trips.Registry.Domain.Locations;

namespace Priamus.Trips.Registry.Domain.Contracts.ValueObjects;

/// <summary>
/// The stored shape of a <see cref="LocationRate"/>: one row of <c>registry.contract_location_rates</c>,
/// owned by its <see cref="Contract"/>.
/// </summary>
internal sealed class StoredLocationRate : StoredRate
{
    private StoredLocationRate()
    {
        // EF only.
    }

    internal StoredLocationRate(LocationRate locationRate)
        : base(locationRate.Rate)
    {
        PickupLocationId = locationRate.PickupLocationId;
        DeliveryLocationId = locationRate.DeliveryLocationId;
    }

    internal LocationId? PickupLocationId { get; private set; }

    internal LocationId? DeliveryLocationId { get; private set; }

    /// <summary>Rebuilds the location rate from the stored columns.</summary>
    internal LocationRate ToLocationRate() => new(PickupLocationId, DeliveryLocationId, ToRate());
}
