# Epics

[← Back to Docs Home](../README.md)

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
