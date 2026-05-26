# Epics

[← Back to Docs Home](../README.md)

---

## How agents find open work

**Do not use `- [ ]` checkboxes in feature files as progress** — they stay unchecked by convention (spec only). Use this order:

| Step | Source | What you learn |
|------|--------|----------------|
| 1 | This page → **Open work** on the epic README | Prioritized gaps for that epic (backend vs frontend called out) |
| 2 | `docs/epics/{epic}/features/F0N-*.md` | Per–user-story `> **Implementation status**` + `Gaps vs spec` + `**Done:**` / `**Deferred:**` |
| 3 | `docs/PROGRESS.md` | Module layer summary (Domain → Frontend); cross-cutting foundation phases |
| 4 | `grep -r "Application: ⚠️\|Infrastructure: ⚠️" docs/epics/` | US rows still blocked before API work ([agent-checklist](../playbooks/agent-checklist.md)) |

**Symbols** (same as [agent-checklist § Layer status](../playbooks/agent-checklist.md)): ✅ done for that layer on this US · ⚠️ partial (read `Gaps vs spec`) · ⏳ not started.

When you ship code, update **US callout → epic README table → epic Open work → PROGRESS** in the same PR. Never mark ✅ while `Gaps vs spec` still lists backend work for that layer.

**Full AC coverage (all cases, not happy path only):** [agent-checklist § AC coverage](../playbooks/agent-checklist.md#ac-coverage--avoid-happy-path-only) — Gate 0 AC map + TDD + `Gaps vs spec` on every PR.

---

## All Epics

| ID | Epic | Phase | Features |
|---|---|---|---|
| [E01](./E01-platform-foundation/README.md) | Platform Foundation | MVP | Tenant registration, org management, data isolation, subscription plans |
| [E02](./E02-identity-access/README.md) | Identity & Access Management | MVP | Authentication, user management, roles, permissions |
| [E03](./E03-data-modeling/README.md) | Data Modeling | MVP | Custom models, field types, data classes, record CRUD |
| [E04](./E04-workflow-builder/README.md) | Workflow Builder | MVP | Visual canvas, step types, triggers, branching, parallel, import/export |
| [E05](./E05-form-builder/README.md) | Form Builder | MVP | Form definition, field config, workflow integration, submissions |
| [E06](./E06-workflow-engine/README.md) | Workflow Execution Engine | MVP | Execution management, step handlers, error handling, history, retry |
| [E07](./E07-page-builder/README.md) | Page & UI Builder | Phase 2 | Page management, widget library, drag & drop, data binding |

---

## MVP Core Loop

```
[E01] Tenant Setup → [E02] Auth & Users → [E03] Model Data
         → [E04] Build Workflow → [E05] Add Forms
                  → [E06] Execute & Monitor
```

## Phase 2 Expansion

```
[E06] Execution Data → [E07] Build Pages & Widgets → End Users
```

---

## Distributed-ready foundation (cross-epic)

Platform-wide infrastructure tracked in [PROGRESS.md](../PROGRESS.md) (not owned by a single feature US):

| Phase | Status | Notes |
|---|---|---|
| Phase 0 — ADRs & architecture | ✅ | ADR-010..023 |
| Phase 1 — Infrastructure | ✅ | Per-module DBs, Kafka/RabbitMQ, OpenTelemetry ([ADR-018](../TECH_STACK.md#adr-018-opentelemetry-sdk-with-grafana-stack-for-observability)), Avro/Schema Registry pilot on WorkflowBuilder events ([ADR-019](../TECH_STACK.md#adr-019-avro-and-schema-registry-for-event-payloads-with-cloudevents-envelope)) |
| Phase 2 — Module boundaries | ⚠️ | Identity, WorkflowBuilder, FormBuilder, WorkflowEngine, and DataModeling `Axis.{Module}.Contracts` + Kafka Avro events. **Done:** model/form delete → broken refs (FormBuilder + WorkflowBuilder Kafka handlers, WorkflowBuilder gRPC for form delete guard). **Deferred:** DataModeling gRPC; field-delete consumers. |

Implementation patterns: [OpenTelemetry observability](../playbooks/patterns.md#opentelemetry-observability).
