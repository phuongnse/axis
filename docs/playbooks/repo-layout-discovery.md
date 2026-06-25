# Repo layout discovery (agents)

> **Navigation**: [<- docs/README.md](../README.md) . [<- agent checklist](./agent-checklist.md) . [<- AGENTS.md](../../AGENTS.md)

This file owns how repo layout maps to docs/config. Shared discovery code lives in `scripts/axis_repo.py`; checks run through `python scripts/axis.py check doc-drift`.

## Auto-discovered (do not duplicate lists elsewhere)

Modules, endpoint groups, proto roots, Kafka topic constants, domain README index rows, and many docs/layout relationships are discovered by scripts.

## Still manual (CI catches omissions)

Use-case specs, behavior/status callouts, design-source rows, workaround entries, and PR requirement honesty remain human/agent-owned.

## One command before review (layout + docs)

Use `$axis-ready-review`; it runs triggered layout/docs checks. For direct debugging, use `python scripts/axis.py check doc-drift`.

## Agent checklists

### A — New module (`src/Modules/NewModule/`)

Add projects/tests, solution entries, domain docs, composition wiring, and discovery-compatible names.

### B — New use case (folder under `docs/use-cases/{domain}/{slug}/`)

Use `$axis-use-case-spec`; add domain README link and status only when scope is real.

### C — New REST surface (`*Endpoints.cs` or handler in existing module)

Use `$axis-api-contract`; update generated contracts and API tests when route shape changes.

### D — New Kafka event

Use `$axis-cross-module-contract`; add schema, topic constant, producer/consumer wiring, and tests.

### E — `docker-compose.yml` change

Update [local-dev.md](./local-dev.md) and run the local-dev docs check.

## Rules (P0/P1 for agents)

Do not hand-maintain parallel module/topic/proto lists. Extend discovery scripts or focused checkers.

## See also

[agent-checklist.md](./agent-checklist.md), [process.md](./process.md), [scripts.md](./scripts.md).
