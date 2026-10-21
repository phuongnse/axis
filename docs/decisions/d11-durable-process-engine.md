# D11. Durable process engine

Status: **Agreed**

- **Engine model:** processes run on a persisted state machine.
- **Version pinning:** an instance is pinned to the release it started on.
- **Atomic commit:** each step commits in one PostgreSQL transaction: business
  writes, process progress, idempotency receipt, audit and next work (outbox).
- **External calls:** these never run inside a transaction. They are staged as
  durable work with stable operation identities.
- **Worker claims:** workers claim work with leases and fencing.

_Why:_ see [knowledge](../domain/knowledge.md#execution).
