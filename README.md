# Priamus Trips

Sistema di gestione e monitoraggio viaggi di trasporto merci per Priamus Ecologica S.r.l.

## Repository structure

```
priamus-trips/
├── .claude/agents/       AI agents for Claude Code
├── docs/                 Shared and per-track documentation
│   ├── shared/           Cross-cutting (glossary, system overview)
│   ├── backend/          Backend-specific context + appendices
│   └── frontend/         Frontend-specific context (placeholder until frontend kicks off)
├── backend/              .NET 10 modular monolith (the application)
├── frontend/             Angular 21+ PWA (to be added later)
├── compose.yaml          Local development environment
├── .gitignore
└── README.md             this file
```

## Development workflow

This repository follows a **backend-first** approach. The frontend track does not start until the backend has a stable API contract published as OpenAPI.

Daily development uses **Claude Code with specialized AI agents**. See `.claude/agents/` and `docs/shared/context.md` for the conventions.

## Quick start (backend)

Prerequisites:
- .NET 10 SDK
- Docker (for Testcontainers in integration tests)
- `dotnet-ef` global tool (`dotnet tool install --global dotnet-ef --version 10.*`)

```bash
cd backend
dotnet restore
dotnet build
dotnet test
```

To run the API locally, the backend module will provide instructions once the Host project is set up.

## Documentation

Start with `docs/shared/context.md` for the system overview. Backend-specific rules are in `docs/backend/context.md` and its `appendices/`.

## License

Proprietary. © Priamus Ecologica S.r.l.
