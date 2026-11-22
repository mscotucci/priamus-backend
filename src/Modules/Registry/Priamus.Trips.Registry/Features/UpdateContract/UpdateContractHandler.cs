using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Registry.Application.Services;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Features.Validation;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Features.UpdateContract;

/// <summary>
/// Handles <c>PUT /api/registry/customers/{customerId}/contracts/{contractId}</c>: replaces the
/// terms and rate of a contract of an active customer (PRD §5.4, RF-16); a deactivated customer's
/// contracts are read-only.
/// </summary>
/// <remarks>
/// The version the client read is compared with the loaded one before anything else, so a stale
/// client always gets <see cref="ContractErrors.ConcurrentUpdate"/>, even when its validity also
/// overlaps. It also becomes the original value of the concurrency token, so a contract changed
/// between the load and the save fails with <see cref="DbUpdateConcurrencyException"/>, answered
/// the same way. The new validity is checked against the customer's other contracts; a race on the
/// deferred exclusion constraint (<c>ex_contracts_customer_validity</c>) maps to
/// <see cref="ContractErrors.OverlapWithAnother"/>. An edit never closes another contract.
/// </remarks>
internal sealed class UpdateContractHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ErrorMessageArguments _arguments;
    private readonly ILogger<UpdateContractHandler> _logger;

    public UpdateContractHandler(
        RegistryDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        ErrorMessageArguments arguments,
        ILogger<UpdateContractHandler> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _arguments = arguments;
        _logger = logger;
    }

    /// <summary>
    /// Updates the contract <paramref name="contractId"/> of the customer from the request.
    /// </summary>
    public async Task<Result> HandleAsync(
        Guid customerId,
        Guid contractId,
        UpdateContractRequest request,
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
            return ContractErrors.CustomerDeactivatedReadOnly;
        }

        var id = new ContractId(contractId);
        var contract = await _db.Contracts
            .FirstOrDefaultAsync(c => c.Id == id && c.CustomerId == ownerId, cancellationToken);
        if (contract is null)
        {
            return ContractErrors.NotFound;
        }

        if (contract.Version != request.Version!.Value)
        {
            return ContractErrors.ConcurrentUpdate;
        }

        var validity = Validity.Create(request.ValidFrom!.Value, request.ValidTo);
        if (validity.IsFailure)
        {
            return validity.Error;
        }

        var others = await _db.Contracts
            .AsNoTracking()
            .Where(c => c.CustomerId == ownerId && c.Id != id)
            .ToListAsync(cancellationToken);
        if (ContractOverlaps.Find(others, validity.Value, _arguments) is { } overlap)
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

        if (await ContractVehicleRateChecks.FindAsync(
            _db, vehicleRates.Value, contract.VehicleRates, _arguments, cancellationToken) is { } vehicleError)
        {
            return vehicleError;
        }

        var locationRates = ContractLocationRateRequest.ToLocationRates(request.LocationRates);
        if (locationRates.IsFailure)
        {
            return locationRates.Error;
        }

        if (await ContractLocationRateChecks.FindAsync(
            _db, locationRates.Value, contract.LocationRates, _arguments, cancellationToken) is { } locationError)
        {
            return locationError;
        }

        var actorId = _currentUser.UserId;

        var updated = contract.Update(
            validity.Value,
            request.BillingLevel!.Value,
            request.PricingMode!.Value,
            request.RequiredDocuments!,
            request.NightSurcharge!.Value,
            rate.Value,
            vehicleRates.Value,
            locationRates.Value,
            _clock,
            actorId);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        _db.Entry(contract).Property(c => c.Version).OriginalValue = request.Version!.Value;

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
            "Contract {ContractId} of customer {CustomerId} updated by actor {ActorId}",
            contractId, customerId, actorId.Value);

        return Result.Success();
    }
}
