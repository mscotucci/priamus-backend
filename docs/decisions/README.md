# Architecture Decision Records

This directory contains the Architecture Decision Records (ADRs) for Priamus Trips backend.

An ADR captures a single significant architectural decision: what was decided, why, what alternatives were considered, and what consequences follow. ADRs are immutable once accepted — if a decision is reversed or refined, a new ADR is added that supersedes the previous one.

## Format

Each ADR follows this structure:

- **Status**: Proposed / Accepted / Superseded by [link] / Deprecated
- **Context**: the situation that prompted the decision
- **Decision**: what was decided
- **Consequences**: positive, negative, and neutral effects
- **Alternatives considered**: other options and why they were rejected

## Index

| #                                                              | Title                                                              | Status   | Date    |
| -------------------------------------------------------------- | ------------------------------------------------------------------ | -------- | ------- |
| [0001](0001-modular-monolith-with-vertical-slice.md)           | Modular monolith with vertical slice                               | Accepted | 2026-05 |
| [0002](0002-result-pattern-for-business-errors.md)             | Result pattern for business errors instead of exceptions           | Accepted | 2026-05 |
| [0003](0003-pricing-engine-as-pure-function.md)                | Pricing engine as a pure function with snapshot persistence        | Accepted | 2026-05 |
| [0004](0004-separate-repositories-for-backend-and-frontend.md) | Separate repositories for backend and frontend                     | Accepted | 2026-05 |
| [0005](0005-jwt-with-refresh-token-rotation.md)                | JWT authentication with refresh token rotation                     | Accepted | 2026-05 |
| [0006](0006-shared-kernel-http-as-separate-project.md)         | SharedKernel.Http as a separate project                            | Accepted | 2026-05 |
| [0007](0007-glossary-sync-strategy.md)                         | Glossary synchronization between backend and frontend repositories | Accepted | 2026-05 |
| [0008](0008-cross-module-identity-driver-linkage.md)           | Cross-module identity-driver linkage and JWT claim enrichment      | Accepted | 2026-05 |
| [0009](0009-no-repository-pattern.md)                          | No repository pattern; handlers depend on DbContext directly       | Accepted | 2026-05 |
| [0010](0010-refresh-claim-enrichment-requery.md)               | Refresh-token claim enrichment via re-query at refresh             | Accepted | 2026-05 |
| [0011](0011-0011-handler-is-commit-point.md)                   | Unit of work: the handler is the commit point                      | Accepted | 2026-05 |

## When to write a new ADR

Write an ADR whenever you make a decision that:

- Affects more than one module
- Restricts future technical choices (e.g. "we will never use library X")
- Has trade-offs significant enough that a future contributor would ask "why did we do it this way?"
- Could be reversed later only with significant cost

Do NOT write an ADR for:

- Routine implementation choices that follow existing patterns
- Internal refactorings that do not change external contracts
- Bug fixes
