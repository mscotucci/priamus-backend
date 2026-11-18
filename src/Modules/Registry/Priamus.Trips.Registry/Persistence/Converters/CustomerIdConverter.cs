using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Priamus.Trips.Registry.Domain.Customers;

namespace Priamus.Trips.Registry.Persistence.Converters;

/// <summary>
/// Converts <see cref="CustomerId"/> to/from a raw <see cref="Guid"/> for storage as a
/// PostgreSQL <c>uuid</c> column.
/// </summary>
public sealed class CustomerIdConverter : ValueConverter<CustomerId, Guid>
{
    /// <summary>Initializes a new instance of <see cref="CustomerIdConverter"/>.</summary>
    public CustomerIdConverter()
        : base(id => id.Value, value => new CustomerId(value))
    {
    }
}
