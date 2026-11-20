using Priamus.Trips.Registry.Domain.Vehicles;

namespace Priamus.Trips.Registry.Domain.Contracts.ValueObjects;

/// <summary>
/// The rate one vehicle has of its own in a flat-rate <see cref="Contract"/>. It applies for the
/// whole validity of the contract and has none of its own.
/// </summary>
/// <param name="VehicleId">The vehicle the rate is for.</param>
/// <param name="Rate">The rate, with the same rules as the contract's own.</param>
public sealed record VehicleRate(VehicleId VehicleId, Rate Rate);
