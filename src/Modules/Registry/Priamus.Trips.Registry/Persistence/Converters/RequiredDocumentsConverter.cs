using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Priamus.Trips.Registry.Domain.Contracts;

namespace Priamus.Trips.Registry.Persistence.Converters;

/// <summary>
/// Converts the required documents of a <see cref="Contract"/> to/from a PostgreSQL <c>text[]</c>
/// of enum names, in the order they are held (the canonical order).
/// </summary>
public sealed class RequiredDocumentsConverter : ValueConverter<IReadOnlyList<RequiredDocument>, string[]>
{
    /// <summary>Initializes a new instance of <see cref="RequiredDocumentsConverter"/>.</summary>
    public RequiredDocumentsConverter()
        : base(
            documents => documents.Select(d => d.ToString()).ToArray(),
            names => names.Select(Enum.Parse<RequiredDocument>).ToArray())
    {
    }
}

/// <summary>
/// Compares, hashes and snapshots the required documents by content, so that change tracking sees
/// the array behind the read-only list as a value.
/// Equality and hash are order-sensitive, which is correct because the order is canonical: the list
/// always holds the documents in the fixed enum order, so equal sets are equal sequences.
/// </summary>
public sealed class RequiredDocumentsComparer : ValueComparer<IReadOnlyList<RequiredDocument>>
{
    /// <summary>Initializes a new instance of <see cref="RequiredDocumentsComparer"/>.</summary>
    public RequiredDocumentsComparer()
        : base(
            (left, right) => ReferenceEquals(left, right)
                || (left != null && right != null && left.SequenceEqual(right)),
            documents => documents.Aggregate(0, (hash, d) => HashCode.Combine(hash, (int)d)),
            documents => documents.ToArray())
    {
    }
}
