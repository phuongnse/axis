# Audit Supporting Responsibilities

> **Navigation**: [docs/use-cases/README.md](../README.md) · [docs/architecture/identity-governance.md](../../architecture/identity-governance.md) · [docs/ARCHITECTURE.md](../../ARCHITECTURE.md) · [AGENTS.md](../../../AGENTS.md)

Audit is a supporting module, not an independent product journey. Identity Governance use cases own the actor-visible security outcomes; [docs/architecture/identity-governance.md](../../architecture/identity-governance.md#audit-delivery-realization) owns the shared delivery, projection, retention, and threat-model realization.

## Supporting Responsibilities

| Owning use case | Layer | Responsibility | Status |
|---|---|---|---|
| [docs/use-cases/identity-governance/create-organization-workspace.md](../identity-governance/create-organization-workspace.md) | Audit | Project the required redacted Organization-creation outcome and support immutable read-back. | Done |
| [docs/use-cases/identity-governance/switch-active-workspace.md](../identity-governance/switch-active-workspace.md) | Audit | Project correlated requested and terminal Workspace-transition outcomes under the shared retention policy. | Done |
| [docs/use-cases/identity-governance/invite-workspace-member.md](../identity-governance/invite-workspace-member.md) | Audit | Project invitation administration, delivery, denial, and terminal lifecycle outcomes. | Done |
| [docs/use-cases/identity-governance/accept-workspace-invitation.md](../identity-governance/accept-workspace-invitation.md) | Audit | Project exchange, replay, invalid-token, wrong-account, stale-authority, and acceptance outcomes. | Done |

The root inventory declares this hub's `Audit` layer identity. The use-case documentation gate derives every owning spec containing that layer, requires an exact nonempty responsibility mapping, and derives each responsibility status from its owner. This hub owns no separate acceptance criteria, evidence, or implementation status.
