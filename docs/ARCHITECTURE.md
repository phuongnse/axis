# Architecture

> **Navigation**: [<- docs/README.md](./README.md) . [<- AGENTS.md](../AGENTS.md)

Axis is a modulith with strict service boundaries. Extraction must be a redeploy, not a refactor.

## System Context

Actors use the React SPA or platform APIs through `Axis.Api`. The gateway owns REST/OpenAPI, auth enforcement, and module composition in modulith mode.

## Containers

| Layer | Runtime |
|---|---|
| Web | React SPA |
| Gateway | `Axis.Api` |
| Modules | Identity, DataModeling, WorkflowBuilder, WorkflowEngine, FormBuilder, PageBuilder |
| Data | One PostgreSQL DB per module; schema-per-workspace inside each module DB |
| Messaging | Kafka for events/snapshots; RabbitMQ via Wolverine for commands/jobs/saga steps |
| Observability | OpenTelemetry SDK to Grafana stack |

## Module = Service: layering and contract surface

Each module keeps Domain pure, Application orchestration local, Infrastructure persistence/integration local, and Contracts schema-only.

### Per-module layer convention

Contracts <- Domain <- Application <- Infrastructure <- `Axis.Api` composition. Other modules may reference only Contracts.

### Cross-module communication contract

Events/snapshots use Kafka + Avro + CloudEvents. Commands/jobs/saga steps use RabbitMQ/Wolverine. Sync escape hatches use gRPC proto contracts.

Forbidden: shared `DbContext`, direct calls into another module Application/Infrastructure, cross-module SQL, in-process `IMediator` for cross-module dispatch.

## Multi-Workspace Isolation Strategy

Each module owns its own database. Workspace data lives in `workspace_{workspaceId:N}` schemas. Identity public schema is the registry; other modules do not query it.

See [persistence patterns](./playbooks/persistence-patterns.md#multi-workspace-isolation-pitfalls).

## Authentication

Identity issues JWTs. Other modules validate locally via JWKS and claims. Call Identity gRPC only when claims are insufficient for a synchronous decision.

## Observability & Operations

Use structured logs without PII, OpenTelemetry traces/metrics, `/health`, and `/health/ready`. Runtime patterns own host wiring details.

## Workflow Execution

Workflow definitions belong to WorkflowBuilder; executions belong to WorkflowEngine. Cross-module state is local read models synced by events unless a gRPC escape hatch is explicitly justified.
