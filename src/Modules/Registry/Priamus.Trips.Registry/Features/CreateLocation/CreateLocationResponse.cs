using Priamus.Trips.Registry.Domain.Locations;

namespace Priamus.Trips.Registry.Features.CreateLocation;

/// <summary>
/// Response body for a successful <c>POST /api/registry/locations</c> request.
/// </summary>
/// <param name="Id">The newly created location's identifier.</param>
/// <param name="Name">The trimmed name.</param>
/// <param name="Municipality">The trimmed municipality.</param>
/// <param name="Address">The trimmed address, or <c>null</c>.</param>
/// <param name="Status">The status (always <see cref="LocationStatus.Active"/> on creation).</param>
/// <param name="LastModifiedAt">UTC instant of the last change.</param>
/// <param name="Version">Version the client sends back with its next write on this location.</param>
public sealed record CreateLocationResponse(
    Guid Id,
    string Name,
    string Municipality,
    string? Address,
    LocationStatus Status,
    DateTime LastModifiedAt,
    int Version);
