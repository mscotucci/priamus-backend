# Priamus Trips Backend — Context

Technical context for the Priamus Trips backend. This repository hosts only backend code; the frontend lives in a separate repository and consumes the OpenAPI contract produced by the Minimal API endpoints (ADR 0004).

---

## 1. Product overview

Priamus Trips is the digital management and monitoring system for freight trips operated by Priamus Ecologica S.r.l. Functional requirements are the PRD in `docs/PRD.md`.

Two user-facing surfaces consume this backend:
- **Driver PWA** (smartphone / 10-inch tablet, BYOD): drivers record their trips during or at the end of the day, with attachments and end-of-day summaries.
- **Back-office web app** (desktop): the secretariat manages master data (customers, contracts, rates, vehicles, drivers), reviews trips, resolves anomalies, exports invoicing data.

**Strict data-boundary rule**: drivers must NEVER see pricing, tariffs, rates, or calculated totals. Financial logic lives server-side and is surfaced only on the back-office endpoints. Every endpoint and DTO contract must respect this boundary.

### Language

- **Code** (identifiers, comments, logs, error descriptions, file names, namespaces, tests, README, ADR, commit messages): **English, always.**
- **End-user content** (validation messages, generated documents, emails): **Italian, in `.resx` files**, keyed by `Error.Code`.
- Domain terms: `docs/glossary.md` maps the Italian PRD terms to the English identifiers. The bounded-context glossaries (`CONTEXT.md` per module, indexed by `CONTEXT-MAP.md`) are the authority for meaning once a context has one; consult them before introducing a domain identifier.

### Modules

| Module | Assembly prefix | Responsibility |
|---|---|---|
| Identity | `Priamus.Trips.Identity` | Authentication, sessions (refresh tokens), users and roles, password hashing, identity audit log |
| Registry | `Priamus.Trips.Registry` | Customers, contracts, rates (incl. overrides), vehicles, drivers, locations, pricing engine (not yet built) |
| Operations | `Priamus.Trips.Operations` | Trips (+ attachments and issues as child entities), daily summaries (not yet built) |
| BackOffice | `Priamus.Trips.BackOffice` | Reporting, filterable trip list, Excel/PDF export (not yet built) |

### Out of scope (MVP)

Explicitly NOT to implement; require re-discussion if requested:

Offline mode, push notifications, GPS tracking, OCR of documents, billing system integration, end-customer app, advanced analytics, occasional rented vehicles not in registry, vehicle categorization with category-level pricing, trailer-differentiated pricing, recipient digital signature, automatic invoice generation.

---

## 2. Stack (non-negotiable)

- **.NET 10 LTS**, C# 14, nullable enabled, `TreatWarningsAsErrors=true`, implicit usings.
- **PostgreSQL 16+** with **EF Core 10** (`Npgsql.EntityFrameworkCore.PostgreSQL`).
- **ASP.NET Core 10 Minimal API**, ProblemDetails, native OpenAPI.
- **JWT bearer issued by the application**, with refresh token rotation (ADR 0005).
- Libraries: `Microsoft.Extensions.Validation`, `Scrutor`, `Serilog.AspNetCore`, `xUnit v3`, `Shouldly`, `NSubstitute`, `Testcontainers.PostgreSql`.
- Central package management (`Directory.Packages.props`). `dotnet test` runs on Microsoft.Testing.Platform (opt-in in `global.json`).

Forbidden: MediatR, FluentValidation, AutoMapper, MassTransit, EF Core InMemory, any commercial-license library.

---

## 3. Architecture

- **Modular monolith** with vertical slices inside each module (ADR 0001).
- **DDD tactical patterns** where the domain has behaviour (trips, contracts, rates, users); catalogs and simple registries stay plain entities.
- **CQRS light**: commands through handlers; queries project from the DbContext directly.
- **No repository pattern; the handler is the commit point** (ADR 0009, ADR 0011).
- **Slices before shared persistence**: a module is built outside-in, whole slices first (ADR 0012).
- Inter-module calls are synchronous through `*.Contracts` interfaces; asynchronous integration goes through an outbox persisted in the database. No external message bus in MVP.
- A module project references only `Priamus.Trips.{X}.Contracts` of other modules, never `Priamus.Trips.{X}`. Contracts projects reference only `Priamus.Trips.SharedKernel`.
- One PostgreSQL database for the whole application, one connection string (`ConnectionStrings:Database`) read by every module; each module owns its schema in it. No cross-schema foreign keys; a reference to another module is a bare `Guid`.
- Single deploy artifact (`Priamus.Trips.Host`, Docker). Migrations are applied at host startup in Development only; in production a dedicated job applies them before startup. After that step the host runs the bootstrap administrator step in every environment: it creates the first `Admin` from `Identity:BootstrapAdmin` when no user exists; in Production it needs the schema already migrated, otherwise the host does not start.

### Solution structure

```
priamus-trips-backend/
├── Priamus.Trips.slnx
├── Directory.Build.props, Directory.Packages.props, global.json, NuGet.config
├── CLAUDE.md                            ← instructions for Claude Code
├── CONTEXT-MAP.md                       ← bounded contexts and their CONTEXT.md
├── docs/{context.md, glossary.md, PRD.md, backlog.md, adr/, appendices/}
├── src/
│   ├── Priamus.Trips.Host/
│   ├── Priamus.Trips.SharedKernel/       ← framework-agnostic primitives
│   ├── Priamus.Trips.SharedKernel.Http/  ← ResultHttpExtensions
│   └── Modules/{Module}/{Priamus.Trips.{Module}, Priamus.Trips.{Module}.Contracts}/
└── tests/
    ├── Priamus.Trips.IntegrationTesting/ ← shared test rig (class library)
    └── Priamus.Trips.{Module}.{UnitTests|IntegrationTests}/
```

