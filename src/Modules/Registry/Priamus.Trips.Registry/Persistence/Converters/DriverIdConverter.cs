using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Priamus.Trips.Registry.Domain.Drivers;

namespace Priamus.Trips.Registry.Persistence.Converters;

/// <summary>
/// Converts <see cref="DriverId"/> to/from a raw <see cref="Guid"/> for storage as a
/// PostgreSQL <c>uuid</c> column.
/// </summary>
public sealed class DriverIdConverter : ValueConverter<DriverId, Guid>
{
    /// <summary>Initializes a new instance of <see cref="DriverIdConverter"/>.</summary>
    public DriverIdConverter()
        : base(id => id.Value, value => new DriverId(value))
    {
    }
}
