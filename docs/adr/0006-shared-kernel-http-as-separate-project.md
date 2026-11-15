# 0006 — SharedKernel.Http as a separate project

## Status

Accepted (2026-05)

## Context

The `Result<T>` / `Error` pattern (ADR 0002) requires a bridge from the error model to HTTP `IResult` / `ProblemDetails`. The bridge code (`ResultHttpExtensions.ToProblemDetails`) needs access to `Microsoft.AspNetCore.Http.Results` and related ASP.NET Core types.

We had a stated rule: **SharedKernel must remain framework-agnostic** — no ASP.NET Core dependency in the core project. This rule was designed to allow non-web consumers (workers, batch jobs, command-line tools) to use the `Result/Error` primitives without taking a transitive dependency on the web framework.

When implementing `ResultHttpExtensions`, we discovered:

- The `Microsoft.AspNetCore.Http.Abstractions` NuGet package on .NET 10 does NOT expose `Results.Problem` or `TypedResults`. These types ship only inside the `Microsoft.AspNetCore.App` shared framework.
- Therefore, any code that calls `Results.Problem(...)` must declare `<FrameworkReference Include="Microsoft.AspNetCore.App" />`.
- Placing `ResultHttpExtensions` in `Priamus.Trips.Host` is technically possible but creates a circular conceptual dependency: module endpoints (which live in module implementation projects) would need to call code in Host, but Host references the modules, not vice versa.

## Decision

Create a separate project, **`Priamus.Trips.SharedKernel.Http`**, dedicated to the HTTP bridge:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="..\Priamus.Trips.SharedKernel\Priamus.Trips.SharedKernel.csproj" />
  </ItemGroup>
</Project>
```

This project contains only `ResultHttpExtensions`. It references `Priamus.Trips.SharedKernel` (the framework-agnostic core) and adds the ASP.NET Core dependency that the core does not have.

Module implementation projects (`Priamus.Trips.Identity`, `Priamus.Trips.Registry`, etc.) reference `Priamus.Trips.SharedKernel.Http` directly, in addition to `Priamus.Trips.SharedKernel`. The `Host` project receives `Priamus.Trips.SharedKernel.Http` transitively through any module.

To enforce the rule that `Priamus.Trips.SharedKernel` itself never gains an ASP.NET Core dependency, an MSBuild `<Target>` was added to its `.csproj` that fails the build if `<FrameworkReference Include="Microsoft.AspNetCore.App" />` is ever added to that project.

## Consequences

**Positive:**
- The framework-agnostic rule is preserved at the code level AND enforced at build time.
- The split is self-documenting: a developer who sees `Priamus.Trips.SharedKernel.Http` in the project references immediately understands it brings the HTTP bridge.
- Future non-web consumers can reference only `Priamus.Trips.SharedKernel` without taking on ASP.NET Core.
- The dependency direction is clean: modules → SharedKernel.Http → SharedKernel.

**Negative:**
- One additional project in the solution (now 20 instead of 19).
- A small amount of conceptual overhead for new contributors: they must learn that the HTTP bridge is "the second SharedKernel".

**Neutral:**
- The MSBuild guard is one of several invariants codified at build time. See `docs/appendices/architectural-invariants.md` for the full list.

## Alternatives considered

**`FrameworkReference` directly in `Priamus.Trips.SharedKernel`** was rejected because it forces every consumer of `Result<T>` to take on ASP.NET Core, even if they only consume `Result`/`Error` from a non-web context.

**`ResultHttpExtensions` in `Priamus.Trips.Host`** was tried first and then abandoned because the dependency direction (Host → modules, not the reverse) makes module endpoints unable to call it.

**Duplicating the mapping logic in each endpoint** was rejected because adding a new `ErrorType` would require updating every endpoint, violating the centralization that ADR 0002 establishes.

**A global endpoint filter that intercepts `Result<T>`** was considered. Technically feasible but more complex than the explicit `result.Match(...)` call in each endpoint, and harder to debug when something goes wrong.
