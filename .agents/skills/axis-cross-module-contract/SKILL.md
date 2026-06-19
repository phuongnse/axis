---
name: axis-cross-module-contract
description: Wire Axis cross-module contracts safely. Use when adding or changing Avro events or snapshots, RabbitMQ commands/jobs/saga steps, Wolverine routing, Kafka topic wiring, gRPC proto contracts, or module-to-module integration.
---

# Axis Cross Module Contract

## Goal

Add or change cross-module communication without introducing in-process coupling, cross-module SQL, or contract drift.

## Workflow

1. Run `$axis-design-gate`.
   - Cross-module interaction is high-risk.
   - Stop for user sign-off before code.

2. Read the owning rules.
   - `AGENTS.md`
   - `docs/TECH_STACK.md` ADRs for event sourcing, Avro, RabbitMQ/Wolverine, and routing suffixes
   - `docs/playbooks/cross-module-patterns.md`
   - `docs/playbooks/wolverine-patterns.md`
   - `docs/playbooks/grpc-patterns.md` when sync RPC or proto changes
   - `docs/playbooks/repo-layout-discovery.md`
   - `docs/playbooks/agent-checklist.md`
   - The owning use-case file when behavior changes

3. Choose the transport from the contract semantics.
   - `*Event` or `*Snapshot`: Kafka topic with Avro payload and CloudEvents envelope.
   - `*Command`, `*Job`, or `*SagaStep`: RabbitMQ exchange/queue via Wolverine.
   - Sync escape hatch: gRPC only, with `.proto` in `Axis.{Module}.Contracts`.
   - Never add a project reference to another module Application/Infrastructure project.
   - Never read another module database.

4. Put schema first.
   - Avro and proto contracts live in the producer/owner module Contracts project.
   - Name topics, message types, and versioned fields before wiring handlers.
   - Search existing schemas, topic constants, handlers, consumers, and tests with `rg`.

5. Wire only through allowed integration points.
   - Publish from the originating module outbox.
   - Consume in the target module through Wolverine handlers or gRPC clients/servers.
   - Keep local read models synced by events when the target module needs queryable foreign state.
   - Keep MediatR intra-module only.

6. Verify.
   - Proto: regenerate/check Buf config, then run `buf lint` when available.
   - Avro/Kafka: run `python scripts/axis.py check kafka-wiring` and `python scripts/axis.py register avro-schemas --dry-run` when triggered.
   - Layout: run `python scripts/axis.py check buf-modules` when proto layout changes.
   - Architecture: run architecture tests or `dotnet test` scope that covers project references and module boundaries.
   - Ready review: `$axis-ready-review`.

## Output

Report the chosen transport, contract files, producer and consumer wiring, verification commands, and any compatibility risk.
