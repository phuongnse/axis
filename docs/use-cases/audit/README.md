# Audit Supporting Responsibilities

> **Navigation**: [docs/use-cases/README.md](../README.md) · [docs/architecture/identity-governance.md](../../architecture/identity-governance.md) · [docs/ARCHITECTURE.md](../../ARCHITECTURE.md) · [AGENTS.md](../../../AGENTS.md)

Audit is a supporting module, not an independent product journey. Owning use cases define actor-visible security outcomes; [docs/architecture/identity-governance.md](../../architecture/identity-governance.md#audit-delivery-realization) owns the current shared delivery, projection, retention, and threat-model realization.

## Supporting Responsibilities

| Owning use case | Layer | Responsibility | Status |
|---|---|---|---|
| [docs/use-cases/identity-governance/create-organization-workspace.md](../identity-governance/create-organization-workspace.md) | Audit | Project the required redacted Organization-creation outcome and support immutable read-back. | Done |
| [docs/use-cases/identity-governance/switch-active-workspace.md](../identity-governance/switch-active-workspace.md) | Audit | Project correlated requested and terminal Workspace-transition outcomes under the shared retention policy. | Done |
| [docs/use-cases/identity-governance/invite-workspace-member.md](../identity-governance/invite-workspace-member.md) | Audit | Project invitation administration, delivery, denial, and terminal lifecycle outcomes. | Done |
| [docs/use-cases/identity-governance/accept-workspace-invitation.md](../identity-governance/accept-workspace-invitation.md) | Audit | Project exchange, replay, invalid-token, wrong-account, stale-authority, and acceptance outcomes. | Done |
| [docs/use-cases/identity-governance/manage-workspace-service-identities.md](../identity-governance/manage-workspace-service-identities.md) | Audit | Project redacted service-identity, key, grant, denial, and irreversible-revocation lifecycle outcomes. | Not started |
| [docs/use-cases/identity-access/authenticate-service-identity.md](../identity-access/authenticate-service-identity.md) | Audit | Project redacted service authentication, assertion-replay, token rejection, and dependency-failure outcomes. | Not started |
| [docs/use-cases/authorization/manage-product-role-assignments.md](../authorization/manage-product-role-assignments.md) | Audit | Project redacted product-role assignment, revocation, denial, and failure outcomes. | Not started |
| [docs/use-cases/authorization/access-product-by-policy.md](../authorization/access-product-by-policy.md) | Audit | Project redacted server authorization allow, deny, and dependency-failure decisions. | Not started |
| [docs/use-cases/solutions/publish-signed-solution-version.md](../solutions/publish-signed-solution-version.md) | Audit | Project redacted package publication, trust validation, denial, conflict, and failure outcomes. | Not started |
| [docs/use-cases/solutions/install-solution-version.md](../solutions/install-solution-version.md) | Audit | Project redacted installation, resume, component-step, trust halt, and noncompliance outcomes. | Not started |
| [docs/use-cases/solutions/migrate-reference-product-to-signed-solution.md](../solutions/migrate-reference-product-to-signed-solution.md) | Audit | Project the signed reference-product publication, installation, cutover, denial, and failure outcomes required by its owning lifecycle contracts. | Not started |

The root inventory declares this hub's `Audit` layer identity. The use-case documentation gate derives every owning spec containing that layer, requires an exact nonempty responsibility mapping, and derives each responsibility status from its owner. This hub owns no separate acceptance criteria, evidence, or implementation status.
