using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.Vehicles;

namespace Priamus.Trips.Registry.Domain.Contracts.ValueObjects;

/// <summary>
/// The rate one vehicle has of its own in a <see cref="Contract"/>. In a flat-rate contract it has no
/// locations and applies on the whole contract; in a contract priced per location or pair it names the
/// location or pair it applies on, as a <see cref="LocationRate"/> does, one the contract has a rate
/// for. A vehicle that applies on several locations has one vehicle rate for each, with equal terms.
/// It applies for the whole validity of the contract and has none of its own.
/// </summary>
/// <param name="VehicleId">The vehicle the rate is for.</param>
/// <param name="PickupLocationId">The pickup location; <c>null</c> when the billing level does not use it.</param>
/// <param name="DeliveryLocationId">The delivery location; <c>null</c> when the billing level does not use it.</param>
/// <param name="Rate">The rate, with the same rules as the contract's own.</param>
public sealed record VehicleRate(VehicleId VehicleId, LocationId? PickupLocationId, LocationId? DeliveryLocationId, Rate Rate);
