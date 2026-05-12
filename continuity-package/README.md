# Continuity Package — Priamus Trips

This package contains three artifacts designed to preserve context across AI assistant sessions:

1. **Six retroactive ADRs** (`docs/decisions/`) capturing the most important architectural decisions made so far.
2. **`docs/CURRENT_STATE.md`** — a single-page snapshot of where the project stands.
3. **`SESSION_OPENERS.md`** — templates for starting new AI conversations.

## How to install

After the repository restructure (moving from `backend/` subdirectory to root), copy the contents like this:

```
priamus-trips-backend/                    (your repo root)
├── docs/
│   ├── context.md                        (already there)
│   ├── glossary.md                       (already there)
│   ├── appendices/                       (already there)
│   ├── CURRENT_STATE.md                  ← NEW, copy from this package
│   └── decisions/                        ← NEW directory, copy from this package
│       ├── README.md
│       ├── 0001-modular-monolith-with-vertical-slice.md
│       ├── 0002-result-pattern-for-business-errors.md
│       ├── 0003-pricing-engine-as-pure-function.md
│       ├── 0004-separate-repositories-for-backend-and-frontend.md
│       ├── 0005-jwt-with-refresh-token-rotation.md
│       └── 0006-shared-kernel-http-as-separate-project.md
└── SESSION_OPENERS.md                    ← NEW at root, copy from this package
```

## Suggested commit

```bash
git add docs/CURRENT_STATE.md docs/decisions/ SESSION_OPENERS.md
git commit -m "Add ADRs, current state, and session opener templates for AI continuity"
```

## Maintenance

- **ADRs**: add a new one each time a significant decision is made. Never edit a past one (use the "Superseded by" status if a decision is reversed).
- **CURRENT_STATE.md**: update after each milestone. Keep it under 150 lines.
- **SESSION_OPENERS.md**: refine over time as you discover what context gets asked for repeatedly.

