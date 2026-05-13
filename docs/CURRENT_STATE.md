# Current State

**Last updated**: 2026-05 (after Identity module milestone, before feature-implementer for auth slices)

This document is the single-page snapshot of where the project stands. It is updated after each significant milestone. It is intended as the first file an AI assistant or new contributor should read after `docs/context.md`.

---

## Where we are right now

The Identity module is complete at the domain and persistence layers, with full test coverage. No HTTP-facing slices yet — that's the next milestone.

**Completed milestones:**

1. **Documentation and agent setup** — `docs/context.md`, all appendices, glossary, and six specialized AI agents (`domain-modeler`, `persistence-engineer`, `feature-implementer`, `test-writer`, `architecture-guardian`, `pricing-engine-specialist`) are in place.

2. **Solution skeleton** — 20 projects across SharedKernel, SharedKernel.Http, Host, four module implementations, four module Contracts, and nine test projects. Project references follow the rules; the solution is in `.slnx` format.

3. **SharedKernel implementation** — Money, Tons/Kilometers/Hours, Currency, Result/Error/ErrorType, ResultHttpExtensions (in SharedKernel.Http per ADR 0006), PagedRequest/PagedResponse/SortDirection, IClock/SystemClock, AggregateRoot/IDomainEvent/DomainException, UserId/ICurrentUser. MSBuild guard prevents accidental ASP.NET Core dependency in the framework-agnostic kernel.

4. **Audit rules refined** — `B6` in `docs/appendices/audit-rules.md` catches `MapGet`/`MapPost`/`MapPut`/`MapDelete`/`MapPatch` calls without authorization across all `.cs` files.

5. **Identity module — domain layer** — `User` aggregate (Username VO, PasswordHash VO, UserRole, UserStatus, audit fields, Version), `RefreshToken` aggregate as separate root with rotation chain (`ReplacedByTokenId` for future theft detection), value objects with their validation rules, errors classes, domain events. No reference to the Driver concept in Identity per ADR 0008.

6. **Identity module — persistence layer** — `IdentityDbContext`, EF Core configurations, value converters, `AuditInterceptor` writing audit rows synchronously in the same transaction as aggregate state changes, initial migration creating the `identity` schema with `users`, `refresh_tokens`, and `audit_log` tables. No repository abstractions per ADR 0009; handlers will inject `DbContext` directly.

7. **Identity module — test suite** — Unit tests for aggregates, value objects, and domain events; integration tests for persistence round-trips, concurrency, unique constraints, and audit interceptor behavior. Multi-scope test pattern for true round-trip verification. Domain coverage 93.3% line, 100% branch. Idempotency bug in `User.Disable`/`Enable` discovered during testing and fixed.

8. **New ADRs and convention updates**:
    - ADR 0008: cross-module identity-driver linkage and JWT claim enrichment via `IDriverLookup` from `Registry.Contracts`.
    - ADR 0009: no repository pattern; handlers depend on DbContext directly.
    - `persistence.md` updated: data access without repositories, audit log as default domain event persistence (synchronous via interceptor), outbox as opt-in for cross-module communication only.
    - `test-writer.md` updated: multi-scope persistence test pattern, audit log testing requirements.

**Build status**: 0 errors, 0 warnings across all projects.
**Test status**: all unit and integration tests passing; domain coverage 93.3% line / 100% branch.
**Audit status**: PASS — 0 BLOCKER, 0 MAJOR, 0 MINOR on the Identity module.

---

## What is next

**Next milestone: Identity feature slices (Login, Refresh, Revoke).**

Three vertical slices to implement, per ADR 0005 and ADR 0008:

- **`POST /api/auth/login`** — authenticates by username + password, returns access JWT + opaque refresh token. The JWT carries `sub` (UserId), `role`, and — when the role is `Driver` — `driver_id` populated via `IDriverLookup` from `Registry.Contracts`. Audit events: `UserLoggedIn`, `LoginFailed`.
- **`POST /api/auth/refresh`** — accepts a refresh token, issues a new access token AND rotates the refresh token. The old refresh token is marked rotated (`ReplacedByTokenId` set, `RevokedAt` stamped). Audit event: `TokenRefreshed`. The `driver_id` claim is copied from the previous token, not re-queried (per ADR 0008).
- **`POST /api/auth/revoke`** — revokes a specific refresh token. Available to the token's owner (logout) and to back-office/admin. Audit event: `TokenRevoked`.

Dependencies:

- `IPasswordHasher<User>` from `Microsoft.AspNetCore.Identity.Core`, registered in DI by the feature layer (not by the persistence layer).
- `IDriverLookup` from `Registry.Contracts` — the interface doesn't exist yet because Registry isn't built. For the MVP slice work, register a temporary stub in `Host` that returns `null`, with a TODO referencing ADR 0008.
- JWT signing: HS256 with a static key from configuration (production key rotation strategy deferred to a future ADR).

Token lifetimes: access 1h default, refresh 12h default; both configurable via options.

**Out of scope for this milestone** (deferred to future work):

- Self-service registration, password reset, 2FA, lockout after N failed attempts, "revoke all sessions" as a single operation, email verification (all confirmed out of scope by ADR 0005).
- The real `IDriverLookup` implementation (will arrive with the Registry module).
- JWT signing key rotation strategy.

## Suggested workflow for the next milestone

1. **Domain extensions if needed** — `domain-modeler`. Likely minimal: domain events like `UserLoggedIn`, `LoginFailed`, `TokenRevoked` may need adjusting. Some operations (e.g. recording a successful login on the User aggregate) may benefit from new methods. Decide case by case.

2. **Feature slices** — `feature-implementer`. Three slices in order: Login, Refresh, Revoke. Each slice: Request/Response/Handler/Endpoint plus DI registration. The handler injects `IdentityDbContext` directly (per ADR 0009).

3. **`IDriverLookup` stub in Host** — `feature-implementer`. Temporary in-process implementation that returns `null`. Replaced when Registry arrives.

4. **JWT infrastructure** — `feature-implementer`. JWT issuance with claims as specified, configured options for lifetimes and signing key, JwtBearer authentication on the Host pipeline.

5. **Tests** — `test-writer`. Unit tests for handlers (happy path + error paths), integration tests for the three endpoints end-to-end via Testcontainers.

6. **Audit** — `architecture-guardian`. Full audit before commit, with particular attention to B6 (every endpoint must declare an authorization stance).

7. **Build, test, commit, update this file.**

---

## Open questions / known frictions

- **`Microsoft.Extensions.Validation`** was removed from SharedKernel due to NU1510 during the initial setup. The Identity feature slices will be the first DTOs with validation attributes. Verify at that point that `[ValidatableType]` and `services.AddValidation()` resolve from the shared framework; if not, the package must be re-added.

- **JWT signing key management** — for MVP a static HS256 key from configuration is sufficient. Production deployment will require a rotation strategy; deferred to a future ADR.

- **`Microsoft.AspNetCore.Identity.Core` consumption** — Identity uses `IPasswordHasher<User>` from this package without adopting the full `AddIdentity()` framework (no `IdentityUser`, no `UserManager`, no `SignInManager`). The package will be added in the feature-implementer milestone where the hasher is first injected.

---

## How to update this file

Update at the end of each significant milestone (a completed module, a major architectural change, a phase transition). Keep it under 150 lines total. Move stale "open questions" into ADRs or into module-specific documentation as they get resolved.

The goal of this file is to answer the question: **"if I had to start working on this project tomorrow with no prior context, what would I need to know first?"** Anything that doesn't serve that question belongs elsewhere.
