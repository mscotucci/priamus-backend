using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Priamus.Trips.Registry.Domain.Vehicles;

namespace Priamus.Trips.Registry.Persistence.Converters;

/// <summary>
/// Converts <see cref="VehicleId"/> to/from a raw <see cref="Guid"/> for storage as a
/// PostgreSQL <c>uuid</c> column.
/// </summary>
public sealed class VehicleIdConverter : ValueConverter<VehicleId, Guid>
{
    /// <summary>Initializes a new instance of <see cref="VehicleIdConverter"/>.</summary>
    public VehicleIdConverter()
        : base(id => id.Value, value => new VehicleId(value))
    {
    }
}
