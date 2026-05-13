# 0009 — No repository pattern; handlers depend on DbContext directly

## Status

Accepted (2026-05)

## Context

The project adopts vertical slice architecture inside each module (see `context.md` §3, §4): each feature lives under `Features/{UseCase}/` with its own `Request`, `Response`, `Handler`, and `Endpoint`. Cross-feature shared code is minimized in favor of cohesion per use case.

The project also adopts CQRS-light: commands flow through handlers that mutate aggregates; queries bypass aggregates and project from the DbContext directly (`context.md` §3).

When the Identity persistence layer was first being designed, repository interfaces (`IUserRepository`, `IRefreshTokenRepository`) were specified in the brief as the standard data-access abstraction for write-side operations on aggregates. During review, this choice was questioned and the underlying assumption was re-examined.

The question is whether the project should adopt the classic Repository pattern (a per-aggregate abstraction in `Application/Abstractions/` exposing `Add`, `Update`, `GetByIdAsync`, etc.) or whether handlers should depend on the module's `DbContext` directly.

## Decision

**Handlers depend on the module's `DbContext` directly. No generic `I{Aggregate}Repository` interfaces.**

Concretely:

- Write-side handlers inject `IdentityDbContext` (or the relevant module's `DbContext`) and call `dbContext.Users.Add(user)`, `await dbContext.SaveChangesAsync(ct)` directly.
- Aggregate hydration from the DbContext for write operations (`dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, ct)`) is done inline in the handler.
- Read-side queries already bypass aggregates per CQRS-light, projecting from the `DbContext` directly into DTOs.
- **Where specialized read queries are reused across multiple slices** (e.g. "find the active user by username" applied in Login, ChangePassword, and Revoke slices), they live as extension methods on `IQueryable<T>` or as small read-only query services — not as repository methods. This avoids drift on shared filters (e.g. forgetting `WHERE Status = Active`) without re-introducing the repository abstraction.
- **`DbContext` is registered as scoped via DI** and exposed to the feature layer through standard constructor injection.

This decision applies to **all modules**: Identity, Registry, Operations, BackOffice.

## Consequences

**Positive:**
- One less layer of indirection between handlers and persistence. Handlers read top-to-bottom as a single unit of work.
- No drift between repository methods and the real semantics of EF Core (tracking, change detection, deferred execution). The handler sees what's actually happening.
- Aligned with vertical slice architecture: cross-feature shared code is minimized; each slice is self-contained except for genuinely reusable primitives.
- Aligned with CQRS-light: write and read sides are symmetric in their dependency on `DbContext`.
- Less code overall: no repository interfaces, no implementations, no DI registrations for them, no tests for repository implementations that re-test EF Core itself.

**Negative:**
- Handlers are tightly coupled to EF Core. Swapping to another ORM (or to raw Dapper, or to a different DB) would require touching every handler. This trade-off is accepted: the project is not designed to be ORM-agnostic, and a hypothetical migration would require non-trivial rework even with repositories (LINQ-to-Entities is not portable; tracking semantics are not portable).
- Mocking persistence in unit tests is not idiomatic with this pattern. This is also accepted: the project already mandates Testcontainers PostgreSQL for integration tests (`context.md` §6) rather than mocking persistence. Handler-level tests that need to exercise persistence run against a real database.
- When a query filter is genuinely reusable across slices, the team must remember to use an `IQueryable<T>` extension method or a dedicated query service rather than re-implementing it. This is a discipline trade-off rather than a structural one.

**Neutral:**
- The choice of "no repository" is conventional in the Jimmy Bogard / vertical slice tradition and unconventional in the classic Vernon / Evans DDD tradition. The project commits to the former.
- Some legacy DDD literature treats "Repository" as a pillar of tactical patterns. This ADR explicitly does not consider that an architectural requirement; the design pillars retained from DDD are aggregates, value objects, domain events, and bounded contexts (modules). Repositories are a pattern, not a pillar.

## Alternatives considered

**Classic Repository pattern (`IUserRepository`, `IRefreshTokenRepository`, etc.) with generic `Add/Update/GetByIdAsync` methods.** Rejected because:

1. It duplicates `DbSet<T>`, which is already a tracked collection. Wrapping a wrapper provides no architectural benefit in a project that is not ORM-agnostic.
2. Asymmetric with the read side, where queries already bypass repositories per CQRS-light.
3. Adds layers without addressing a real friction. The arguments traditionally cited (testability, ORM-agnosticism) do not apply: testability is delivered by Testcontainers, ORM-agnosticism is a non-goal.

**Repository pattern for specialized operations only (no generic `Add/Update`, only domain-meaningful methods like `GetActiveUserByUsernameAsync`).** Rejected as the default, but partially adopted via the `IQueryable<T>` extension methods / query services note above. The form is different: not a per-aggregate `I{Aggregate}Repository` interface, but small, specific, composable helpers attached to the read side.

**Generic repository base class with shared `Add/Update/GetByIdAsync` plus aggregate-specific subclasses.** Rejected because it inherits the downsides of the classic pattern plus the additional friction of base classes in a vertical-slice architecture.

## Notes on transition

The Identity persistence layer, in its current draft form (at the time this ADR is accepted), includes `IUserRepository` and `IRefreshTokenRepository` interfaces and their implementations. A follow-up to the persistence engineer agent will remove these in favor of direct `DbContext` access. The Identity domain layer is not affected — aggregates, value objects, domain events, and errors classes remain as committed.

Future modules (Registry, Operations, BackOffice) will follow this ADR from the start.

## References

- `context.md` §3 (vertical slice architecture, CQRS-light)
- `context.md` §6 (no in-memory database in tests, Testcontainers mandate)
- `docs/appendices/persistence.md` (to be updated to reflect this decision)
- Jimmy Bogard, "Vertical Slice Architecture" (2018) — the conceptual inspiration for the no-repository stance in this style of design
