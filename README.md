# Priamus Trips — Backend

Backend repository for **Priamus Trips**, the digital management and monitoring system for freight trips operated by Priamus Ecologica S.r.l.

The frontend (Angular PWA + back-office web app) lives in a separate repository (link TBD) and communicates with this backend exclusively through the OpenAPI contract produced by the ASP.NET Core endpoints.

## Repository structure

```
priamus-trips/
├── .claude/agents/         Specialized AI agents for Claude Code
├── docs/
│   ├── context.md          System overview, universal rules, backend technical context
│   ├── glossary.md         Italian PRD terms → English code identifiers
│   └── appendices/         Detailed rules loaded on demand by agents
├── src/                    .NET 10 modular monolith (Host, SharedKernel, Modules)
├── tests/                  Unit + integration tests (xUnit, Testcontainers)
├── Priamus.Trips.slnx      Solution file
├── Directory.Build.props   Project-wide MSBuild settings
├── Directory.Packages.props Central package management
├── global.json             SDK version pin
└── NuGet.config            Feed configuration
```

## Prerequisites

- .NET 10 SDK (`dotnet --version` must report `10.x`)
- Docker (required by Testcontainers for integration tests)
- `dotnet-ef` global tool: `dotnet tool install --global dotnet-ef --version 10.*`

## Quick start

```bash
dotnet restore
dotnet build
dotnet test
```

## Documentation

Start with [`docs/context.md`](docs/context.md). The appendices in `docs/appendices/` provide deep detail on persistence, slices, pricing, testing, and the architecture audit rules.

The original step-by-step bootstrap procedure is preserved in [`SETUP.md`](SETUP.md) for historical reference.

## License

Proprietary. © Priamus Ecologica S.r.l.
