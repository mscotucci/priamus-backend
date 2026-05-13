using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Priamus.Trips.Identity.Domain.RefreshTokens;

namespace Priamus.Trips.Identity.Persistence.Converters;

/// <summary>
/// Converts <see cref="RefreshTokenId"/> to/from a raw <see cref="Guid"/> for storage
/// as a PostgreSQL <c>uuid</c> column.
/// </summary>
public sealed class RefreshTokenIdConverter : ValueConverter<RefreshTokenId, Guid>
{
    /// <summary>Initializes a new instance of <see cref="RefreshTokenIdConverter"/>.</summary>
    public RefreshTokenIdConverter()
        : base(id => id.Value, value => new RefreshTokenId(value))
    {
    }
}
