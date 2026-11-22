using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Features.ListContracts;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Features.GetContract;

/// <summary>
/// Handles <c>GET /api/registry/customers/{customerId}/contracts/{contractId}</c>: returns one
/// contract of the customer.
/// </summary>
internal sealed class GetContractHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;

    public GetContractHandler(RegistryDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// Reads the contract <paramref name="contractId"/>; one that belongs to another customer is
    /// not found.
    /// </summary>
    public async Task<Result<ContractDto>> HandleAsync(
        Guid customerId,
        Guid contractId,
        CancellationToken cancellationToken)
    {
        var ownerId = new CustomerId(customerId);
        if (!await _db.Customers.AnyAsync(c => c.Id == ownerId, cancellationToken))
        {
            return CustomerErrors.NotFound;
        }

        var id = new ContractId(contractId);
        var contract = await _db.Contracts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.CustomerId == ownerId, cancellationToken);
        if (contract is null)
        {
            return ContractErrors.NotFound;
        }

        var vehicleRates = await ContractVehicleRateDto.LoadAsync(_db, [contract], cancellationToken);
        var locationRates = await ContractLocationRateDto.LoadAsync(_db, [contract], cancellationToken);
        return ContractDto.From(contract, _clock.TodayInItaly(), vehicleRates[contract.Id], locationRates[contract.Id]);
    }
}
