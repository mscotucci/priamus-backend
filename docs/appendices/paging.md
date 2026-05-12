# Appendix: Paging

Standard pagination types live in SharedKernel under `Priamus.Trips.SharedKernel.Paging`.

## Types

```csharp
public enum SortDirection { Ascending = 0, Descending = 1 }

public sealed class PagedRequest
{
    // Page: minimum 1, coerced if invalid
    public int Page { get; set; }

    // PageSize: minimum 1, maximum 100, default 20, coerced if invalid
    public int PageSize { get; set; }

    public string? SortColumn { get; set; }

    public SortDirection SortDirection { get; set; } = SortDirection.Ascending;

    public int Skip => (Page - 1) * PageSize;
    public bool IsAscending => SortDirection == SortDirection.Ascending;
}

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Data,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages { get; }     // ceil(TotalCount / PageSize)
    public bool HasNextPage { get; }
    public bool HasPreviousPage { get; }

    public static PagedResponse<T> Empty(int page, int pageSize);
}
```

`PagedRequest` is a mutable class (not record) because ASP.NET Core query-string model binding populates public setters. Setters validate values to coerce invalid input to safe defaults rather than throwing.

`PagedResponse<T>` is an immutable record.

## Custom paginated requests

Extend `PagedRequest` for slice-specific filters:

```csharp
public sealed class ListTripsRequest : PagedRequest
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public Guid? CustomerId { get; set; }
}
```

Subclasses remain `class` with mutable properties (required by model binding). Bind in the endpoint with `[AsParameters]`.

## Paginated query handler

```csharp
public sealed class ListDriverTripsHandler
{
    private readonly OperationsDbContext _db;
    private readonly ICurrentUser _currentUser;

    private static readonly HashSet<string> AllowedSortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "executionDate",
        "finalAmount",
        "referenceNumber",
    };

    public async Task<Result<PagedResponse<TripListItemDto>>> HandleAsync(
        ListDriverTripsRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.DriverId is not DriverId driverId)
            return AuthErrors.NotADriver;

        var query = _db.Trips
            .AsNoTracking()
            .Where(t => t.DriverId == driverId);

        if (request.From.HasValue)
            query = query.Where(t => t.ExecutionDate >= request.From.Value);
        if (request.To.HasValue)
            query = query.Where(t => t.ExecutionDate <= request.To.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var data = await ApplySorting(query, request)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(t => new TripListItemDto(
                t.Id.Value,
                t.ReferenceNumber,
                t.ExecutionDate,
                t.FinalAmount.Amount,
                t.Issues.Any(i => i.Status == IssueStatus.Open)))
            .ToListAsync(cancellationToken);

        return new PagedResponse<TripListItemDto>(data, totalCount, request.Page, request.PageSize);
    }

    private static IQueryable<Trip> ApplySorting(IQueryable<Trip> query, PagedRequest request)
    {
        var sortColumn = request.SortColumn?.Trim();

        if (string.IsNullOrEmpty(sortColumn) || !AllowedSortColumns.Contains(sortColumn))
            sortColumn = "executionDate";  // default

        return (sortColumn.ToLowerInvariant(), request.IsAscending) switch
        {
            ("executiondate", true)   => query.OrderBy(t => t.ExecutionDate),
            ("executiondate", false)  => query.OrderByDescending(t => t.ExecutionDate),
            ("finalamount", true)     => query.OrderBy(t => t.FinalAmount),
            ("finalamount", false)    => query.OrderByDescending(t => t.FinalAmount),
            ("referencenumber", true) => query.OrderBy(t => t.ReferenceNumber),
            ("referencenumber", false)=> query.OrderByDescending(t => t.ReferenceNumber),
            _ => query.OrderBy(t => t.ExecutionDate),
        };
    }
}
```

## Whitelist sort columns — mandatory

User input drives `SortColumn` as a string. NEVER pass it directly to `OrderBy(string)` or `EF.Property` without validation against a whitelist.

The whitelist is a `HashSet<string>` with `StringComparer.OrdinalIgnoreCase`. Sort direction is dispatched via `switch` expression on `(column, ascending)`. Unknown columns fall back to a default safe column.

## Endpoint

```csharp
public static class ListDriverTripsEndpoint
{
    public static IEndpointRouteBuilder MapListDriverTrips(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/trips", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Driver)
            .WithName("ListDriverTrips")
            .WithTags("Trips")
            .Produces<PagedResponse<TripListItemDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] ListDriverTripsRequest request,
        ListDriverTripsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
```

Query endpoints do NOT use `TransactionFilter` (no writes).

`[AsParameters]` is required to bind query-string fields into a `PagedRequest`-derived request.
