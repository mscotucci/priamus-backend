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

namespace Priamus.Trips.Registry.Features.ChangeContractFromDate;

/// <summary>
/// Handles <c>POST /api/registry/customers/{customerId}/contracts/{contractId}/change-from-date</c>:
/// the contract keeps its own terms and rate and ends the day before <c>ChangeFrom</c>, and a new
/// contract with the requested terms starts on <c>ChangeFrom</c> and ends where the written
/// validity ends; both are saved together. A deactivated customer's contracts are read-only.
/// </summary>
/// <remarks>
/// As in the update slice, the version the client read is compared with the loaded one before
/// anything else, and becomes the original value of the concurrency token. Both pieces are checked
/// against the customer's other contracts; a race on the deferred exclusion constraint
/// (<c>ex_contracts_customer_validity</c>) maps to <see cref="ContractErrors.OverlapWithAnother"/>.
/// The source's status is not checked.
/// </remarks>
internal sealed class ChangeContractFromDateHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ErrorMessageArguments _arguments;
    private readonly ILogger<ChangeContractFromDateHandler> _logger;

    public ChangeContractFromDateHandler(
        RegistryDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        ErrorMessageArguments arguments,
        ILogger<ChangeContractFromDateHandler> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _arguments = arguments;
        _logger = logger;
    }

    /// <summary>
    /// Changes the terms of the contract <paramref name="contractId"/> from the date of the request.
    /// </summary>
    public async Task<Result<ContractDto>> HandleAsync(
        Guid customerId,
        Guid contractId,
        ChangeContractFromDateRequest request,
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
        var source = await _db.Contracts
            .FirstOrDefaultAsync(c => c.Id == id && c.CustomerId == ownerId, cancellationToken);
        if (source is null)
        {
            return ContractErrors.NotFound;
        }

        if (source.Version != request.Version!.Value)
        {
            return ContractErrors.ConcurrentUpdate;
        }

        var changeFrom = request.ChangeFrom!.Value;
        var firstPiece = Validity.Create(request.ValidFrom!.Value, changeFrom.AddDays(-1));
        if (firstPiece.IsFailure)
        {
            return firstPiece.Error;
        }

        var secondPiece = Validity.Create(changeFrom, request.ValidTo);
        if (secondPiece.IsFailure)
        {
            return secondPiece.Error;
        }

        var others = await _db.Contracts
            .AsNoTracking()
            .Where(c => c.CustomerId == ownerId && c.Id != id)
            .ToListAsync(cancellationToken);
        if (ContractOverlaps.Find(others, firstPiece.Value, _arguments) is { } firstOverlap)
        {
            return firstOverlap;
        }

        if (ContractOverlaps.Find(others, secondPiece.Value, _arguments) is { } secondOverlap)
        {
            return secondOverlap;
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
            _db, vehicleRates.Value, source.VehicleRates, _arguments, cancellationToken) is { } vehicleError)
        {
            return vehicleError;
        }

        var actorId = _currentUser.UserId;

        var kept = source.Update(
            firstPiece.Value,
            source.BillingLevel,
            source.PricingMode,
            source.RequiredDocuments,
            source.NightSurcharge,
            source.Rate,
            source.VehicleRates,
            _clock,
            actorId);
        if (kept.IsFailure)
        {
            return kept.Error;
        }

        var created = Contract.Create(
            ContractId.New(),
            ownerId,
            secondPiece.Value,
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
        _db.Entry(source).Property(c => c.Version).OriginalValue = request.Version!.Value;

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
            "Contract {ContractId} of customer {CustomerId} changed from {ChangeFrom} by actor {ActorId}: new contract {NewContractId}",
            contractId, customerId, changeFrom, actorId.Value, contract.Id.Value);

        var vehicleRateDtos = await ContractVehicleRateDto.LoadAsync(_db, [contract], cancellationToken);
        return ContractDto.From(contract, _clock.TodayInItaly(), vehicleRateDtos[contract.Id]);
    }
}
