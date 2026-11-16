# Priamus Trips — Backend

Backend of **Priamus Trips**, the digital management and monitoring system for freight trips operated by Priamus Ecologica S.r.l., as a .NET 10 modular monolith on PostgreSQL. The frontend (driver PWA and back-office web app) lives in a separate repository and talks to this backend only through the OpenAPI contract.

## Repository structure

```
priamus-trips-backend/
├── CLAUDE.md                 Instructions for Claude Code
├── CONTEXT-MAP.md            Bounded contexts and where each CONTEXT.md glossary lives
├── docs/
│   ├── context.md            Stack, architecture, universal rules, delivery workflow
│   ├── PRD.md                Functional requirements
│   ├── glossary.md           Italian PRD terms → English code identifiers
│   ├── backlog.md            Work deferred on purpose
│   ├── adr/                  Architecture decision records
│   └── appendices/           Detailed rules loaded on demand (slices, errors, persistence, testing, pricing)
├── src/                      Host, SharedKernel, Modules/{Identity, Registry, Operations, BackOffice}
├── tests/                    Unit and integration tests (xUnit v3, Testcontainers PostgreSQL)
├── Priamus.Trips.slnx
├── Directory.Build.props     Project-wide MSBuild settings
├── Directory.Packages.props  Central package management
└── global.json               SDK pin and Microsoft.Testing.Platform opt-in
```

## Prerequisites

- .NET 10 SDK
- Docker (local PostgreSQL through Docker Compose; Testcontainers for integration tests)
- `dotnet-ef` global tool: `dotnet tool install --global dotnet-ef --version 10.*`

## Quick start

Run the API locally against a PostgreSQL started with Docker Compose:

```bash
docker compose up -d
dotnet run --project src/Priamus.Trips.Host   # http://localhost:5210, OpenAPI at /openapi/v1.json
```

The whole application uses one database (`priamus_trips`) behind one connection string, `ConnectionStrings:Database`; the local value lives in `src/Priamus.Trips.Host/appsettings.Development.json` (localhost credentials only). In Development the host applies pending migrations at startup. On a database that has never been migrated the first start logs one `ERR` line, `Failed executing DbCommand … FROM identity.__ef_migrations_history`: EF Core reads the migrations history before creating it, and the migration then runs normally. It is harmless and does not appear again.

### First administrator

At startup, in every environment, the host creates the first `Admin` from the configuration section `Identity:BootstrapAdmin` (`Username`, `Password`), but only when the users table is empty. Once any user exists the section is ignored (logged) and nothing is created or changed.

- **Development**: `appsettings.Development.json` carries a local-only administrator (`admin`, see the file for the password); after `dotnet run` you can sign in through `POST /api/identity/auth/login`.
- **Production**: supply the two values as secrets, for example the environment variables `Identity__BootstrapAdmin__Username` and `Identity__BootstrapAdmin__Password`. The schema must already be migrated by the pre-deploy job: the bootstrap runs after it and the host does not start if the users table is missing.
- The password follows the same rule as every user (8-128 characters, at least one letter, one digit and one special character) and the user name the rule of `Username`; when either breaks its rule the host does not start. With no (or incomplete) configuration and no users, the host starts and logs a warning: nobody can sign in.
- There is no endpoint to change a password yet (see `docs/backlog.md`): choose the production password knowing it cannot be rotated through the API.

Build and test:

```bash
dotnet build Priamus.Trips.slnx
dotnet test --project tests/Priamus.Trips.Identity.UnitTests
dotnet test --project tests/Priamus.Trips.Identity.IntegrationTests   # starts a PostgreSQL container
```

## Documentation

Start with [`docs/context.md`](docs/context.md). Delivery follows the workflow described there (§7): one slice → `grilling` → `plan-and-split` → `execute-plan` → `ai-code-review` → `qa-walkthrough` → `strip-plan-refs`.

## License

Proprietary. © Fixato di Scotucci Michele
