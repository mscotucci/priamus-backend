# Architecture Decision Records

One file per decision, `NNNN-slug.md`, sequential numbering. Short format: context, decision, why, in a few sentences; optional *Considered options* and *Consequences* only when they add value. An ADR is written only when the decision is hard to reverse, surprising without context, and the result of a real trade-off.

ADRs 0001–0011 predate the short format and keep their original long form (status, context, decision, consequences, alternatives). An accepted ADR is not rewritten: a decision that changes gets a new ADR that supersedes the old one.

| # | Title | Status |
|---|---|---|
| [0001](0001-modular-monolith-with-vertical-slice.md) | Modular monolith with vertical slice | Accepted |
| [0002](0002-result-pattern-for-business-errors.md) | Result pattern for business errors instead of exceptions | Accepted |
| [0003](0003-pricing-engine-as-pure-function.md) | Pricing engine as a pure function with snapshot persistence | Accepted |
| [0004](0004-separate-repositories-for-backend-and-frontend.md) | Separate repositories for backend and frontend | Accepted |
| [0005](0005-jwt-with-refresh-token-rotation.md) | JWT authentication with refresh token rotation | Accepted |
| [0006](0006-shared-kernel-http-as-separate-project.md) | SharedKernel.Http as a separate project | Accepted |
| [0007](0007-glossary-sync-strategy.md) | Glossary synchronization between backend and frontend repositories | Accepted |
| [0008](0008-cross-module-identity-driver-linkage.md) | Cross-module identity-driver linkage and JWT claim enrichment | Accepted |
| [0009](0009-no-repository-pattern.md) | No repository pattern; handlers depend on DbContext directly | Accepted |
| [0010](0010-refresh-claim-enrichment-requery.md) | Refresh-token claim enrichment via re-query at refresh | Accepted |
| [0011](0011-handler-is-commit-point.md) | Unit of work: the handler is the commit point | Accepted |
| [0012](0012-slices-before-shared-persistence.md) | Slices come before shared persistence | Accepted |
| [0013](0013-registry-reads-identity-users.md) | Registry reads Identity users through a lookup in Identity.Contracts | Accepted |
| [0014](0014-one-location-registry-without-owner.md) | One location registry, without an owner customer | Accepted |
