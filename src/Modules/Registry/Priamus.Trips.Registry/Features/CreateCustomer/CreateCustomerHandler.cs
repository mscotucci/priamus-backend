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

namespace Priamus.Trips.Registry.Features.CreateCustomer;

/// <summary>
/// Handles <c>POST /api/registry/customers</c>: registers a new, active customer
/// (PRD §4, RF-15).
/// </summary>
/// <remarks>
/// Race-condition safety net: the VAT number is checked upfront to tell an active holder from a
/// deactivated one, but if two concurrent requests slip through, the unique index
/// (<c>uq_customers_vat_number</c>) is caught on <see cref="DbUpdateException"/> and translated to
/// the same errors.
/// </remarks>
internal sealed class CreateCustomerHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ErrorMessageArguments _arguments;
    private readonly ILogger<CreateCustomerHandler> _logger;

    public CreateCustomerHandler(
        RegistryDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        ErrorMessageArguments arguments,
        ILogger<CreateCustomerHandler> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _arguments = arguments;
        _logger = logger;
    }

    /// <summary>
    /// Creates a customer from the request.
    /// </summary>
    public async Task<Result<CreateCustomerResponse>> HandleAsync(
        CreateCustomerRequest request,
        CancellationToken cancellationToken)
    {
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
            var conflict = await VatNumberConflicts.FindAsync(_db, vatNumber, excluding: null, _arguments, cancellationToken);
            if (conflict is not null)
            {
                return conflict;
            }
        }

        var actorId = _currentUser.UserId;

        Customer customer;
        try
        {
            customer = Customer.Create(
                CustomerId.New(),
                request.CompanyName,
                vatNumber,
                taxCode,
                request.Address,
                _clock,
                actorId);
        }
        catch (DomainException ex)
        {
            return ex.Error;
        }

        _db.Customers.Add(customer);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (vatNumber is not null && VatNumberConflicts.IsUniqueViolation(ex))
        {
            return await VatNumberConflicts.ErrorAfterLostRaceAsync(_db, vatNumber, excluding: null, _arguments, cancellationToken);
        }

        _logger.LogInformation(
            "Customer {CustomerId} created by actor {ActorId}", customer.Id.Value, actorId.Value);

        return new CreateCustomerResponse(
            Id: customer.Id.Value,
            CompanyName: customer.CompanyName,
            VatNumber: customer.VatNumber?.Value,
            TaxCode: customer.TaxCode?.Value,
            Address: customer.Address,
            Status: customer.Status,
            LastModifiedAt: customer.LastModifiedAt,
            Version: customer.Version);
    }
}
