using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Registry.Application.Services;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Features.ListContracts;
using Priamus.Trips.Registry.Features.Validation;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Features.CreateContract;

/// <summary>
/// Handles <c>POST /api/registry/customers/{customerId}/contracts</c>: adds a contract to an active
/// customer, closing its open contract when the new one starts later (PRD §5.4, RF-16, RF-18).
/// </summary>
/// <remarks>
/// The closed contract keeps its loaded version as concurrency token, so a change made to it
/// between the load and the save fails with <see cref="DbUpdateConcurrencyException"/>. The new
/// validity is checked against every other contract after the close; a race on the deferred
/// exclusion constraint (<c>ex_contracts_customer_validity</c>) maps to
/// <see cref="ContractErrors.OverlapWithAnother"/>.
/// </remarks>
internal sealed class CreateContractHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ErrorMessageArguments _arguments;
    private readonly ILogger<CreateContractHandler> _logger;

    public CreateContractHandler(
        RegistryDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        ErrorMessageArguments arguments,
        ILogger<CreateContractHandler> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _arguments = arguments;
        _logger = logger;
    }

    /// <summary>
    /// Creates a contract for the customer identified by <paramref name="customerId"/>.
    /// </summary>
    public async Task<Result<ContractDto>> HandleAsync(
        Guid customerId,
        CreateContractRequest request,
        CancellationToken cancellationToken)
    {
        var ownerId = new CustomerId(customerId);
        var status = await _db.Customers
            .AsNoTracking()
            .Where(c => c.Id == ownerId)
            .Select(c => (CustomerStatus?)c.Status)
            .FirstOrDefaultAsync(cancellationToken);
        if (status is null)
        {
            return CustomerErrors.NotFound;
        }

        if (status is CustomerStatus.Deactivated)
        {
            return ContractErrors.CustomerDeactivated;
        }

        var validity = Validity.Create(request.ValidFrom!.Value, request.ValidTo);
        if (validity.IsFailure)
        {
            return validity.Error;
        }

        var contracts = await _db.Contracts
            .Where(c => c.CustomerId == ownerId)
            .ToListAsync(cancellationToken);

        var actorId = _currentUser.UserId;

        // RF-18: the open contract that started before the new one ends the day before it.
        var open = contracts.FirstOrDefault(c => c.Validity.IsOpen && c.Validity.ValidFrom < validity.Value.ValidFrom);
        if (open is not null)
        {
            var closed = open.CloseOn(validity.Value.ValidFrom.AddDays(-1), _clock, actorId);
            if (closed.IsFailure)
            {
                return closed.Error;
            }
        }

        if (ContractOverlaps.Find(contracts, validity.Value, _arguments) is { } overlap)
        {
            return overlap;
        }

        var rate = request.RateMembers().ToRate();
        if (rate.IsFailure)
        {
            return rate.Error;
        }

        var vehicleRates = ContractVehicleRateRequest.ToVehicleRates(request.VehicleRates);
        if (vehicleRates.IsFailure)
        {
            return vehicleRates.Error;
        }

        if (await ContractVehicleRateChecks.FindAsync(_db, vehicleRates.Value, [], _arguments, cancellationToken) is { } vehicleError)
        {
            return vehicleError;
        }

        var created = Contract.Create(
            ContractId.New(),
            ownerId,
            validity.Value,
            request.BillingLevel!.Value,
            request.PricingMode!.Value,
            request.RequiredDocuments!,
            request.NightSurcharge!.Value,
            rate.Value,
            vehicleRates.Value,
            _clock,
            actorId);

        if (created.IsFailure)
        {
            return created.Error;
        }

        var contract = created.Value;
        _db.Contracts.Add(contract);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ContractErrors.ConcurrentUpdate;
        }
        catch (Exception ex) when (ContractOverlaps.IsLostRace(ex))
        {
            return ContractErrors.OverlapWithAnother;
        }

        _logger.LogInformation(
            "Contract {ContractId} created for customer {CustomerId} by actor {ActorId}",
            contract.Id.Value, customerId, actorId.Value);

        var vehicleRateDtos = await ContractVehicleRateDto.LoadAsync(_db, [contract], cancellationToken);
        return ContractDto.From(contract, _clock.TodayInItaly(), vehicleRateDtos[contract.Id]);
    }
}
