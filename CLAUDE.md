# Priamus Trips backend — instructions for Claude Code

## Core rules (before anything else)

### 1. Think Before Coding

- Don't assume. When a requirement, a type, or an existing pattern is unclear, read the code; in an interactive session, ask. In an orchestrated phase there is no one to ask: take the conservative interpretation and record the assumption in your report, or stop and report Blocked.
- Don't hide confusion. If something doesn't add up, surface it instead of coding around it.
- Surface tradeoffs. When there's more than one reasonable approach, state the options and give a recommendation rather than silently picking one.

### 2. Simplicity First

- Write the minimum code that solves the problem. No speculative abstractions, no "might need it later".
- Prefer what the framework or the codebase already provides (standard validation, an existing base class or shared component) over introducing something new.
- If a change starts growing beyond the ask, stop and flag it.

### 3. Surgical Changes

- Touch only what you must. Respect the vertical-slice structure (ADR 0012) and each agent's write scope.
- Clean up only your own mess — never opportunistically refactor adjacent code.
- Match the surrounding code: naming, comment density, and idiom.

## Project instructions

- Read `docs/context.md` first. Bounded-context glossaries are indexed by `CONTEXT-MAP.md`; ADRs are in `docs/adr/`.
- Functional source of truth: `docs/PRD.md`. Cite the PRD section or requirement (`PRD §5.4`, `RF-08quater`) in code comments when implementing behaviour. Never load the whole PRD: read only the sections the slice brief cites.
- Code in English; user-facing text in Italian `.resx` keyed by `Error.Code`.
- Drivers never see pricing, rates or totals: check every endpoint and DTO against this boundary.
- The database is PostgreSQL, one schema per module, changed only through EF Core migrations; no cross-schema foreign keys.
- Every endpoint declares a nominal policy from `Priamus.Trips.Identity.Contracts.AuthorizationPolicies`.
- Build a module outside-in (ADR 0012): whole slices — handler plus the persistence that slice needs — before anything shared. Extract a shared loader or query service from working slices, never ahead of them; a rule two slices would restate becomes a pure function in the domain that both call.
- Tests: `dotnet test --project <test project>` (Microsoft.Testing.Platform). Integration tests need Docker.
- `.plans/` holds grilling/planning staging artifacts and is never committed.
