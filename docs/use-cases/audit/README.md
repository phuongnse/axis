# Audit

> **Navigation**: [docs/use-cases/identity-governance/README.md](../identity-governance/README.md) · [docs/use-cases/README.md](../README.md) · [docs/ARCHITECTURE.md](../../ARCHITECTURE.md) · [AGENTS.md](../../../AGENTS.md)

Audit owns the versioned redacted ingestion envelope and its immutable append-only projection. It is not an independent product journey: current audit behavior, including the required Identity outbox and correlated read-back, is owned by Identity Governance.

## Current Use Cases

| Owning use case | Audit responsibility | Status |
|---|---|---|
| [docs/use-cases/identity-governance/create-and-switch-workspace.md](../identity-governance/create-and-switch-workspace.md) | Ingest Identity's durable audit-outbox events idempotently into redacted append-only records and read them back by event ID. | In progress |
| [docs/use-cases/identity-governance/invite-and-accept-workspace-member.md](../identity-governance/invite-and-accept-workspace-member.md) | Ingest invitation lifecycle, replay/invalid-token, anonymous, and system-delivery audit outcomes through the same contract. | In progress |

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Audit Contracts, Domain, Application, and Infrastructure foundation | In progress |
> | Identity audit-outbox production and delivery integration | In progress |
>
> **Verification:** Focused Audit domain, application, model, migration, persistence, idempotency, and append-only-trigger evidence exists for the current foundation. End-to-end Identity Governance acceptance evidence remains in progress.
