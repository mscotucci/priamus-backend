using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;

namespace Priamus.Trips.Identity.Persistence.Converters;

/// <summary>
/// Converts <see cref="Username"/> to/from a <see cref="string"/> for storage as
/// a PostgreSQL <c>varchar(64)</c> column.
/// </summary>
/// <remarks>
/// Uses the primary constructor directly rather than <c>Username.Create</c> to avoid
/// re-running validation on a value already known to be valid (it was validated before
/// being stored).
/// </remarks>
public sealed class UsernameConverter : ValueConverter<Username, string>
{
    /// <summary>Initializes a new instance of <see cref="UsernameConverter"/>.</summary>
    public UsernameConverter()
        : base(v => v.Value, s => Username.Create(s))
    {
    }
}
