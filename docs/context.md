# Priamus Trips Backend — Context

Technical context for the Priamus Trips backend. This is the only repository that hosts backend code; the frontend lives in a separate repository and is consumed via the OpenAPI contract produced by the ASP.NET Core endpoints.

---

## 1. Product overview

Priamus Trips is the digital management and monitoring system for freight trips operated by Priamus Ecologica S.r.l.

Two user-facing surfaces consume this backend:
- **Driver PWA** (smartphone / 10-inch tablet, BYOD): drivers record their trips during or at the end of the day, with attachments and end-of-day summaries.
- **Back-office web app** (desktop): the secretariat manages master data (customers, contracts, rates, vehicles, drivers), reviews trips, resolves anomalies, exports invoicing data.

**Strict data-boundary rule**: drivers must NEVER see pricing, tariffs, rates, or calculated totals. Financial logic lives server-side and is surfaced only on the back-office endpoints. Every endpoint and DTO contract must respect this boundary.

### Language

- **Code** (identifiers, comments, logs, exception/error messages, file and folder names, namespaces, test names, README, ADR, commit messages): **English, always.**
- **End-user content** (validation messages shown in the UI, generated documents, emails): **Italian, in `.resx` localization files.**

The shared glossary (`docs/glossary.md`) maps Italian domain terms from the PRD to English code identifiers. Always consult it before introducing new domain identifiers.

### Logical modules (system-wide)

| Module       | Backend responsibility                                            | Frontend surface           |
| ------------ | ----------------------------------------------------------------- | -------------------------- |
| `Identity`   | Authentication, sessions, roles                                   | Login, session management  |
| `Registry`   | Master data + pricing engine                                      | Back-office only           |
| `Operations` | Trips, attachments, daily summaries                               | Driver PWA + back-office   |
| `BackOffice` | Reporting, exports                                                | Back-office only           |

### Out of scope (MVP)

Explicitly NOT to implement; require re-discussion if requested:

Offline mode, push notifications, GPS tracking, OCR of documents, billing system integration, end-customer app, advanced analytics, occasional rented vehicles not in registry, vehicle categorization with category-level pricing, trailer-differentiated pricing, recipient digital signature, automatic invoice generation.

---

## 2. Stack (non-negotiable)

- **.NET 10 LTS**, C# 14, nullable enabled, `TreatWarningsAsErrors=true`, implicit usings.
- **PostgreSQL 16+** with **EF Core 10** (`Npgsql.EntityFrameworkCore.PostgreSQL`).
- **ASP.NET Core 10 Minimal API** (no MVC controllers).
- **ProblemDetails** as the error response format. JWT bearer auth, stateless.
- **Native OpenAPI** (`Microsoft.AspNetCore.OpenApi`), no Swashbuckle.

### Approved libraries

| Library                                         | Version | Purpose             |
| ----------------------------------------------- | ------- | ------------------- |
| `Npgsql.EntityFrameworkCore.PostgreSQL`         | 10.x    | DB provider         |
| `Microsoft.Extensions.Validation`               | 10.x    | DTO validation      |
| `Scrutor`                                       | 7.x     | Handler discovery   |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.x    | Auth                |
| `Serilog.AspNetCore`                            | 10.x    | Structured logging  |
| `xUnit`, `Shouldly`, `NSubstitute`              | latest  | Test stack          |
| `Testcontainers.PostgreSql`                     | 4.x+    | Integration test DB |

### Forbidden libraries

MediatR, FluentValidation, AutoMapper, MassTransit, any commercial-license library.

---

## 3. Architecture

- **Modular monolith** with vertical slice inside each module.
- **DDD tactical patterns**: aggregate roots, value objects, domain events.
- **CQRS light**: commands through handlers; queries bypass aggregates and project from DbContext directly.

### Modules

| Module       | Responsibility                                                                              |
| ------------ | ------------------------------------------------------------------------------------------- |
| `Identity`   | Authentication, sessions, roles, password hashing                                           |
| `Registry`   | Customers, contracts, rates (incl. overrides), vehicles, drivers, locations, pricing engine |
| `Operations` | Trips (+ attachments and issues as child entities), daily summaries                         |
| `BackOffice` | Reporting, filterable trip list, Excel/PDF export                                           |

