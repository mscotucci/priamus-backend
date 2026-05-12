# Priamus Trips Frontend — Context

**Status**: PLACEHOLDER. Frontend work has not started yet. This document will be expanded when the backend reaches a stable API contract.

This file inherits from `docs/shared/context.md` (which all agents read).

---

## Stack (planned)

- **Angular 21+** (active support release, released November 2025).
- **Zoneless change detection** (default in new Angular 21 projects).
- **Vitest** as test runner (default in Angular 21).
- **Tailwind CSS** + **DaisyUI 4** with `corporate` theme.
- **Inline SVG sprite** with Feather/Lucide-style icons.
- **TypeScript** strict mode.

Two distinct Angular applications in the same workspace (or two route trees in one app — decision deferred):
- Driver PWA (mobile/tablet, glove-friendly, no financial data)
- Back-office app (desktop, full data visibility)

## Decisions deferred until frontend kickoff

- Workspace structure (single app with route guards, or two apps in one workspace, or two apps in two workspaces)
- State management strategy (Signals only, or NgRx Signal Store, or other)
- API client generation tool (openapi-generator, ng-openapi-gen, or similar)
- Form strategy (Reactive Forms; Signal Forms are still developer-preview in 21.x)
- E2E test framework (Playwright likely)
- PWA configuration (service worker, manifest, install prompt)
- i18n strategy (Angular built-in i18n vs ngx-translate)

These decisions will be made in collaboration when frontend work starts, drawing on the patterns established in the backend and the constraints from PRD §8.

## Universal rules already established

The following rules from `docs/shared/context.md` apply equally to frontend work:

- Language: code in English, end-user content in Italian via i18n
- Domain glossary: same as backend (`docs/shared/glossary.md`)
- Driver/back-office boundary: drivers must NEVER see pricing, tariffs, totals
- Authorization: every route declares a guard, driver and back-office route sets are disjoint
- Error handling: server `errorCode` translated to localized Italian messages

## When this document gets filled in

This placeholder is replaced when:

1. The backend has at least the SharedKernel, the Identity module, and one Operations endpoint working end-to-end.
2. The OpenAPI specification produced by the backend is reviewed and stable.
3. The frontend agents are designed (planned: 3-4 specialized agents — component builder, API client, state management, test writer).

Until then, do not start frontend work.
