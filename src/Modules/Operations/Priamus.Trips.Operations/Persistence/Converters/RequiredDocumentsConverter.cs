using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Priamus.Trips.Operations.Persistence.Converters;

/// <summary>
/// Converts the required documents of a trip to/from a PostgreSQL <c>text[]</c> of enum names, in the
/// order the contract listed them.
/// </summary>
public sealed class RequiredDocumentsConverter : ValueConverter<IReadOnlyList<string>, string[]>
{
    /// <summary>Initializes a new instance of <see cref="RequiredDocumentsConverter"/>.</summary>
    public RequiredDocumentsConverter()
        : base(
            documents => documents.ToArray(),
            names => names.ToArray())
    {
    }
}

/// <summary>
/// Compares, hashes and snapshots the required documents by content, so that change tracking sees
/// the array behind the read-only list as a value.
/// </summary>
public sealed class RequiredDocumentsComparer : ValueComparer<IReadOnlyList<string>>
{
    /// <summary>Initializes a new instance of <see cref="RequiredDocumentsComparer"/>.</summary>
    public RequiredDocumentsComparer()
        : base(
            (left, right) => ReferenceEquals(left, right)
                || (left != null && right != null && left.SequenceEqual(right)),
            documents => documents.Aggregate(0, (hash, d) => HashCode.Combine(hash, d)),
            documents => documents.ToArray())
    {
    }
}
