using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Priamus.Trips.Registry.Domain.Locations;

namespace Priamus.Trips.Registry.Persistence.Converters;

/// <summary>
/// Converts <see cref="LocationId"/> to/from a raw <see cref="Guid"/> for storage as a
/// PostgreSQL <c>uuid</c> column.
/// </summary>
public sealed class LocationIdConverter : ValueConverter<LocationId, Guid>
{
    /// <summary>Initializes a new instance of <see cref="LocationIdConverter"/>.</summary>
    public LocationIdConverter()
        : base(id => id.Value, value => new LocationId(value))
    {
    }
}
