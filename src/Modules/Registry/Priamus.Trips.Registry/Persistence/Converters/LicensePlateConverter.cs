using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Priamus.Trips.Registry.Domain.Vehicles.ValueObjects;

namespace Priamus.Trips.Registry.Persistence.Converters;

/// <summary>
/// Converts <see cref="LicensePlate"/> to/from its normal-form <see cref="string"/> for storage
/// as a PostgreSQL <c>varchar(10)</c> column.
/// </summary>
public sealed class LicensePlateConverter : ValueConverter<LicensePlate, string>
{
    /// <summary>Initializes a new instance of <see cref="LicensePlateConverter"/>.</summary>
    public LicensePlateConverter()
        : base(v => v.Value, s => LicensePlate.Create(s))
    {
    }
}
