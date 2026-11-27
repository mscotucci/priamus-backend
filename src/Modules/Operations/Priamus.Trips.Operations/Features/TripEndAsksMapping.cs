using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Registry.Contracts;
using RegistryContracts = Priamus.Trips.Registry.Contracts;

namespace Priamus.Trips.Operations.Features;

/// <summary>Tells what the end of a trip in progress asks, by the contract in force on its day.</summary>
internal static class TripEndAsksMapping
{
    /// <summary>
    /// What the end of <paramref name="trip"/> asks: the kilograms or the kilometres when the contract in
    /// force for its customer on its day measures them, nothing otherwise, also when no contract covers the
    /// day any more. The customer's status is not asked, as the pricing at the end does not ask it.
    /// </summary>
    /// <param name="registry">The registry that knows the contract.</param>
    /// <param name="trip">A trip in progress.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<TripEndAsks> FindEndAsksAsync(
        this ITripRegistry registry, Trip trip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(trip);

        var measure = await registry.FindMeasureAsync(trip.CustomerId, trip.Day!.Value, cancellationToken);
        return measure switch
        {
            RegistryContracts.TripMeasureKind.Weight => TripEndAsks.Kilograms,
            RegistryContracts.TripMeasureKind.Distance => TripEndAsks.Kilometres,
            _ => TripEndAsks.Nothing,
        };
    }
}
