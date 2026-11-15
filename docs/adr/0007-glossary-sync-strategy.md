# 0007 — Glossary synchronization between backend and frontend repositories

## Status

Accepted (2026-05)

## Context

ADR 0004 split the project into two separate Git repositories: `priamus-trips-backend` (this one) and `priamus-trips-frontend` (to be created). The domain glossary, which maps Italian PRD terms to English code identifiers, is genuinely shared: both backend and frontend must use the same translations to avoid integration bugs.

The glossary lives in `docs/glossary.md` of the backend repository. The question: how does the frontend repository stay aligned?

Four options were considered:

1. **Manual copy** — backend is canonical; the frontend repo holds a copy that is updated manually whenever the backend's glossary changes.
2. **Git submodule or subtree** — the glossary lives in a third repository or as a sub-tree, included by both backend and frontend via Git mechanics.
3. **Generated package** — the backend publishes the glossary as part of an OpenAPI artifact or a GitHub Release; the frontend downloads it.
4. **Duplicated with CI check** — same as option 1, but a daily CI job compares the two files and fails if they diverge.

## Decision

**Manual copy for MVP** (option 1).

The backend repository is the canonical source of the glossary. When a term is added or modified, the change is committed in the backend repo first. The frontend repo holds a copy at `docs/glossary.md` with a header note pointing to the canonical location.

When the frontend repository is created, an initial copy is made. Subsequent updates are responsibility of whoever adds a term: update the backend glossary first, then propagate the change to the frontend repo in a separate PR.

If glossary drift becomes a recurring problem in practice (more than one drift incident per quarter, or any drift that causes an integration bug), upgrade to **option 4 (CI check)** by adding a scheduled GitHub Action that compares the two files and opens an issue if they differ.

## Consequences

**Positive:**
- Zero infrastructure cost upfront. No submodules, no third repository, no release pipeline.
- The discipline of "update the canonical source first" is light and natural.
- The cost of drift is low because the glossary is small (~70 entries) and stable (typical change rate: less than one term per month).

**Negative:**
- Manual sync is forgettable. A contributor working only in the frontend repo might add a term locally and forget to update the backend.
- There is no automatic alert when drift occurs.

**Neutral:**
- The decision is explicitly designed to be upgradeable: option 4 can be added later without restructuring anything else.

## Alternatives considered

**Git submodule or subtree** was rejected because it adds friction every time the glossary is updated: contributors must learn the submodule workflow, and routine PRs include submodule pointer updates. The complexity is not justified for a file that changes rarely.

**Generated package** was rejected because it presupposes a release pipeline for non-code artifacts, which is overhead we have no other reason to set up.

**CI check from day one** was rejected as premature optimization. Adding it later (if drift becomes a real problem) is straightforward and avoids paying the cost of CI infrastructure before we know it is needed.
