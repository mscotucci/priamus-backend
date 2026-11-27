using Priamus.Trips.Operations.Application.Services;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Registry.Contracts;

namespace Priamus.Trips.Operations.Features;

/// <summary>Builds the <see cref="TripDto"/> of a trip, naming what it refers to.</summary>
internal static class TripDtoMapping
{
    /// <summary>
    /// Reads from the registry the names of the vehicles, customer and locations of <paramref name="trip"/>
    /// and maps it.
    /// </summary>
    /// <param name="trip">The trip to map.</param>
    /// <param name="registry">The registry that names what the trip refers to.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<TripDto> ToDtoAsync(
        this Trip trip, ITripRegistry registry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(registry);

        var names = await registry.DescribeAsync(
            [.. new[] { trip.LeadingVehicleId, trip.TrailerId }.OfType<Guid>()],
            [trip.CustomerId],
            [.. new[] { trip.PickupLocationId, trip.DeliveryLocationId }.OfType<Guid>()],
            cancellationToken);

        return trip.ToDto(names);
    }

    /// <summary>Maps <paramref name="trip"/> with the names already read from the registry.</summary>
    /// <param name="trip">The trip to map.</param>
    /// <param name="names">The names of what the trip refers to.</param>
    public static TripDto ToDto(this Trip trip, TripNames names)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(names);

        return new TripDto(
            trip.Id.Value,
            trip.State,
            names.CustomerOf(trip.CustomerId),
            trip.PickupLocationId is { } pickupId ? names.LocationOf(pickupId) : null,
            trip.DeliveryLocationId is { } deliveryId ? names.LocationOf(deliveryId) : null,
            names.VehicleOf(trip.LeadingVehicleId),
            trip.TrailerId is { } trailerId ? names.VehicleOf(trailerId) : null,
            trip.Day,
            trip.Start,
            trip.End,
            trip.Kilograms,
            trip.Kilometres,
            trip.Number);
    }
}
