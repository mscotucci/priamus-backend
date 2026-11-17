# 0010 — Refresh-token claim enrichment via re-query at refresh

## Status

Accepted (2026-05)

Amends [ADR 0008](0008-cross-module-identity-driver-linkage.md) on one specific point. Does not supersede it.

## Context

ADR 0008 prescribes that the `driver_id` JWT claim is populated at login via `IDriverLookup`, and that subsequent refreshes do not re-execute the lookup but instead **copy the claim from the previous token**:

> _"Token refresh does not re-execute the lookup. When refreshing the access token, the `driver_id` claim is read from the previous token and copied into the new one."_

When the Identity feature slices (Login, Refresh, Revoke) reached implementation, this prescription surfaced a specification gap: the refresh token is an **opaque** value ([ADR 0005](0005-jwt-with-refresh-token-rotation.md)), it carries no claims. The "previous token" from which the claim should be read does not exist in the form ADR 0008 implicitly assumed.

Three remediation options were considered to close the gap:

1. **Add `DriverId?` field on the `RefreshToken` aggregate**, populated at login from the lookup result, copied to the rotated token at refresh.
2. **Add `ClaimsSnapshot` jsonb field on the `RefreshToken` aggregate**, with the same lifecycle as (1) but extensible to multiple enriched claims.
3. **Re-execute `IDriverLookup` at refresh**, contradicting the letter of ADR 0008 but preserving its spirit.

(1) and (2) introduce schema bleed in the Identity module: the `identity.refresh_tokens` table would carry a reference (typed or opaque) to a concept owned by Registry. The first reason given in ADR 0008 §Context for rejecting `DriverId` on the `User` aggregate ("wrong direction of coupling") applies equally — and arguably more strongly — to `RefreshToken`, which is even further from the domain meaning of "this principal is associated with a Driver".

(3) was the originally-rejected option in ADR 0008. The rationale for rejection there was:

> _"It defeats the stateless-JWT design of ADR 0005, adds a database hit per authenticated request, and provides no compensating benefit."_

That reasoning conflates two different operations: **per-request authentication** (validating an access JWT on every API call — must remain stateless and lookup-free) and **token refresh** (a deliberate database operation that already reads `RefreshToken`, reads `User`, writes a rotated `RefreshToken`, and writes audit rows). Adding one indexed `SELECT` to a refresh operation is qualitatively different from adding one to every API call.

## Decision

**`IDriverLookup` is re-executed at refresh time. The `RefreshToken` aggregate does not persist any enriched claim.**

Concretely:

- At login, the Identity Login slice calls `IDriverLookup.GetDriverIdByUserIdAsync(userId, ct)`. If the role is `Driver` and a `DriverId` is returned, the JWT carries the `driver_id` claim; otherwise it does not. This part is unchanged from ADR 0008.
- At refresh, the Identity Refresh slice — after validating the refresh token and loading the owning `User` — calls `IDriverLookup` **again** with the same `userId`. The new JWT carries the `driver_id` claim under the same rules as at login.
- The `RefreshToken` aggregate is **not modified**. No `DriverId` field, no `ClaimsSnapshot`, no jsonb column. Identity's persistence schema remains free of Registry-owned concepts.

## Consequences

**Positive:**

- The Identity module schema remains pure: no field on any Identity table refers, even opaquely, to a Registry concept.
- The `RefreshToken` aggregate stays focused on its actual responsibility (authentication session lifecycle) without absorbing claim-enrichment state.
- Stale-claim scenarios disappear at refresh: if a Driver↔User link changes between login and refresh, the refresh produces a JWT with the up-to-date value. ADR 0008 §Consequences listed mid-session staleness as a known limitation; this ADR removes it for the refresh boundary specifically. (Within a single access-token lifetime — typically 1 hour per ADR 0005 — staleness can still exist, since the access JWT is the source of truth between refreshes. This is unchanged and acceptable.)
- The Refresh slice has the same external dependency as the Login slice (`IDriverLookup`). Test surface and stub strategy are identical for both, reducing cognitive load.

**Negative:**

- One additional `SELECT` against `registry.drivers` per refresh operation. With the default 12-hour refresh window of ADR 0005, this is 1–2 queries per driver per day on an indexed column. Invisible to production load.
- The contract of `IDriverLookup` becomes "called at login AND at refresh" rather than "called at login only". Implementations of `IDriverLookup` must be idempotent and side-effect-free with respect to repeated lookups for the same `UserId`. The current stub (returning `null`) and the future Registry implementation (a read query) both satisfy this trivially. Documenting it explicitly avoids accidental future implementations that, say, count lookups for analytics.

**Neutral:**

- The reverse implication — "if we ever need a second enriched claim (e.g. `office_branch_id` per the speculative example in ADR 0008 §Consequences), do we re-query each at refresh?" — is answered uniformly: yes, each enrichment runs at login AND at refresh, each through its own lookup interface in the appropriate `*.Contracts` project. The cost is linear in the number of enriched claims, which the MVP shape (one such claim) keeps trivially small.

## What this ADR does NOT change

- Per-request authentication remains stateless: validating an access JWT on every API call does not touch the database. The `driver_id` claim is read directly from the JWT.
- ADR 0008's choice of putting the User↔Driver link on the `Driver` aggregate in Registry stands.
- ADR 0008's overall pattern ("Identity asks Registry via a Contracts interface to enrich a claim at login") stands. The only change is "at login" → "at login AND at refresh".

## Alternatives considered

**`DriverId?` field on `RefreshToken` (option 1 above).** Rejected because it places a Registry-owned concept on an Identity aggregate, repeating the same coupling mistake ADR 0008 explicitly identified and rejected for the `User` aggregate. The argument "but it's only a `Guid?` field, not a navigation property" is the same argument that was already weighed and rejected for `User.DriverId`; the same answer applies.

**`ClaimsSnapshot` jsonb on `RefreshToken` (option 2 above).** Rejected for the same reason as (1), with additional cost: a freeform blob is harder to evolve safely than a typed field, and the precedent it sets ("Identity can hold opaque snapshots of other modules' state") would be a slippery slope for the modular monolith boundary. The `RateSnapshot` pattern on `Trip` (PRD §5.9) does not apply here: `Trip` is the _consumer_ of the priced quantity that needs the snapshot for historical immutability; `RefreshToken` is a pass-through, not a consumer.

**Status quo per ADR 0008 (copy claim from previous token).** Not actually implementable as written, because the refresh token carries no claims. Any "implementation" of this option requires choosing (1) or (2) first to give the previous token a claim to copy, which collapses back into the rejected options.

## Implementation notes

- The Refresh slice depends on `IDriverLookup` in `Registry.Contracts`, identically to the Login slice.
- The temporary stub in `Host` (returning `null` until the Registry module exists) serves both Login and Refresh with no changes.
- Tests should cover the case "user logs in as Driver while link is absent (claim missing), then link is established, then user refreshes" — the refresh should produce a JWT with the now-populated `driver_id`. This is a positive consequence of this ADR worth pinning with a test.
- Refresh writes no audit row, so nothing needs to record the `driver_id` value. The audit story for the claim lives in the JWT issuance log line.