### Inter-module communication

- **Sync**: in-process via interfaces in `*.Contracts` projects.
- **Async**: outbox pattern persisted in DB. No external message bus in MVP.
- **Schema ownership**: each module owns its PostgreSQL schema. No cross-schema FKs at the DB level.
- **References**: a module project may only reference `Priamus.Trips.{X}.Contracts`, never `Priamus.Trips.{X}` implementation. Module implementation projects (`Priamus.Trips.{Module}`) reference both `Priamus.Trips.SharedKernel` and `Priamus.Trips.SharedKernel.Http`. Contracts projects (`Priamus.Trips.{Module}.Contracts`) reference only `Priamus.Trips.SharedKernel` — they must not pull in the ASP.NET Core bridge.

---

## 4. Solution structure (repository root)

```
priamus-trips/
├── Priamus.Trips.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── src/
│   ├── Priamus.Trips.Host/
│   ├── Priamus.Trips.SharedKernel/
│   ├── Priamus.Trips.SharedKernel.Http/
│   └── Modules/
│       ├── Identity/
│       │   ├── Priamus.Trips.Identity/
│       │   └── Priamus.Trips.Identity.Contracts/
│       ├── Registry/{Priamus.Trips.Registry, Priamus.Trips.Registry.Contracts}/
│       ├── Operations/{Priamus.Trips.Operations, Priamus.Trips.Operations.Contracts}/
│       └── BackOffice/{Priamus.Trips.BackOffice, Priamus.Trips.BackOffice.Contracts}/
└── tests/
    └── Priamus.Trips.{Module}.{UnitTests|IntegrationTests}/
```

### Module internal layout

```
Priamus.Trips.{Module}/
├── Domain/{Aggregate}/{Aggregate,*Errors,ValueObjects,Events}.cs
├── Features/{UseCase}/{Request,Response,Handler,Endpoint}.cs
├── Persistence/{DbContext,Configurations,Converters,Repositories,Outbox,Migrations}/
├── Application/{Abstractions,Services}/
├── Infrastructure/
└── DependencyInjection/{Module}Module{Extensions,Endpoints}.cs
```

---

## 5. SharedKernel — strict scope

### `Priamus.Trips.SharedKernel` (framework-agnostic)

Authorized content only:

- **Money / quantities**: `Money`, `Tons`, `Kilometers`, `Hours`
- **Results**: `Result`, `Result<T>`, `Error`, `ErrorType`
- **Paging**: `PagedRequest`, `PagedResponse<T>`, `SortDirection`
- **Time**: `IClock`, `SystemClock`
- **Domain base**: `AggregateRoot`, `IDomainEvent`, `DomainException`
- **User context**: `ICurrentUser`

This project has no `FrameworkReference` and no dependency on ASP.NET Core, so it can be consumed by non-web hosts (workers, batch jobs) without dragging in the web framework.

### `Priamus.Trips.SharedKernel.Http` (ASP.NET Core bridge)

- Declares `<FrameworkReference Include="Microsoft.AspNetCore.App" />`.
- Contains exactly one type: `ResultHttpExtensions` with the `ToProblemDetails(this Error)` extension method that maps `Error.Type` to a ProblemDetails `IResult`.
- References `Priamus.Trips.SharedKernel`.
- This separation exists so non-web consumers (workers, batch jobs) can use Result/Error without taking ASP.NET Core as a transitive dependency.

### Forbidden in SharedKernel

- Module-specific errors (live in `*Errors` classes in their module)
- Module-specific domain types
- EF Core configurations, repositories
- Framework setup, HTTP mapping specific to modules
- Inter-module DTOs (those go in `*.Contracts`)

---

## 6. Universal backend rules (memorize)

These apply across all backend code. No exceptions without explicit documentation.

### Time

- **Always inject `IClock`.** Never `DateTime.UtcNow`, `DateTime.Now`, `DateTimeOffset.Now`. Only exception: `SystemClock.cs` implementation.

### Money

- **Always `Money` value object** for monetary amounts. Never bare `decimal`, never `double` or `float`.

### Errors and Results

