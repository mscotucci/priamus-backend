using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;

namespace Priamus.Trips.Identity.Persistence.Converters;

/// <summary>
/// Converts <see cref="PasswordHash"/> to/from a <see cref="string"/> for storage as
/// a PostgreSQL <c>text</c> column.
/// </summary>
/// <remarks>
/// Uses the primary constructor directly rather than <c>PasswordHash.Create</c> to avoid
/// re-running validation on a value already known to be valid (it was validated before
/// being stored).
/// </remarks>
public sealed class PasswordHashConverter : ValueConverter<PasswordHash, string>
{
    /// <summary>Initializes a new instance of <see cref="PasswordHashConverter"/>.</summary>
    public PasswordHashConverter()
        : base(v => v.Value, s => new PasswordHash(s))
    {
    }
}
