using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Features.ListCustomers;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Features.GetCustomer;

/// <summary>
/// Handles <c>GET /api/registry/customers/{id}</c>: returns one customer in the shape of a list item.
/// </summary>
internal sealed class GetCustomerHandler
{
    private readonly RegistryDbContext _db;

    public GetCustomerHandler(RegistryDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Reads the customer identified by <paramref name="id"/>.
    /// </summary>
    public async Task<Result<CustomerListItemDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var customerId = new CustomerId(id);

        var customer = await _db.Customers
            .AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(CustomerListItemDto.Projection)
            .FirstOrDefaultAsync(cancellationToken);

        if (customer is null)
        {
            return CustomerErrors.NotFound;
        }

        return customer;
    }
}
