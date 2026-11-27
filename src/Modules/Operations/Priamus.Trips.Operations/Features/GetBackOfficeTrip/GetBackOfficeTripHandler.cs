using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Operations.Application.Services;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.Features.ListBackOfficeTrips;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Operations.Features.GetBackOfficeTrip;

/// <summary>
/// Handles <c>GET /api/operations/backoffice/trips/{tripId}</c>: one trip in any state with its names,
/// timeline, required documents and, once ended, the calculation it was saved with.
/// </summary>
internal sealed class GetBackOfficeTripHandler
{
    private readonly OperationsDbContext _db;
    private readonly ITripRegistry _registry;
    private readonly IClock _clock;

    public GetBackOfficeTripHandler(OperationsDbContext db, ITripRegistry registry, IClock clock)
    {
        _db = db;
        _registry = registry;
        _clock = clock;
    }

    /// <summary>
    /// A trip that is unknown, deleted or planned and expired is <see cref="TripErrors.NotFound"/>.
    /// Otherwise the trip as saved: the calculation is read from the trip, never recomputed from the contract.
    /// </summary>
    public async Task<Result<GetBackOfficeTripResponse>> HandleAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var id = new TripId(tripId);
        var trip = await _db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        var today = _clock.TodayInItaly();
        if (trip is null || trip.IsExpiredOn(today))
        {
            return TripErrors.NotFound;
        }

        var pricing = trip.Pricing;
        var names = await _registry.DescribeAsync(
            [.. new[] { trip.LeadingVehicleId, trip.TrailerId, pricing?.RateVehicleId }.OfType<Guid>().Distinct()],
            [trip.CustomerId],
            [.. new[] { trip.PickupLocationId, trip.DeliveryLocationId, pricing?.RatePickupLocationId, pricing?.RateDeliveryLocationId }
                .OfType<Guid>().Distinct()],
            cancellationToken);
        var parts = BackOfficeTripParts.Of(
            trip, names, await _registry.DescribeDriversAsync([trip.DriverId], cancellationToken));

        var isPlanned = trip.State == TripState.Planned;
        DateOnly? endedOn = trip.End is { } end
            ? DateOnly.FromDateTime(TripSchedule.Of(trip.Day!.Value, trip.Start!.Value, end).EndsAt!.Value)
            : null;

        return new GetBackOfficeTripResponse(
            trip.Id.Value,
            trip.State,
            trip.Number,
            trip.Day,
            trip.Start,
            trip.End,
            endedOn,
            trip.AddedAt,
            isPlanned ? trip.DaysToExpiry(today) : null,
            isPlanned ? trip.AddedOn.AddDays(Trip.ExpiryDays) : null,
            parts.Customer,
            parts.Pickup,
            parts.Delivery,
            parts.Driver,
            parts.Vehicle,
            parts.Trailer,
            trip.MeasureKind,
            trip.RequiredDocuments,
            pricing is null ? null : ToCalculation(pricing, names));
    }

    private static BackOfficeTripCalculationDto ToCalculation(Domain.Trips.TripPricing p, TripNames names) => new(
        p.RateSource,
        p.RateVehicleId is { } vehicleId ? names.VehicleOf(vehicleId) : null,
        p.RatePickupLocationId is { } pickupId ? names.LocationOf(pickupId) : null,
        p.RateDeliveryLocationId is { } deliveryId ? names.LocationOf(deliveryId) : null,
        p.DayPrice,
        p.NightPrice,
        p.NightCallFee,
        p.MinimumKind,
        p.MinimumAmount,
        p.MinimumThreshold,
        p.MinimumOveragePrice,
        p.NightBandStart,
        p.NightBandEnd,
        p.NightSurcharge,
        p.IsNight,
        p.Quantity,
        p.UnitPrice,
        p.BaseAmount,
        p.AmountAfterMinimum,
        p.MinimumApplied,
        p.AppliedNightCallFee,
        p.Total);
}
