using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Operations.Features.ListTripCustomers;

/// <summary>
/// Handles <c>GET /api/operations/driver/customers</c>: the customers with a contract in force today,
/// the ones of the driver's latest trips first (PRD §5.4, RF-08).
/// </summary>
internal sealed class ListTripCustomersHandler
{
    /// <summary>How many recent customers the question shows at most.</summary>
    private const int RecentCount = 5;

    private readonly OperationsDbContext _db;
    private readonly ITripRegistry _registry;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public ListTripCustomersHandler(
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
    /// Asks the Registry for the customers of today; without a search text, moves to <c>Recent</c> the
    /// ones of the driver's latest trips (by adding, distinct) that the Registry offers.
    /// </summary>
    public async Task<Result<ListTripCustomersResponse>> HandleAsync(
        ListTripCustomersRequest request,
        CancellationToken cancellationToken)
    {
        // The Driver policy requires the claim, so a handler reached without it is a wiring bug.
        var driverId = _currentUser.DriverId
            ?? throw new InvalidOperationException("The session names no driver.");

        var search = request.Search?.Trim();
        var offered = await _registry.ListCustomersAsync(_clock.TodayInItaly(), search, cancellationToken);
        if (!string.IsNullOrEmpty(search))
        {
            return new ListTripCustomersResponse([], ToDtos(offered));
        }

        var offeredIds = offered.Select(c => c.Id).ToList();
        var recentIds = await _db.Trips.AsNoTracking()
            .Where(t => t.DriverId == driverId && offeredIds.Contains(t.CustomerId))
            .GroupBy(t => t.CustomerId)
            .Select(g => new { CustomerId = g.Key, Latest = g.Max(t => t.AddedAt) })
            .OrderByDescending(g => g.Latest)
            .Take(RecentCount)
            .Select(g => g.CustomerId)
            .ToListAsync(cancellationToken);

        var recent = recentIds.Select(id => offered.First(c => c.Id == id));
        var others = offered.Where(c => !recentIds.Contains(c.Id));

        return new ListTripCustomersResponse(ToDtos(recent), ToDtos(others));
    }

    private static List<TripCustomerDto> ToDtos(IEnumerable<TripCustomer> customers) =>
        customers.Select(c => new TripCustomerDto(c.Id, c.CompanyName, c.VatNumber)).ToList();
}
