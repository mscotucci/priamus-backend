# 0004 — Separate repositories for backend and frontend

## Status

Accepted (2026-05). Supersedes the initial monorepo decision discussed but not committed in the early setup phase.

## Context

Priamus Trips has two user-facing surfaces:
- **Driver PWA** built in Angular 21+, deployed as a Progressive Web App on smartphones and 10-inch tablets.
- **Back-office web app** built in Angular 21+, used by the secretariat on desktops.

The backend is a .NET 10 modular monolith that serves both surfaces via a documented REST API with OpenAPI 3.1 specification.

The initial plan was a single repository containing both `backend/` and `frontend/` subdirectories. After completing the backend scaffolding phase, we revisited the decision.

## Decision

**Two separate Git repositories**:

- `priamus-trips-backend` — this repository. .NET 10 modular monolith.
- `priamus-trips-frontend` — to be created. Angular 21+ workspace, possibly containing both driver app and back-office app.

Cross-repository coordination:

- **Domain glossary**: maintained as `docs/glossary.md` in the backend repository (the source of truth). The frontend repository receives a copy that must be kept in sync manually or via a lightweight script.
- **API contract**: the backend produces an OpenAPI 3.1 specification. The frontend consumes it via code generation (tool to be decided when frontend work starts).
- **Release coordination**: backend and frontend release independently. Breaking changes to the API follow a deprecation period rather than atomic cross-repo changes.

## Consequences

**Positive:**
- CI pipelines are cleanly separated. A backend change does not trigger frontend builds, and vice versa.
- Repository-level permissions and CODEOWNERS work naturally per concern.
- Editor and tooling indexing is faster (no need to skip `node_modules`, no large frontend dependency trees in backend searches).
- Mental separation: opening the backend repo means thinking in C#/.NET terms; opening the frontend repo means thinking in TypeScript/Angular terms.
- The OpenAPI contract becomes an explicit, versioned artifact rather than an implicit detail of monorepo organization.
- Repositories can be released, archived, or transferred independently.

**Negative:**
- Glossary duplication must be managed. Drift between backend and frontend term mappings is now possible (mitigated by the fact that the glossary is a small, stable file).
- Cross-cutting changes that touch both backend and frontend require coordinated PRs in two repositories. This forces the team to design API changes with backwards compatibility in mind.
- A developer working on a full feature (API + UI) must clone two repositories and switch between them.

**Neutral:**
- Architectural decision records (this directory) live in the backend repo. Frontend-specific ADRs will live in the frontend repo. Cross-cutting decisions are referenced from both.

## Alternatives considered

**Monorepo with `backend/` and `frontend/` subdirectories** was initially favored. Rejected because:

- The "shared documentation" benefit is small (only the glossary is genuinely shared; the rest is per-track).
- "Single CI pipeline" turns out to be two pipelines in one YAML file with path filters, which is more complex, not less, than two separate pipelines.
- Backend and frontend operate as separate worlds in practice (different agents, different tooling, different review cadences). Forcing them into one repository creates artificial coupling.
- Long-term, separation is the convention for full-stack systems where backend and frontend are owned by different teams or scale differently.

**Three repositories (backend, frontend, shared-contracts)** was considered for a "contract-first" approach where OpenAPI lives in its own repo. Rejected as overhead for a project of this size. The backend repo can publish OpenAPI as a release artifact.
