using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Priamus.Trips.Registry.Domain.Contracts;

namespace Priamus.Trips.Registry.Persistence.Converters;

/// <summary>
/// Converts <see cref="ContractId"/> to/from a raw <see cref="Guid"/> for storage as a
/// PostgreSQL <c>uuid</c> column.
/// </summary>
public sealed class ContractIdConverter : ValueConverter<ContractId, Guid>
{
    /// <summary>Initializes a new instance of <see cref="ContractIdConverter"/>.</summary>
    public ContractIdConverter()
        : base(id => id.Value, value => new ContractId(value))
    {
    }
}
