# Backlog

Work deferred on purpose, with the decision that deferred it. Items leave this file when a slice picks them up or a decision drops them. Plan folders under `.plans/` are not committed, so anything a future slice must know lives here.

## Identity — code written before the current rules (recorded 2026-11-15)

The Identity module was built in May 2026, before the rules in `docs/context.md` and the appendices took their current form. It was brought in line on 2026-11-15 (original error preserved by `DomainException`, internal handlers, nominal policies, error codes and routes prefixed by the module, enums by name, camelCase query names in OpenAPI, Italian messages, cross-slice sweeps). What is left:

- **Tests build one host per test** instead of one per class (`appendices/testing.md`, "End-to-end tests"). Each endpoint test class calls `ApiFactory.Create` inside the test; moving to a class-level host needs the per-scope re-pointing of the `DbContext` that a shared host requires.
- **Endpoint tests parse `errorCode` by hand** instead of `ShouldBeProblemAsync`.
- **`PostgresFixture` is registered per collection** (`IdentityIntegrationTestsCollection`) and migrates only the Identity schema. It becomes an assembly fixture migrating every module's schema when a second module has tests.
- **No sweep compares each endpoint's `ProducesProblem` with what its handler can return.** The declared statuses were checked by hand on 2026-11-15.
- **`IClock` has no `Today`.** Needed by the first slice that reasons on calendar dates in the business time zone (trip execution date, daily summary).
- **No `src/Modules/Identity/CONTEXT.md`.** To write at the next grilling of the module.
- **Italian texts of `IdentityMessages.resx`** were written with the alignment, not reviewed with the customer.

## Shared test infrastructure

- **Test authentication handler.** Endpoint tests of Registry, Operations and BackOffice should not seed a user and log in to obtain a token: a test scheme issuing the claims of a caller (user id, role, linked driver) belongs in `tests/Priamus.Trips.IntegrationTesting`, written with the first endpoint test that needs it. Identity keeps testing the real JWT.

## Documentation

- **`docs/appendices/audit-rules.md`** still ends with the report format of the removed `architecture-guardian` agent ("Output format", "Summary", "Violations"). The rules above it are the checklist `ai-code-review` reads; the tail can go.
- **`docs/appendices/persistence.md`** keeps its long form (it describes a schema designed for the domain, which has no shorter equivalent); only its links and its list of read-side rules were aligned.
