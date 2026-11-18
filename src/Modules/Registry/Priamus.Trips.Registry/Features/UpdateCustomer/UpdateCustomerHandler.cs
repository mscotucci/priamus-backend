using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Registry.Application.Services;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Features.UpdateCustomer;

/// <summary>
/// Handles <c>PUT /api/registry/customers/{id}</c>: replaces the editable data of a customer, active
/// or deactivated (PRD §4, RF-15).
/// </summary>
/// <remarks>
/// The version the client read is compared with the loaded one before anything else, so a stale
/// client always gets <see cref="CustomerErrors.ConcurrentUpdate"/>, even when its VAT number is
/// taken. It also becomes the original value of the concurrency token, so a customer changed
/// between the load and the save fails with <see cref="DbUpdateConcurrencyException"/>, answered the
/// same way. The VAT number is checked upfront against the other customers; a race on the unique
/// index (<c>uq_customers_vat_number</c>) maps to the same errors.
/// </remarks>
internal sealed class UpdateCustomerHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ErrorMessageArguments _arguments;
    private readonly ILogger<UpdateCustomerHandler> _logger;

    public UpdateCustomerHandler(
        RegistryDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        ErrorMessageArguments arguments,
        ILogger<UpdateCustomerHandler> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _arguments = arguments;
        _logger = logger;
    }

    /// <summary>
    /// Updates the customer identified by <paramref name="id"/> from the request.
    /// </summary>
    public async Task<Result> HandleAsync(
        Guid id,
        UpdateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = new CustomerId(id);
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null)
        {
            return CustomerErrors.NotFound;
        }

        if (customer.Version != request.Version!.Value)
        {
            return CustomerErrors.ConcurrentUpdate;
        }

        VatNumber? vatNumber;
        TaxCode? taxCode;
        try
        {
            vatNumber = string.IsNullOrWhiteSpace(request.VatNumber) ? null : VatNumber.Create(request.VatNumber);
            taxCode = string.IsNullOrWhiteSpace(request.TaxCode) ? null : TaxCode.Create(request.TaxCode);
        }
        catch (DomainException ex)
        {
            return ex.Error;
        }

        if (vatNumber is not null)
        {
            var conflict = await VatNumberConflicts.FindAsync(_db, vatNumber, customerId, _arguments, cancellationToken);
            if (conflict is not null)
            {
                return conflict;
            }
        }

        var actorId = _currentUser.UserId;

        try
        {
            customer.Update(request.CompanyName, vatNumber, taxCode, request.Address, _clock, actorId);
        }
        catch (DomainException ex)
        {
            return ex.Error;
        }

        _db.Entry(customer).Property(c => c.Version).OriginalValue = request.Version!.Value;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CustomerErrors.ConcurrentUpdate;
        }
        catch (DbUpdateException ex) when (vatNumber is not null && VatNumberConflicts.IsUniqueViolation(ex))
        {
            return await VatNumberConflicts.ErrorAfterLostRaceAsync(_db, vatNumber, customerId, _arguments, cancellationToken);
        }

        _logger.LogInformation(
            "Customer {CustomerId} updated by actor {ActorId}", customer.Id.Value, actorId.Value);

        return Result.Success();
    }
}
