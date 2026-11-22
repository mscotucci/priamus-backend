using Priamus.Trips.Registry.Domain.Locations;

namespace Priamus.Trips.Registry.Domain.Contracts.ValueObjects;

/// <summary>
/// The rate a <see cref="Contract"/> priced per location or per pair of locations holds for one of
/// them. <c>PerPickupLocation</c> fills the pickup only, <c>PerDeliveryLocation</c> the delivery
/// only, <c>PerLocationPair</c> both. It applies for the whole validity of the contract and has none
/// of its own.
/// </summary>
/// <param name="PickupLocationId">The pickup location; <c>null</c> when the billing level does not use it.</param>
/// <param name="DeliveryLocationId">The delivery location; <c>null</c> when the billing level does not use it.</param>
/// <param name="Rate">The rate, with the same rules as the contract's own.</param>
public sealed record LocationRate(LocationId? PickupLocationId, LocationId? DeliveryLocationId, Rate Rate);
