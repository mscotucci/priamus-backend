# Context Map

Bounded contexts of the Priamus Trips backend, one per module. Each context owns a `CONTEXT.md` glossary next to its code (`src/Modules/<Module>/CONTEXT.md`), written when its first slice is grilled; ADRs that span contexts live in `docs/adr/`. Until a context has its glossary, `docs/glossary.md` is the only authority for its terms.

## Contexts

- **Identity** — authentication, sessions (refresh tokens), users and roles, audit log of identity events. Built before the per-context glossaries existed: its `CONTEXT.md` is written at the next grilling of the module (`docs/backlog.md`).
- **Registry** — customers, contracts, rates and overrides, vehicles, drivers, locations, pricing engine. Not yet built.
- **Operations** — trips with their attachments and issues, daily summaries. Not yet built.
- **BackOffice** — reporting, filterable trip list, Excel/PDF export. Not yet built.

## Relationships

- **Identity → all**: provides the current user (`ICurrentUser`) and the nominal authorization policies.
- **Registry → Identity**: resolves the driver linked to a user (`IDriverLookup` in `Priamus.Trips.Registry.Contracts`, ADR 0008); the host registers a null implementation until Registry lands.
- **Registry → Operations**: exposes the data a trip registration needs (customer, contract, vehicles, rates) and the pricing engine (`IPricingEngine`, ADR 0003).
- **Operations → BackOffice**: trips and daily summaries are the source of reports and exports.
- **Data boundary**: drivers never see pricing, rates or totals; only back-office endpoints expose them.
- **Shared types**: `UserId`, `Money`, `Tons`, `Kilometers`, `Hours`, `Result`, `IClock` from `Priamus.Trips.SharedKernel`.
- **Database**: one PostgreSQL schema per context, no cross-schema foreign keys; a reference to another context is a bare `Guid`.
