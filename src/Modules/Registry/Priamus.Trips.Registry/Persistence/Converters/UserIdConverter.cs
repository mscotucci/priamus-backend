using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Registry.Persistence.Converters;

/// <summary>
/// Converts <see cref="UserId"/> (readonly record struct from SharedKernel) to/from
/// a raw <see cref="Guid"/> for storage as a PostgreSQL <c>uuid</c> column.
/// </summary>
public sealed class UserIdConverter : ValueConverter<UserId, Guid>
{
    /// <summary>Initializes a new instance of <see cref="UserIdConverter"/>.</summary>
    public UserIdConverter()
        : base(id => id.Value, value => new UserId(value))
    {
    }
}
