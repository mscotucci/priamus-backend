# 0003 — Pricing engine as a pure function with snapshot persistence

## Status

Accepted (2026-05)

## Context

The PRD (sections 5.3, 5.5–5.9, 5.10, 12) defines a non-trivial pricing model:

- Four pricing modes (ByHour, ByWeight, ByDistance, ByTrip)
- Day/night time bands with configurable boundaries
- Minimum rules (None, FixedMinimum, TieredOverage)
- Per-vehicle rate overrides applied as full rate matrices, not partial values
- Rate history: a price change must not retroactively alter trips already invoiced

PRD §10.2 explicitly flags the pricing engine as the highest-risk component: a calculation bug means wrong invoices, lost customer trust, and potential legal exposure.

## Decision

The pricing engine is implemented as a **pure function** with the following properties:

- **No state**: no fields, no mutable globals.
- **No I/O**: does not read from database, does not call other services, does not access time.
- **Same inputs always produce same output**: deterministic and reproducible.
- **Lives in the Registry module**, exposed via `IPricingEngine` interface in `Registry.Contracts`.
- **Inputs**: a `RateMatrix` (already resolved by caller — standard or vehicle override), `TripParameters` (sealed discriminated union over the four modes), and a `TimeBandConfiguration`.
- **Output**: `CalculatedAmount` containing the final `Money`, the `AppliedTimeBand`, an audit trail of `CalculationSteps`, and a human-readable `Explanation` string.

Each `Trip` aggregate persists a **`RateSnapshot`** — an immutable value object containing the full rate matrix as it was at the moment of trip registration. This snapshot is serialized as PostgreSQL `jsonb`. The denormalized `FinalAmount` and `AppliedTimeBand` are stored as scalar columns for query performance.

Test coverage requirement: **100%** on `PricingEngine`, `RateMatrix`, `TimeBand`, `MinimumRule`, `TimeBandConfiguration`. Every numeric example in PRD §12 must have a corresponding test with the exact expected result.

## Consequences

**Positive:**
- The engine is trivially testable: input → output, no setup, no mocking.
- The same engine instance runs both the real calculation (when a trip is saved) and the simulation (when the back office wants to verify a new contract). This guarantees that simulation cannot diverge from production calculation.
- Past trips remain immutable. A rate change made today does not invalidate yesterday's invoices.
- Audit trail in `CalculationSteps` makes back-office disputes ("why does this trip cost this much?") solvable without manual reverse-engineering.

**Negative:**
- The `RateSnapshot` denormalizes data that could in principle be reconstructed from `Rate` aggregates with `EffectiveFrom`/`EffectiveTo`. Storage cost is paid for clarity.
- A dedicated `pricing-engine-specialist` agent is needed because the standards (100% coverage, PRD §12 mandatory tests, no fallback without explicit step) are stricter than elsewhere in the codebase.

**Neutral:**
- The caller (Operations module via Trip aggregate factory) is responsible for resolving which rate matrix applies (standard vs vehicle override) before calling the engine. The engine itself does not query for rates.

## Alternatives considered

**Calculate in a service that queries the database** was rejected because it makes testing painful (every test needs a database or extensive mocking) and creates risk of divergence between "live calculation" and "simulation".

**Cache calculated amounts without snapshot** was rejected because it does not address the retroactive change problem: a rate edit would invalidate the cache and produce different results on the next read.

**Recalculate trips on every read** was rejected because it makes the system fragile to rate-data corruption and slow on large reports.
