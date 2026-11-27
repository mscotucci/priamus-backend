using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Operations.Features.CheckTripAnswers;

/// <summary>
/// Handles <c>GET /api/operations/driver/answers-check</c>: which answers of a trip in the making still
/// hold against the Registry and the contract in force today.
/// </summary>
internal sealed class CheckTripAnswersHandler
{
    private readonly ITripRegistry _registry;
    private readonly IClock _clock;

    public CheckTripAnswersHandler(ITripRegistry registry, IClock clock)
    {
        _registry = registry;
        _clock = clock;
    }

    /// <summary>
    /// Asks the Registry to judge the answers against today in Italy.
    /// </summary>
    public async Task<Result<CheckTripAnswersResponse>> HandleAsync(
        CheckTripAnswersRequest request,
        CancellationToken cancellationToken)
    {
        var check = await _registry.CheckAnswersAsync(
            new TripAnswers(
                request.VehicleId,
                request.TrailerId,
                request.CustomerId,
                _clock.TodayInItaly(),
                request.PickupLocationId,
                request.DeliveryLocationId),
            cancellationToken);

        return new CheckTripAnswersResponse(check.Vehicle, check.Trailer, check.Customer, check.Pickup, check.Delivery);
    }
}
