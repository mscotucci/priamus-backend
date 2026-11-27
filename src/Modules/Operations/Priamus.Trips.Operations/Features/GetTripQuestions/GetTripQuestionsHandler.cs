using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.Features.ListTripLocations;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Operations.Features.GetTripQuestions;

/// <summary>
/// Handles <c>GET /api/operations/driver/trip-questions</c>: what the contract in force for the customer
/// today asks, and the place the driver used in the latest trip for that customer when the
/// contract still offers it (PRD §5.4, §5.5; round 3, Q18).
/// </summary>
internal sealed class GetTripQuestionsHandler
{
    private readonly OperationsDbContext _db;
    private readonly ITripRegistry _registry;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public GetTripQuestionsHandler(
        OperationsDbContext db,
        ITripRegistry registry,
        IClock clock,
        ICurrentUser currentUser)
    {
        _db = db;
        _registry = registry;
        _clock = clock;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Asks the Registry what the contract asks; <see cref="TripErrors.NoContractThatDay"/> when the
    /// customer has no contract in force today.
    /// </summary>
    public async Task<Result<TripQuestionsDto>> HandleAsync(
        GetTripQuestionsRequest request,
        CancellationToken cancellationToken)
    {
        // The Driver policy requires the claim, so a handler reached without it is a wiring bug.
        var driverId = _currentUser.DriverId
            ?? throw new InvalidOperationException("The session names no driver.");

        // The request validation guarantees the customer.
        var customerId = request.CustomerId!.Value;
        var questions = await _registry.FindQuestionsAsync(customerId, _clock.TodayInItaly(), cancellationToken);
        if (questions is null)
        {
            return TripErrors.NoContractThatDay;
        }

        var places = questions.Places.Select(ToDto).ToList();

        // A contract that asks for no location offers none to propose.
        TripPlaceDto? lastPlace = null;
        if (questions.LocationsAsked != TripLocationsAsked.None)
        {
            var latest = await _db.Trips.AsNoTracking()
                .Where(t => t.DriverId == driverId && t.CustomerId == customerId)
                .OrderByDescending(t => t.AddedAt)
                .Select(t => new { t.PickupLocationId, t.DeliveryLocationId })
                .FirstOrDefaultAsync(cancellationToken);

            if (latest is not null)
            {
                var match = questions.Places.FirstOrDefault(p =>
                    Matches(questions.LocationsAsked, p, latest.PickupLocationId, latest.DeliveryLocationId));
                lastPlace = match is null ? null : ToDto(match);
            }
        }

        return new TripQuestionsDto(questions.LocationsAsked, places, lastPlace);
    }

    /// <summary>Whether the place holds the locations the level names that the trip used.</summary>
    private static bool Matches(TripLocationsAsked asked, TripPlace place, Guid? pickupId, Guid? deliveryId)
    {
        var pickupMatches = place.Pickup is { } pickup && pickup.Id == pickupId;
        var deliveryMatches = place.Delivery is { } delivery && delivery.Id == deliveryId;

        return asked switch
        {
            TripLocationsAsked.Pickup => pickupMatches,
            TripLocationsAsked.Delivery => deliveryMatches,
            TripLocationsAsked.Both => pickupMatches && deliveryMatches,
            _ => false,
        };
    }

    private static TripPlaceDto ToDto(TripPlace place) =>
        new(ToDto(place.Pickup), ToDto(place.Delivery));

    private static TripLocationDto? ToDto(TripLocation? location) =>
        location is null ? null : new TripLocationDto(location.Id, location.Name, location.Municipality);
}
