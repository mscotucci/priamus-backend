using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;

namespace Priamus.Trips.Registry.Persistence.Converters;

/// <summary>
/// Converts <see cref="VatNumber"/> to/from its normal-form <see cref="string"/> for storage
/// as a PostgreSQL <c>varchar(11)</c> column.
/// </summary>
public sealed class VatNumberConverter : ValueConverter<VatNumber, string>
{
    /// <summary>Initializes a new instance of <see cref="VatNumberConverter"/>.</summary>
    public VatNumberConverter()
        : base(v => v.Value, s => VatNumber.Create(s))
    {
    }
}
