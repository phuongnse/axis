# Wolverine Patterns

> **Navigation**: [<- docs/README.md](../README.md) . [<- patterns index](./patterns-index.md) . [<- AGENTS.md](../../AGENTS.md)

Use `$axis-cross-module-contract` for Wolverine routing that crosses module boundaries.

## Wolverine patterns

Keep Wolverine usage explicit: local handlers for intra-module work, RabbitMQ for `*Command`/`*Job`/`*SagaStep`, Kafka outbox for `*Event`/`*Snapshot`.

### Host setup (ADR-012 — per-module `wolverine` schema)

Each module owns its Wolverine envelope schema in its own database. Do not share envelope tables across modules.

### Intra-module domain event handler

In-process domain event handling stays inside the module boundary and must not dispatch to other modules through MediatR.

### Inter-module domain event handler

Cross-module events are contracts: Avro payload, CloudEvents envelope, Kafka topic, consumer handler, and local read model when queryable state is needed.

### Reliable background job

Jobs use Wolverine/RabbitMQ work-queue semantics: ACK, retry/requeue, DLX, idempotent handler.

### Step handler idempotency — at-least-once delivery

Handlers must tolerate duplicate delivery. Store an idempotency key or state transition guard before producing side effects.

### Concurrent-duplicate protection via xmin optimistic concurrency

Use database concurrency where duplicate job delivery can race the same row.

### Wolverine handler logging — two-layer rule

Log operation context and outcome without PII. Keep noisy payload logging out of handlers.

### Scheduled / recurring job

Recurring work must be idempotent, observable, cancellable, and safe across workspace boundaries.
