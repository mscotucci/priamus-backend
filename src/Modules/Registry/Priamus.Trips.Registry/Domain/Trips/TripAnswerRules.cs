using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.Vehicles;

namespace Priamus.Trips.Registry.Domain.Trips;

/// <summary>
/// The rules the answers of a trip must satisfy against the Registry (PRD §5.2, §5.4; RF-08ter, RF-08quater,
/// RF-27ter), pure functions shared by the check of the answers and by the pricing of a trip.
/// </summary>
internal static class TripAnswerRules
{
    /// <summary>Whether the vehicle may lead a trip: active, a tractor or a standalone vehicle.</summary>
    public static bool CanLead(Vehicle vehicle) =>
        vehicle.Status == VehicleStatus.Active && vehicle.Type != VehicleType.Trailer;

    /// <summary>
    /// Whether the trailer holds with the leading vehicle: active, a trailer, and not behind a standalone
    /// vehicle. A leading vehicle that is not known does not exclude it.
    /// </summary>
    public static bool TrailerHolds(Vehicle? leading, Vehicle trailer) =>
        trailer.Status == VehicleStatus.Active
        && trailer.Type == VehicleType.Trailer
        && leading?.Type != VehicleType.StandaloneVehicle;

    /// <summary>Whether the leading vehicle requires a trailer and none is named.</summary>
    public static bool TrailerMissing(Vehicle leading, bool trailerGiven) =>
        leading.RequiresTrailer && !trailerGiven;

    /// <summary>Whether the billing level names a location the trip does not give.</summary>
    public static bool LocationMissing(BillingLevel level, LocationId? pickup, LocationId? delivery)
    {
        var (namesPickup, namesDelivery) = level.NamedLocations();
        return (namesPickup && pickup is null) || (namesDelivery && delivery is null);
    }

    /// <summary>
    /// Whether the locations given hold for the contract. A location not given holds. The side the billing
    /// level names must be active and rated, the other side any active location; for a pair the two are judged
    /// together, so a pair that is not rated, or has an inactive location, fails both sides. A lone side of a
    /// pair holds when some rated pair has it. A flat-rate contract accepts any active location.
    /// </summary>
    /// <param name="level">The billing level of the contract.</param>
    /// <param name="rates">The location rates of the contract.</param>
    /// <param name="pickup">The pickup location given.</param>
    /// <param name="delivery">The delivery location given.</param>
    /// <param name="active">The given locations that are active.</param>
    public static (bool Pickup, bool Delivery) LocationsHold(
        BillingLevel level,
        IReadOnlyList<LocationRate> rates,
        LocationId? pickup,
        LocationId? delivery,
        IReadOnlySet<LocationId> active)
    {
        var pickupActive = pickup is not { } p || active.Contains(p);
        var deliveryActive = delivery is not { } d || active.Contains(d);

        switch (level)
        {
            case BillingLevel.PerPickupLocation:
                return (pickupActive && (pickup is null || rates.Any(r => r.PickupLocationId == pickup)), deliveryActive);
            case BillingLevel.PerDeliveryLocation:
                return (pickupActive, deliveryActive && (delivery is null || rates.Any(r => r.DeliveryLocationId == delivery)));
            case BillingLevel.PerLocationPair:
                var rated = (pickup, delivery) switch
                {
                    (not null, not null) => rates.Any(r => r.PickupLocationId == pickup && r.DeliveryLocationId == delivery),
                    (not null, null) => rates.Any(r => r.PickupLocationId == pickup),
                    (null, not null) => rates.Any(r => r.DeliveryLocationId == delivery),
                    _ => true,
                };
                var holds = rated && pickupActive && deliveryActive;
                return (pickup is null || holds, delivery is null || holds);
            default:
                return (pickupActive, deliveryActive);
        }
    }
}
