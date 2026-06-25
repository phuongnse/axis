# Development Process

> **Navigation**: [<- docs/README.md](../README.md) . [<- AGENTS.md](../../AGENTS.md)

Workflow lives in repo skills. This file keeps only durable process boundaries and legacy anchors used by docs.

## Backend process

Use `$axis-use-case-implementation` for feature slices.

### New module setup

Create module projects in the standard order: Domain, Application, Infrastructure, Axis.Api endpoint wiring, then Frontend when the slice has UI. Update docs only when the module/domain surface is real product scope.

Use `$axis-design-gate` first; new modules are high-blast-radius work even when no public API exists yet.

### Per use-case workflow

Implement in layer order:

1. Domain
2. Application
3. Infrastructure
4. API
5. Frontend

Keep TDD practical: write the proving test before behavior changes when the target layer has a clear test seam. Do not start a later layer while earlier in-scope gaps are hidden.

### Gap sweep before API

Before API work, search use-case status for partial Domain, Application, or Infrastructure layers. Fix stale status, document exact deferrals, or stop and implement the lower layer first.

### Host wiring

When adding an API endpoint group or hosted integration surface:

- Register it in the owning `Axis.Api` composition point.
- Keep endpoints thin: bind, dispatch, map result/problem details.
- Require authorization unless the route is explicitly public.
- Update generated contracts and API tests when route shape changes.

## Frontend process

Use `$axis-frontend-feature`; use `$axis-design-system` for primitives/tokens and `$axis-visual-artifact` for source/preview changes.

Frontend features stay in feature folders, consume generated API types, and prove observable behavior with Vitest/Testing Library or Playwright when a browser journey is required.

## Deferred follow-up

Do not hide unfinished in-scope work. Record a `**Deferred follow-ups:**` line in the owning use-case callout when a PR intentionally leaves a named AC or layer gap open.

Good deferrals name:

- The exact AC or behavior.
- The owner surface or future PR boundary.
- The reason it is safe to leave open now.

Use `N/A` when there are no deferrals.

## PR wrap-up

Before review, use `$axis-ready-review`. It owns changed-path classification, verification evidence, docs/status checks, and retrospective review.

Use `$axis-pull-request` only after readiness is current.
