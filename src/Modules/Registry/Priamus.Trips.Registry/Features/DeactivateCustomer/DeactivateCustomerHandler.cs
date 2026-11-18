using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Features.DeactivateCustomer;

/// <summary>
/// Handles <c>POST /api/registry/customers/{id}/deactivate</c>: deactivates a customer (PRD RF-15).
/// </summary>
/// <remarks>
/// The version the client read is compared with the loaded one first, so a stale client always gets
/// <see cref="CustomerErrors.ConcurrentUpdate"/>, even when the customer already has the requested
/// status; only a matching version reaches the aggregate's no-op, which answers 204 without an audit
/// row. The version also becomes the original value of the concurrency token, so a customer changed
/// between the load and the save fails with <see cref="DbUpdateConcurrencyException"/>, answered the
/// same way.
/// </remarks>
internal sealed class DeactivateCustomerHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<DeactivateCustomerHandler> _logger;

    public DeactivateCustomerHandler(
        RegistryDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        ILogger<DeactivateCustomerHandler> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Deactivates the customer identified by <paramref name="id"/>.
    /// </summary>
    public async Task<Result> HandleAsync(
        Guid id,
        DeactivateCustomerRequest request,
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

        var actorId = _currentUser.UserId;

        customer.Deactivate(_clock, actorId);

        _db.Entry(customer).Property(c => c.Version).OriginalValue = request.Version!.Value;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CustomerErrors.ConcurrentUpdate;
        }

        _logger.LogInformation(
            "Customer {CustomerId} deactivated by actor {ActorId}", customer.Id.Value, actorId.Value);

        return Result.Success();
    }
}
