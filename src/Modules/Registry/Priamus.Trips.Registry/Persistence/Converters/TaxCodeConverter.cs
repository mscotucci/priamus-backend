using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;

namespace Priamus.Trips.Registry.Persistence.Converters;

/// <summary>
/// Converts <see cref="TaxCode"/> to/from its normal-form <see cref="string"/> for storage
/// as a PostgreSQL <c>varchar(16)</c> column.
/// </summary>
public sealed class TaxCodeConverter : ValueConverter<TaxCode, string>
{
    /// <summary>Initializes a new instance of <see cref="TaxCodeConverter"/>.</summary>
    public TaxCodeConverter()
        : base(v => v.Value, s => TaxCode.Create(s))
    {
    }
}
