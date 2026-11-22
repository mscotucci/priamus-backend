# Context Map

Bounded contexts of the Priamus Trips backend, one per module. Each context owns a `CONTEXT.md` glossary next to its code (`src/Modules/<Module>/CONTEXT.md`), written when its first slice is grilled; ADRs that span contexts live in `docs/adr/`. Until a context has its glossary, `docs/glossary.md` is the only authority for its terms.

## Contexts

- [Identity](./src/Modules/Identity/CONTEXT.md) — authentication, sessions (refresh tokens), users and roles, audit log of identity events.
- [Registry](./src/Modules/Registry/CONTEXT.md) — customers, contracts, rates and overrides, vehicles, drivers, locations, pricing engine. Its glossary covers the vehicle registry (2026-11-16), the customer registry, contracts, drivers and locations (2026-11-21); the rest is not yet built.
- **Operations** — trips with their attachments and issues, daily summaries. Not yet built.
- **BackOffice** — reporting, filterable trip list, Excel/PDF export. Not yet built.

## Relationships

- **Identity → all**: provides the current user (`ICurrentUser`) and the nominal authorization policies.
- **Registry → Identity**: resolves the driver linked to a user (`IDriverLookup` in `Priamus.Trips.Registry.Contracts`, ADR 0008); the implementation lives in Registry.
- **Identity → Registry**: Registry reads user accounts (user name, role, status, the `Driver` accounts) through `IUserLookup` in `Priamus.Trips.Identity.Contracts`, to link a driver to its account (ADR 0013).
- **Registry → Operations**: exposes the data a trip registration needs (customer, contract, vehicles, rates) and the pricing engine (`IPricingEngine`, ADR 0003).
- **Operations → BackOffice**: trips and daily summaries are the source of reports and exports.
- **Data boundary**: drivers never see pricing, rates or totals; only back-office endpoints expose them.
- **Shared types**: `UserId`, `Money`, `Tons`, `Kilometers`, `Hours`, `Result`, `IClock` from `Priamus.Trips.SharedKernel`.
- **Database**: one PostgreSQL schema per context, no cross-schema foreign keys; a reference to another context is a bare `Guid`.
