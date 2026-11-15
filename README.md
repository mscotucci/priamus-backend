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
- Docker (Testcontainers for integration tests)
- `dotnet-ef` global tool: `dotnet tool install --global dotnet-ef --version 10.*`

## Quick start

```bash
dotnet build Priamus.Trips.slnx
dotnet test --project tests/Priamus.Trips.Identity.UnitTests
dotnet test --project tests/Priamus.Trips.Identity.IntegrationTests   # starts a PostgreSQL container
```

## Documentation

Start with [`docs/context.md`](docs/context.md). Delivery follows the workflow described there (§7): one slice → `grilling` → `plan-and-split` → `execute-plan` → `ai-code-review` → `qa-walkthrough` → `strip-plan-refs`.

## License

Proprietary. © Fixato di Scotucci Michele
