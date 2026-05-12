# 0008 — Cross-module identity-driver linkage and JWT claim enrichment

## Status

Accepted (2026-05)

Complements [ADR 0005](0005-jwt-with-refresh-token-rotation.md). Does not supersede it.

## Context

ADR 0005 prescribes that the JWT issued at login carries three claims: `sub` (the `UserId`), `role`, and `driver_id` when the role is `Driver`. The mechanism by which `driver_id` becomes available to the Identity module was not specified, because at the time the User aggregate had not been designed yet.

During the implementation of the Identity domain layer (modeling milestone), the first proposal was to put `DriverId` as a field directly on the `User` aggregate, with an invariant `(Role == Driver) ⟺ (DriverId.HasValue)`. This was rejected during review for three reasons:

1. **Wrong direction of coupling.** Identity is concerned with authentication — who you are, how you prove it, what role you hold. The notion of "Driver" as an operational entity of the fleet (with plates, contracts, trips) belongs to the Registry module. Modeling `DriverId` on `User` would mean Identity knows about a concept that is owned elsewhere.

2. **The invariant is only syntactically local.** Identity can check that a field is non-null, but cannot guarantee that the `Guid` points to a real Driver in Registry, nor observe lifecycle changes of that Driver (e.g. dismissal). The invariant is semantically cross-module and would be illusory if enforced only inside Identity.

3. **Schema bleed.** With `DriverId` on `users`, the `identity` PostgreSQL schema would contain a logical reference into the `registry` schema. The "no cross-schema FK" rule (`context.md` §3) would be technically respected, but the modular boundary would be compromised at the conceptual level.

A decision is therefore needed on (a) where the User↔Driver link lives, and (b) how the `driver_id` claim is populated at login time.

## Decision

**1. The link User↔Driver lives in Registry, not in Identity.**

The `Driver` aggregate in Registry will own a `UserId? UserId` field (nullable, to admit historical drivers without accounts and drivers entered in the registry before the corresponding account is provisioned). `User` in Identity has no knowledge of the Driver concept.

**2. The JWT `driver_id` claim is populated at login via a lookup interface exposed by Registry.**

A new interface `IDriverLookup` is declared in `Priamus.Trips.Registry.Contracts`:

```csharp
public interface IDriverLookup
{
    Task<Guid?> GetDriverIdByUserIdAsync(UserId userId, CancellationToken cancellationToken);
}
```

The Identity Login slice depends on `IDriverLookup`. After authenticating the user, if the role is `Driver`, the slice calls the lookup and — if a DriverId is returned — adds it to the JWT as the `driver_id` claim. If the role is `Driver` but no DriverId is returned (account exists but not yet linked to a Driver record), the slice logs a warning and issues the JWT without the claim. Downstream endpoints requiring `driver_id` will reject such tokens with `403 Forbidden`.

**3. Token refresh does not re-execute the lookup.**

When refreshing the access token, the `driver_id` claim is read from the previous token and copied into the new one. The User↔Driver link is considered stable for the duration of a session. This is a deliberate performance trade-off; see Consequences.

**4. The Identity module gains a dependency on `Priamus.Trips.Registry.Contracts`.**

This is the only inter-module dependency introduced by this ADR. It is a dependency on the abstraction (Contracts project), not on the implementation, consistent with the project reference rules in `context.md` §3.

## Consequences

**Positive:**
- The Identity domain remains pure. The `User` aggregate has no knowledge of any module-specific concept beyond its own role enumeration.
- The real invariant — *"a Driver has at most one account"* — is enforced where the context exists to enforce it (Registry, which knows whether a Driver is active or dismissed and can refuse re-assignment).
- The pattern is extensible. If in the future BackOffice users need their own enrichment claim (e.g. `office_branch_id`), the same shape applies: a `BackOfficeLookup` interface in the appropriate Contracts project, consumed by the Login slice.
- The JWT remains the single source of truth for authorization decisions on subsequent requests. No request-time database lookup for routine API calls.

**Negative:**
- Identity is no longer fully standalone: a login flow without a working Registry module cannot populate `driver_id`. In practice, both modules deploy together as part of the modular monolith, so this is a development-time concern only.
- If the User↔Driver link changes mid-session (a Driver is re-assigned to a different account, or a previously unlinked account is linked after the user already logged in), the claim on the active JWT will be stale until the access token expires or the user re-authenticates. The MVP does not support these re-linking scenarios, so the staleness window is theoretical for now.
- The Login slice now has an external dependency (`IDriverLookup`) that must be available in tests. This will require a fake or stub in Identity integration tests.

**Neutral:**
- Refresh deliberately does not re-execute the lookup. If we ever need to invalidate the claim mid-session, we will revisit. The natural extension would be to revoke the refresh token (already supported per ADR 0005), forcing a fresh login that re-runs the lookup.

## Out of scope for MVP

- **Claim invalidation on link change.** If a Driver is re-assigned to a different `UserId`, currently-active JWTs will continue to carry the stale `driver_id` until expiry. Acceptable because the MVP does not expose any operation that changes the link mid-session.
- **Caching of `IDriverLookup` results inside Identity.** Login is an infrequent operation (1–2 times per day per driver per ADR 0005's 12-hour refresh window). Premature optimization is not warranted; revisit only if telemetry shows the lookup is a bottleneck.
- **Reverse direction (Identity exposing a `UserLookup` to Registry).** Registry currently has no need to look up Identity state. If a future use case emerges (e.g. "list drivers with disabled accounts"), it will be addressed in a new ADR.

## Alternatives considered

**`DriverId` field on the `User` aggregate (the original proposal).** Rejected for the three reasons in Context: wrong coupling direction, illusory invariant, schema bleed. The convenience of having a single self-contained aggregate did not outweigh the modular cost.

**Token enrichment in a separate post-Identity layer.** A pipeline where Identity issues a "base" token and a downstream component (in `Host` or in a dedicated `Auth` module) enriches it with `driver_id`. More decoupled in theory: Identity stays completely standalone. Rejected because (a) it places the enrichment logic in an unnatural location with no obvious owner, (b) the login flow becomes a two-step process that is harder to reason about and test end-to-end, (c) the marginal modularity benefit is small once we accept that Login is conceptually an orchestration of "authenticate + enrich claims".

**No `driver_id` claim on the JWT; lookup per request.** Every authenticated request from a Driver would resolve `UserId → DriverId` against Registry. Rejected because it defeats the stateless-JWT design of ADR 0005, adds a database hit per authenticated request, and provides no compensating benefit (the link is stable; the lookup result would be identical for the entire session).

## Implementation notes

- `IDriverLookup` is defined in `Registry.Contracts` and implemented in the `Registry` module proper. Until the Registry module is built, the Identity Login slice can be developed against a temporary stub registered in `Host`, with a TODO referencing this ADR.
- The Identity → Registry.Contracts dependency must appear in `Priamus.Trips.Identity.csproj`. It is the first inter-module dependency in the codebase; the architectural guards in `appendices/architectural-invariants.md` should be reviewed to ensure they tolerate this allowed reference shape.
- The fallback behavior (role is `Driver` but lookup returns `null`) must be covered by a dedicated integration test, because it is a likely failure mode during driver onboarding.
