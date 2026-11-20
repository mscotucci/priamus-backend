# Backlog

Work deferred on purpose, with the decision that deferred it. Items leave this file when a slice picks them up or a decision drops them. Plan folders under `.plans/` are not committed, so anything a future slice must know lives here.

## Trips — to analyse when the first trip slice is grilled (recorded 2026-11-19)

- **Changes to master data after a trip used them.** Since the vehicle rates slice, a vehicle that has a vehicle rate in any contract cannot become a trailer: the rate must be removed from the contract first. With trips, decide how registered trips react to such changes (a vehicle's type, a vehicle rate removed or changed, a contract's terms): the trip's snapshot of the rate may be enough, but it must be analysed, not assumed (decided by Michele at the grilling of the vehicle rates).

## Registry — vehicle rates (recorded 2026-11-20)

- **The rule "a vehicle rate only on a tractor or a standalone vehicle" has an unguarded window.** It is enforced by two check-then-save handlers on different aggregates: the contract writes refuse a trailer (`ContractVehicleRateChecks`), the vehicle update refuses turning into a trailer a vehicle with a vehicle rate (`UpdateVehicleHandler`, `Registry.Vehicle.HasVehicleRates`). A contract save and a type change on the same vehicle at the same moment can both pass and leave a vehicle rate on a trailer. Accepted by Michele on 2026-11-20: in a small back office the case is practically impossible, and the pricing engine would never use such a rate (a trailer never leads a trip). Closing it would need a lock on the vehicle during contract writes or a database trigger.
- **Idea: vehicle rates for hired vehicles.** A vehicle taken on hire could carry a rate of its own in a contract, through the same vehicle rates. Not analysed: whether hired vehicles live in the vehicle registry, and with what type and status, is open (idea from Michele on 2026-11-20, during the QA of the vehicle rates).

## Identity — code written before the current rules (recorded 2026-11-15)

The Identity module was built in May 2026, before the rules in `docs/context.md` and the appendices took their current form. It was brought in line on 2026-11-15 (original error preserved by `DomainException`, internal handlers, nominal policies, error codes and routes prefixed by the module, enums by name, camelCase query names in OpenAPI, Italian messages, cross-slice sweeps). What is left:

- **Tests build one host per test** instead of one per class (`appendices/testing.md`, "End-to-end tests"). Each endpoint test class calls `ApiFactory.Create` inside the test; moving to a class-level host needs the per-scope re-pointing of the `DbContext` that a shared host requires.
- **Endpoint tests parse `errorCode` by hand** instead of `ShouldBeProblemAsync`.
- **`PostgresFixture` is registered per collection** (`IdentityIntegrationTestsCollection`, `RegistryIntegrationTestsCollection`), so each collection starts its own container. It migrates every module's schema into one template; making it a single assembly-level fixture shared by all test projects is what remains.
- **No sweep compares each endpoint's `ProducesProblem` with what its handler can return.** The declared statuses were checked by hand on 2026-11-15.
- **`IClock` has no `Today`.** Needed by the first slice that reasons on calendar dates in the business time zone (trip execution date, daily summary).
- **Italian texts of `IdentityMessages.resx`** were written with the alignment, not reviewed with the customer.
- **Audit payloads of domain events are serialized as the event's raw shape**: enums as numbers (`"role": 3`) and value objects as objects (`"userId": {"value": "…"}`), while the hand-written payloads use names and plain values (`{"username": "admin"}`). Found by the QA of the local-run slice on 2026-11-15; to align with "enums by name" when something first reads the audit log, deciding what happens to the rows already written.

## Audit logs — retention (recorded 2026-11-16)

- **The audit mechanism is copied, not shared.** Identity and Registry each hold their own `AuditLogEntry`, its configuration, the serializer options and the `AuditInterceptor` loop (decided 2026-11-16, vehicle registry slice). When Operations needs it, promote the module-independent pieces to a shared persistence library (SharedKernel cannot reference EF Core) and keep only the event mapping in each module.

- **Nothing is ever deleted from the `audit_log` tables.** Decided on 2026-11-16: retention per category of event, enforced by a daily job that deletes in batches the rows older than their category's duration (durations in configuration), decided in an ADR and built as a slice of its own. Starting proposal, durations to confirm with Priamus and its advisor: access and session events (login, failed login, revocation) 12 months, for GDPR storage limitation; changes to master data and users as long as the data they explain; changes to trips and anomaly resolutions as long as the trips, which are billing records. Until then nothing is deleted.

## Identity — deferred by the local-run and bootstrap slice (recorded 2026-11-15)

- **Change-password endpoint.** The first administrator's password comes from configuration and nothing in the API can change it; needed before any real use.
- **A disabled user's access token** is accepted by every endpoint but `me` until it expires, at most one hour (ADR 0005); rejecting it on every endpoint would cost a database read per request.
- **CORS.** The frontend reaches the backend through its dev-server proxy in development; the policy is decided with the deployment.

## Documentation

- **`docs/appendices/audit-rules.md`** still ends with the report format of the removed `architecture-guardian` agent ("Output format", "Summary", "Violations"). The rules above it are the checklist `ai-code-review` reads; the tail can go.
- **`docs/appendices/persistence.md`** keeps its long form (it describes a schema designed for the domain, which has no shorter equivalent); only its links and its list of read-side rules were aligned.
