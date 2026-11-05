# D11. Durable process engine

Status: **Agreed**

## Context

Process engines fail in known ways, which [execution](../domain/knowledge.md#execution) describes:
running work picks up a changed definition, a crash leaves half a step written, and external calls
are retried blindly.

## Decision

- **Processes run on a persisted state machine.**
- **An instance is pinned to the release it started on.**
- **Each step commits in one PostgreSQL transaction:** business writes, process progress, the
  idempotency receipt, audit records and the next work.
- **External calls never run inside a transaction.** They are staged as durable work with stable
  operation identities.
- **Workers claim work with leases and fencing.**

## Consequences

A crash leaves no partial step, and every external call needs a way to settle an unknown outcome,
which [D25](d25-connectors-deployment-bindings-and-secrets.md) gives.