Module internal layout:

```
Priamus.Trips.{Module}/
├── Domain/{Aggregate}/{Aggregate,*Errors,ValueObjects,Events}.cs
├── Features/{UseCase}/{Request,Response,Handler,Endpoint}.cs
├── Persistence/{DbContext,Configurations,Converters,Queries,Migrations}/
├── Application/{Abstractions,Services}/
├── Infrastructure/
└── DependencyInjection/{Module}Module{Extensions,Endpoints}.cs
```

---

## 4. Trip aggregate — canonical reference

```
Trip (AggregateRoot)
├── Id: TripId
├── ReferenceNumber: string
├── DriverId, CustomerId, ContractId
├── VehicleComposition (sealed: TractorWithTrailer | StandaloneTractor | StandaloneVehicle)
├── Route (value object)
├── Parameters (sealed: ByHourParameters | ByWeightParameters | ByDistanceParameters | ByTripParameters)
├── RateSnapshot (immutable value object, jsonb in DB)
├── FinalAmount: Money (denormalized)
├── AppliedTimeBand: TimeBand (sealed: Daytime | Nighttime, denormalized)
├── Attachments, Issues (child entities)
├── Audit fields, Version (concurrency)
```

Detailed invariants, structure, and operations are in `appendices/domain-modeling.md`.

The pricing engine is the single highest-risk component (PRD §10.2). It lives in module `Registry`, exposed via `IPricingEngine` in `Registry.Contracts`. It is a **pure function**: same inputs → same output, no side effects, no I/O (ADR 0003). All test cases in PRD §12 must have corresponding numeric test assertions; coverage target on pricing types: 100%. Algorithm and edge cases are in `appendices/pricing-engine.md`.

---

## 5. SharedKernel — strict scope

`Priamus.Trips.SharedKernel` (no ASP.NET Core reference, enforced by an MSBuild guard):
- Results: `Result`, `Result<T>`, `Error`, `ErrorType`
- Domain base: `AggregateRoot` (with the `Version` concurrency token), `IDomainEvent`, `DomainException`
- Money and quantities: `Money`, `Currency`, `Tons`, `Kilometers`, `Hours`
- Paging: `PagedRequest`, `PagedResponse<T>`, `SortDirection`
- Time: `IClock`, `SystemClock`
- Security: `ICurrentUser`, `UserId` (Guid)

`Priamus.Trips.SharedKernel.Http`: `ResultHttpExtensions.ToProblemDetails` (ADR 0006).

Forbidden in SharedKernel: module errors, module domain types (a value object becomes shared only when a second module needs it), EF configurations and value converters, inter-module DTOs.

---

## 6. Universal rules

- **Time**: inject `IClock`. Never `DateTime.Now/UtcNow`. Calendar dates (trip execution date, validity ranges) are `DateOnly` in the domain.
- **Money**: `Money` value object, never bare `decimal` for amounts; quantities are `Tons`, `Kilometers`, `Hours`. No `double`, no `float`.
- **Errors**: handlers return `Result<T>`; domain invariants throw `DomainException` from `{Aggregate}Errors`; HTTP mapping by `Error.Type` only.
- **Authorization**: every endpoint declares `.RequireAuthorization(AuthorizationPolicies.X)` with a nominal policy from `Priamus.Trips.Identity.Contracts`. `.AllowAnonymous()` only on login and refresh, explicitly.
- **Data boundary**: no driver-facing endpoint or DTO carries pricing, rates or totals.
- **Async**: `CancellationToken` everywhere; no `.Result`/`.Wait()`.
- **Database**: Fluent configuration only; `.AsNoTracking()` on reads; migrations only, no `EnsureCreated()`; no cross-schema foreign keys; Testcontainers PostgreSQL in tests.
- **PRD fidelity**: every business rule implemented cites its PRD section or requirement (`PRD §5.4`, `RF-08quater`) in the handler or aggregate XML doc. The numeric examples of PRD §12 are mandatory tests of the pricing engine.
- **Sealed types**, `switch` expressions, explicit mapping (no AutoMapper).
- **Mocking**: never mock domain types; only application dependencies (clock, current user, contracts).

---

## 7. Delivery workflow

Each slice (a use case or a small group of use cases, with the PRD sections it implements as brief) goes through: `grilling` with `domain-modeling` (output `.plans/<slice>/SPEC.md`, updates to the module `CONTEXT.md`, ADRs when warranted) → `plan-and-split` → `execute-plan` → `ai-code-review` → `qa-walkthrough` → `strip-plan-refs`. `.plans/` is never committed. What a slice defers on purpose goes to `docs/backlog.md`.

---

## Appendices (load on demand)

| File | When to load |
|---|---|
| `appendices/slices-and-handlers.md` | Implementing or reviewing a vertical slice |
| `appendices/errors-and-results.md` | Result/Error/HTTP mapping |
| `appendices/domain-modeling.md` | Designing or modifying aggregates, value objects, sealed types |
| `appendices/persistence.md` | EF Core configurations, migrations, queries, audit and outbox |
| `appendices/paging.md` | Paginated query slices |
| `appendices/testing.md` | Writing any test |
| `appendices/pricing-engine.md` | Pricing logic, calculation, snapshots, PRD §12 examples |
| `appendices/audit-rules.md` | Architecture conformity review (checklist for `ai-code-review`) |
| `appendices/architectural-invariants.md` | MSBuild-level guards and architectural assertions |
