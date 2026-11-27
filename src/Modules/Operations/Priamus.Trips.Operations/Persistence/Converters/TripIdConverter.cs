using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Priamus.Trips.Operations.Domain.Trips;

namespace Priamus.Trips.Operations.Persistence.Converters;

/// <summary>
/// Converts <see cref="TripId"/> to/from a raw <see cref="Guid"/> for storage as a PostgreSQL
/// <c>uuid</c> column.
/// </summary>
public sealed class TripIdConverter : ValueConverter<TripId, Guid>
{
    /// <summary>Initializes a new instance of <see cref="TripIdConverter"/>.</summary>
    public TripIdConverter()
        : base(id => id.Value, value => new TripId(value))
    {
    }
}
