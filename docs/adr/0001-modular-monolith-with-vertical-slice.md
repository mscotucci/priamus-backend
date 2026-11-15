# 0001 — Modular monolith with vertical slice

## Status

Accepted (2026-05)

## Context

Priamus Trips is a freight trip management system for a single company (Priamus Ecologica). The expected load is modest: ~50 concurrent drivers, peak ~200 trips per hour, single-tenant deployment.

We needed to choose an overall architecture style for the backend. Three main options were on the table:

1. **Layered monolith** — traditional N-tier (Controllers → Services → Repositories → Database).
2. **Microservices** — separate services for trips, customers, billing, etc.
3. **Modular monolith** — one deployable artifact, but internally organized into modules with strict boundaries.

The team is small (effectively one engineer with AI assistance). The deployment target is a single container with PostgreSQL. There is no operational team to run distributed infrastructure.

## Decision

**Modular monolith** with the following structure:

- Four functional modules: `Identity`, `Registry` (master data + pricing), `Operations` (trips + attachments + issues), `BackOffice` (reporting + exports).
- Each module has its own PostgreSQL schema. No foreign keys cross schemas at the database level.
- Each module exposes a `*.Contracts` project for inter-module communication. Module A may only reference `Module B.Contracts`, never `Module B` implementation.
- Inside each module, code is organized by **vertical slice** (per use case), not by horizontal layer.
- Asynchronous inter-module communication uses an outbox pattern persisted in the database. No external message bus.

## Consequences

**Positive:**
- Single artifact to deploy, debug, and operate. No service mesh, no distributed tracing required for MVP.
- Strict module boundaries make extraction of a module into a separate service feasible later if scale requires it.
- Each schema owned by one module reduces coupling at the data layer.
- Vertical slice keeps related code (DTO, handler, endpoint) physically close, easing changes within a feature.

**Negative:**
- The discipline of "no cross-module implementation references" must be enforced via code review and an `architecture-guardian` agent. Tooling does not enforce it by default.
- Inter-module calls cross a `Contracts` layer, which introduces some interface boilerplate.
- The outbox pattern adds operational complexity (processing service, retry handling) compared to direct in-process events.

**Neutral:**
- Each module has its own DbContext. EF Core configurations and migrations are per-module.

## Alternatives considered

**Layered monolith** was rejected because horizontal layering tends to scatter changes for a single use case across many files (a `RegisterTrip` feature would touch a Controller, a Service, a Validator, a Repository, often in different folders). Vertical slice keeps these grouped.

**Microservices** were rejected as premature. The load is modest, the team is small, there is no operational reason to split. Microservices introduce network failure modes, eventual consistency, distributed tracing, and deployment coordination — all costs we have no reason to pay right now. If a module later needs to scale independently, the modular monolith allows extraction.
