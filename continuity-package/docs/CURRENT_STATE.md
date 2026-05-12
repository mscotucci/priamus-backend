# Current State

**Last updated**: 2026-05 (after backend scaffolding milestone, before Identity module)

This document is the single-page snapshot of where the project stands. It is updated after each significant milestone. It is intended as the first file an AI assistant or new contributor should read after `docs/context.md`.

---

## Where we are right now

The backend repository is fully scaffolded but contains no domain code yet. The SharedKernel is complete and verified.

**Completed milestones:**

1. **Documentation and agent setup** — `docs/context.md`, all appendices, glossary, and six specialized AI agents (`domain-modeler`, `persistence-engineer`, `feature-implementer`, `test-writer`, `architecture-guardian`, `pricing-engine-specialist`) are in place. Each agent has its model assignment (Opus for domain-modeler and pricing-engine-specialist, Sonnet for the workers, Haiku for the read-only auditor).

2. **Solution skeleton** — 20 projects: SharedKernel, SharedKernel.Http, Host, four module implementations, four module Contracts, nine test projects (eight `*.UnitTests` and `*.IntegrationTests` per module plus `SharedKernel.UnitTests`). All project references follow the rules (modules reference only their own Contracts plus other modules' Contracts; never another module's implementation). The solution is in `.slnx` format.

3. **SharedKernel implementation** — Money, Tons/Kilometers/Hours, Currency (record struct), Result/Error/ErrorType, ResultHttpExtensions (in SharedKernel.Http per ADR 0006), PagedRequest/PagedResponse/SortDirection, IClock/SystemClock, AggregateRoot/IDomainEvent/DomainException, UserId/ICurrentUser. All under appropriate folders. Tests: not yet written.

4. **Architectural guards** — an MSBuild `<Target>` in `Priamus.Trips.SharedKernel.csproj` fails the build if `FrameworkReference Microsoft.AspNetCore.App` is ever added. Documented in `docs/appendices/architectural-invariants.md`.

5. **Audit rules refined** — `B6` in `docs/appendices/audit-rules.md` now catches `MapGet`/`MapPost`/`MapPut`/`MapDelete`/`MapPatch` calls without authorization directives across all `.cs` files, not just `*Endpoint.cs`.

**Build status**: 0 errors, 0 warnings across 20 projects.
**Audit status**: PASS — 0 BLOCKER, 0 MAJOR, 0 MINOR. Two human-review notes addressed.

---

## What is next

**Next milestone: Identity module.**

Scoped for MVP (decisions confirmed):

- `User` aggregate with `UserId`, `Username` (unique), `PasswordHash` (value object), `Role` (enum: Driver, BackOffice, Admin), `Status` (Active, Disabled).
- Password hashing via `Microsoft.AspNetCore.Identity.IPasswordHasher<T>` (Argon2id-equivalent, in-framework, secure).
- `Login` slice: `POST /api/auth/login`, returns access JWT + opaque refresh token.
- `RefreshToken` aggregate (separate from User, see ADR 0005) with hashed token, expiry, and revocation timestamp.
- `RefreshAccessToken` slice: `POST /api/auth/refresh` with rotation (new refresh token issued, old one invalidated).
- `RevokeSession` slice: `POST /api/auth/revoke` for back-office or admin to revoke a specific refresh token.
- JWT claims: `sub` (UserId), `role`, `driver_id` (when role is Driver).
- Token lifetimes: access 1h default, refresh 12h default (configurable via options).
- Audit log via outbox: `UserLoggedIn`, `LoginFailed`, `TokenRefreshed`, `TokenRevoked`.

**Out of scope MVP** (deferred to future ADRs):
- Self-service registration (users created by secretariat/admin)
- Password reset flow
- 2FA
- Lockout after N failed attempts
- "Revoke all sessions for user" as a single operation
- Email verification

**Identifier**: username only for MVP (not email). Confirmed.
**Refresh strategy**: rotation from day one. Confirmed.

---

## Suggested workflow for the Identity milestone

1. **Domain design** — `domain-modeler` agent. Defines `User`, `PasswordHash`, `Role`, `Status`, `RefreshToken`, related Errors classes. Domain code only; no persistence, no endpoints.

2. **Persistence** — `persistence-engineer` agent. EF Core configurations, DbContext, initial migration. Adds the `identity` schema.

3. **Vertical slices** — `feature-implementer` agent. Login, Refresh, Revoke slices in sequence.

4. **Tests** — `test-writer` agent. Unit tests for the domain (≥ 90% coverage), integration tests for the slices via Testcontainers.

5. **Audit** — `architecture-guardian` agent. Full audit of the Identity module before commit.

6. **Build and test** — verify `dotnet build` and `dotnet test` from the repo root.

7. **Commit** — milestone commit. Update this file.

---

## Open questions / known frictions

None blocking. Items to verify when relevant:

- **`Microsoft.Extensions.Validation`** was removed during SharedKernel setup because of NU1510 (the analyzer flagged it as unnecessary on .NET 10). When the first DTO with validation attributes is written, verify that the `[ValidatableType]` attribute and `services.AddValidation()` still resolve from the shared framework. If not, the package must be re-added.

- **JWT signing key management** — for MVP a static HS256 key from configuration is sufficient. Production deployment will need a key rotation strategy; deferred to a future ADR when deployment is in scope.

---

## How to update this file

Update at the end of each significant milestone (a completed module, a major architectural change, a phase transition). Keep it under 150 lines total. Move stale "open questions" into ADRs or into module-specific documentation as they get resolved.

The goal of this file is to answer the question: **"if I had to start working on this project tomorrow with no prior context, what would I need to know first?"** Anything that doesn't serve that question belongs elsewhere.
