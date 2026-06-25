# Cross-module Patterns

> **Navigation**: [<- docs/README.md](../README.md) . [<- patterns index](./patterns-index.md) . [<- AGENTS.md](../../AGENTS.md)

Use `$axis-cross-module-contract` for events, commands, jobs, saga steps, Wolverine, Kafka, RabbitMQ, gRPC, Avro, or proto work.

## Cross-module communication pattern

Cross-module data crosses contracts, not in-process calls or SQL. Prefer event-driven local read models; use gRPC only for justified sync decisions.

### ❌ Anti-pattern A: cross-module raw SQL

Never query another module database/table. Sync a local read model by event instead.

### ❌ Anti-pattern B: in-process call dressed as an interface

An interface does not make cross-module Application/Infrastructure calls legal. Use Kafka, RabbitMQ/Wolverine, or gRPC.

### ✅ Pattern 1: event-driven local read model (default)

Producer publishes Avro CloudEvent from its outbox. Consumer updates an owning-module read model. Queries stay local.

### Rules (P0)

- No project reference to another module Application/Infrastructure.
- No shared `DbContext`.
- No cross-module aggregate references.
- MediatR stays intra-module.
- JWT validation is local via JWKS.

### Pre-commit violation sweep

Search raw SQL, project refs, DI registrations, and `IMediator` usage before review. Fix violations or document an approved workaround.
