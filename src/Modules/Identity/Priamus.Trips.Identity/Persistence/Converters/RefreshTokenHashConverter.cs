using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Priamus.Trips.Identity.Domain.RefreshTokens.ValueObjects;

namespace Priamus.Trips.Identity.Persistence.Converters;

/// <summary>
/// Converts <see cref="RefreshTokenHash"/> to/from a <see cref="string"/> for storage
/// as a PostgreSQL <c>varchar(64)</c> column.
/// </summary>
/// <remarks>
/// Uses the primary constructor directly rather than <c>RefreshTokenHash.Create</c> to
/// avoid re-running validation on a value already known to be valid (it was validated
/// before being stored).
/// </remarks>
public sealed class RefreshTokenHashConverter : ValueConverter<RefreshTokenHash, string>
{
    /// <summary>Initializes a new instance of <see cref="RefreshTokenHashConverter"/>.</summary>
    public RefreshTokenHashConverter()
        : base(v => v.Value, s => new RefreshTokenHash(s))
    {
    }
}