- **Handlers return `Result<T>`**, never throw for business errors.
- **Domain invariant violations** throw `DomainException(error)` where `error` comes from a `{Aggregate}Errors` class.
- **Module-specific errors live in `{Aggregate}Errors` classes inside the owning module**, never in SharedKernel.
- **HTTP mapping is driven by `Error.Type` enum**, never by `Error.Code` strings.

### Authorization

- **Every endpoint declares `.RequireAuthorization(...)`** with a specific policy. No exceptions.

### Async

- **Always async/await** for I/O. **`CancellationToken`** propagated everywhere. **No `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`** ever.

### Database

- **No data annotations on domain entities.** Only Fluent Configuration in `Persistence/Configurations/`.
- **No FKs cross-schema.** Inter-module references are bare `Guid`.
- **No `EnsureCreated()`.** Always `MigrateAsync()`.
- **No in-memory database in tests.** Testcontainers PostgreSQL.

### Sealed types and pattern matching

- **Use sealed records** for discriminated unions. Static factory on the abstract base for construction with invariants.
- **Pattern match with `switch` expressions**, never `if/else` on type. Add `_ => throw new UnreachableException(...)` when exhaustiveness is not provable.

### Validation layering

- **DTO structural validation** → DataAnnotations + `IValidatableObject` on the request record. Handled automatically by `Microsoft.Extensions.Validation`.
- **Domain invariants** → constructors of value objects, throwing `DomainException`.
- **Reference existence and cross-aggregate rules** → application services (e.g. context factories).

### Mocking

- **Never mock domain types** (aggregate roots, value objects, sealed types). Only mock application-level dependencies (repositories, factories, clock, current user).

---

## 7. Trip aggregate — canonical reference

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

---

## 8. Pricing engine — risk note

The pricing engine is the single highest-risk component (PRD §10.2). It lives in module `Registry`, exposed via `IPricingEngine` interface in `Registry.Contracts`. It is a **pure function**: same inputs → same output, no side effects, no I/O.

All test cases in PRD §12 (Appendix examples) must have corresponding numeric test assertions. Coverage target on pricing types: 100%.

Detailed algorithm and edge cases are in `appendices/pricing-engine.md`.

---

## 9. Operational constraints

- **Trunk-based development**, feature branches < 3 days, PR required.
- **Documentation tracks code**: changes to architectural rules update this document and the relevant appendices BEFORE the code change is merged.
- **CI gates**: build + unit + integration + analyzer + security scan + Italian-pattern check.
- **Single deploy artifact** (`Priamus.Trips.Host`), Docker.
- **Migrations applied pre-startup** via dedicated job in production.

---

## Appendices (load on demand)

These files are referenced by backend agents only when the task requires their content. Do not load preemptively.

| File                                     | When to load                                                     | Used by                                   |
| ---------------------------------------- | ---------------------------------------------------------------- | ----------------------------------------- |
| `appendices/domain-modeling.md`          | Designing or modifying aggregates, VOs, sealed types             | `domain-modeler`                          |
| `appendices/persistence.md`              | EF Core configurations, repositories, migrations, snapshots      | `persistence-engineer`                    |
| `appendices/slices-and-handlers.md`      | Implementing or reviewing vertical slices                        | `feature-implementer`                     |
| `appendices/errors-and-results.md`       | Working with `Result`, `Error`, `*Errors`, HTTP mapping          | `feature-implementer`, `domain-modeler`   |
| `appendices/paging.md`                   | Paginated query slices                                           | `feature-implementer`                     |
| `appendices/testing.md`                  | Writing tests of any kind                                        | `test-writer`                             |
| `appendices/pricing-engine.md`           | Pricing logic, calculation, snapshots, PRD §12 examples          | `pricing-engine-specialist`               |
| `appendices/audit-rules.md`              | Architecture conformity verification                             | `architecture-guardian`                   |
| `appendices/architectural-invariants.md` | When working on MSBuild-level guards or architectural assertions | `architecture-guardian`, `domain-modeler` |

**Rule for agents**: read the relevant appendix(es) at the start of the task. Do not re-read on subsequent invocations within the same conversation if the content was already loaded.

The glossary (`docs/glossary.md`) is read whenever domain terms are involved, regardless of the task type.
