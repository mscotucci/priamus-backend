using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Domain.Vehicles.ValueObjects;

namespace Priamus.Trips.Registry.Application.Services;

/// <summary>The search over the plate and the description of vehicles, shared by every list that offers it.</summary>
internal static class VehicleSearch
{
    /// <summary>
    /// Keeps the vehicles whose plate or description contains <paramref name="search"/>. The stored plate is
    /// already in normal form, so the search text normalised the same way is matched with Contains; the
    /// description is matched on its lowercased text. A search made only of spaces and hyphens has an empty
    /// plate needle, which would match every plate, so it is matched on the description alone.
    /// </summary>
    /// <param name="query">The vehicles to search.</param>
    /// <param name="search">The trimmed, non-empty search text.</param>
    public static IQueryable<Vehicle> Matching(IQueryable<Vehicle> query, string search)
    {
        var plateNeedle = LicensePlate.Normalize(search);
        var descriptionNeedle = search.ToLowerInvariant();
        return plateNeedle.Length == 0
            ? query.Where(v => v.Description.ToLower().Contains(descriptionNeedle))
            : query.Where(v => v.LicensePlate.Unwrap().Contains(plateNeedle)
                || v.Description.ToLower().Contains(descriptionNeedle));
    }
}
