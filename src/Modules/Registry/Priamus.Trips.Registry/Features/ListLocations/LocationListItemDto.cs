using System.Linq.Expressions;
using Priamus.Trips.Registry.Domain.Locations;

namespace Priamus.Trips.Registry.Features.ListLocations;

/// <summary>
/// A single item in the paginated list of locations.
/// </summary>
/// <param name="Id">The location's identifier.</param>
/// <param name="Name">The name.</param>
/// <param name="Municipality">The municipality.</param>
/// <param name="Address">The address, or <c>null</c>.</param>
/// <param name="Status">The status.</param>
/// <param name="LastModifiedAt">UTC instant of the last change.</param>
/// <param name="Version">Version the client sends back with its next write on this location.</param>
public sealed record LocationListItemDto(
    Guid Id,
    string Name,
    string Municipality,
    string? Address,
    LocationStatus Status,
    DateTime LastModifiedAt,
    int Version)
{
    /// <summary>
    /// The projection from a <see cref="Location"/>, an expression so that EF Core translates it
    /// into the query.
    /// </summary>
    public static readonly Expression<Func<Location, LocationListItemDto>> Projection = l => new LocationListItemDto(
        l.Id.Value,
        l.Name,
        l.Municipality,
        l.Address,
        l.Status,
        l.LastModifiedAt,
        l.Version);
}
