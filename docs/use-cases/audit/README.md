# Audit

> **Navigation**: [docs/use-cases/identity-governance/README.md](../identity-governance/README.md) · [docs/use-cases/README.md](../README.md) · [docs/ARCHITECTURE.md](../../ARCHITECTURE.md) · [AGENTS.md](../../../AGENTS.md)

Audit owns the versioned redacted ingestion envelope and its immutable append-only projection. It is not an independent product journey: current audit behavior, including the required Identity outbox and correlated read-back, is owned by Identity Governance.

## Current Use Cases

| Owning use case | Audit responsibility | Status |
|---|---|---|
| [docs/use-cases/identity-governance/create-organization-workspace.md](../identity-governance/create-organization-workspace.md) | Ingest Organization-creation audit-outbox events idempotently and read them back by event ID. | Partial |
| [docs/use-cases/identity-governance/switch-active-workspace.md](../identity-governance/switch-active-workspace.md) | Ingest requested and terminal Workspace-transition outcomes through the same redacted envelope. | Partial |
| [docs/use-cases/identity-governance/invite-workspace-member.md](../identity-governance/invite-workspace-member.md) | Ingest invitation administration and delivery outcomes, including denied lifecycle actions. | Partial |
| [docs/use-cases/identity-governance/accept-workspace-invitation.md](../identity-governance/accept-workspace-invitation.md) | Ingest exchange, replay, invalid-token, wrong-account, stale-authority, and acceptance outcomes. | Partial |

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Audit Contracts, Domain, Application, and Infrastructure foundation | Partial |
> | Identity audit-outbox production and delivery integration | Partial |
>
> **Verification:** Focused Audit domain, application, model, migration, persistence, idempotency, and append-only-trigger evidence exists for the current foundation. End-to-end Identity Governance acceptance evidence remains in progress.
