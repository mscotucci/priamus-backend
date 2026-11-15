# Appendix: Paging

Paginated query slices use `PagedRequest` / `PagedResponse<T>` from `Priamus.Trips.SharedKernel.Paging`.

## Request

```csharp
public sealed class ListTripsRequest : PagedRequest
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public Guid? CustomerId { get; set; }
}
```

Bound in the endpoint with `[AsParameters]`. A request extending `PagedRequest` is a `sealed class` with settable properties, not a record: query-string binding populates setters. `Page`, `PageSize` and `SortDirection` are optional on the wire (nullable, because `[AsParameters]` treats a non-nullable value type as required and answers 400 before any coercion); read `EffectivePage`, `EffectivePageSize`, `Skip`, `IsAscending`, never the nullable properties.

## Handler

```csharp
private static readonly HashSet<string> SortableColumns =
    new(StringComparer.OrdinalIgnoreCase) { "ExecutionDate", "ReferenceNumber", "FinalAmount" };

var column = SortableColumns.Contains(request.SortColumn ?? string.Empty) ? request.SortColumn! : "ExecutionDate";

query = (column.ToLowerInvariant(), request.IsAscending) switch
{
    ("executiondate", true) => query.OrderBy(t => t.ExecutionDate).ThenBy(t => t.Id),
    ("executiondate", false) => query.OrderByDescending(t => t.ExecutionDate).ThenByDescending(t => t.Id),
    ("referencenumber", true) => query.OrderBy(t => t.ReferenceNumber),
    ("referencenumber", false) => query.OrderByDescending(t => t.ReferenceNumber),
    ("finalamount", true) => query.OrderBy(t => t.FinalAmount).ThenBy(t => t.Id),
    ("finalamount", false) => query.OrderByDescending(t => t.FinalAmount).ThenByDescending(t => t.Id),
    _ => throw new UnreachableException(column),
};

var total = await query.CountAsync(ct);
var items = await query.Skip(request.Skip).Take(request.EffectivePageSize)
    .Select(t => new TripListItem(...))
    .AsNoTracking()
    .ToListAsync(ct);

return new PagedResponse<TripListItem>(items, total, request.EffectivePage, request.EffectivePageSize);
```

Rules:
- Whitelist sort columns with a `HashSet<string>` (OrdinalIgnoreCase) and a switch expression; never `OrderBy(string)` or `EF.Property` with user input. An unknown column falls back to the default.
- Always a deterministic secondary order (`ThenBy` on the key) so pages are stable.
- Filters are the ones the PRD lists for that screen; a list answers the caller's own rows where the PRD says so (a driver's trips), enforced in the query, not in the client.
- `.AsNoTracking()`; project to DTOs in the query, not after materialisation.
- A driver-facing list item never carries amounts (`FinalAmount` above belongs to a back-office list).

## Tests

The nine pagination cases of `testing.md` are mandatory for every paginated endpoint.
