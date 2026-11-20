using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Features.ListContracts;

/// <summary>
/// Handles <c>GET /api/registry/customers/{customerId}/contracts</c>: returns every contract of the
/// customer, newest start first, not paged (PRD §5.4).
/// </summary>
internal sealed class ListContractsHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<ListContractsHandler> _logger;

    public ListContractsHandler(RegistryDbContext db, IClock clock, ILogger<ListContractsHandler> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Lists the contracts of the customer identified by <paramref name="customerId"/>.
    /// </summary>
    public async Task<Result<IReadOnlyList<ContractDto>>> HandleAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var id = new CustomerId(customerId);
        if (!await _db.Customers.AnyAsync(c => c.Id == id, cancellationToken))
        {
            return CustomerErrors.NotFound;
        }

        var contracts = await _db.Contracts
            .AsNoTracking()
            .Where(c => c.CustomerId == id)
            .OrderByDescending(c => c.Validity.ValidFrom)
            .ToListAsync(cancellationToken);

        var today = _clock.TodayInItaly();
        var vehicleRates = await ContractVehicleRateDto.LoadAsync(_db, contracts, cancellationToken);
        IReadOnlyList<ContractDto> items = [.. contracts.Select(c => ContractDto.From(c, today, vehicleRates[c.Id]))];

        _logger.LogInformation("Listed {Count} contracts of customer {CustomerId}", items.Count, customerId);

        return Result<IReadOnlyList<ContractDto>>.Success(items);
    }
}
