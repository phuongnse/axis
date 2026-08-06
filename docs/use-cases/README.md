# Use Cases

> **Navigation**: [docs/README.md](../README.md) · [AGENTS.md](../../AGENTS.md)

Use cases are the product source of truth. Each use case owns one independently valuable primary-actor goal, its observable guarantees, acceptance criteria, implementation status, acceptance tests, and review verification. Shared technical realization belongs to the linked architecture or foundation owner.

## Current Use Cases

| Domain | Use case | Status |
|---|---|---|
| [docs/use-cases/identity-access/README.md](./identity-access/README.md) | [docs/use-cases/identity-access/register-user.md](./identity-access/register-user.md) | Done |
| [docs/use-cases/identity-access/README.md](./identity-access/README.md) | [docs/use-cases/identity-access/sign-in-user.md](./identity-access/sign-in-user.md) | Done |
| [docs/use-cases/identity-access/README.md](./identity-access/README.md) | [docs/use-cases/identity-access/authorize-local-mcp-client.md](./identity-access/authorize-local-mcp-client.md) | Done |
| [docs/use-cases/identity-access/README.md](./identity-access/README.md) | [docs/use-cases/identity-access/sign-out-user.md](./identity-access/sign-out-user.md) | Done |
| [docs/use-cases/identity-governance/README.md](./identity-governance/README.md) | [docs/use-cases/identity-governance/create-organization-workspace.md](./identity-governance/create-organization-workspace.md) | Done |
| [docs/use-cases/identity-governance/README.md](./identity-governance/README.md) | [docs/use-cases/identity-governance/switch-active-workspace.md](./identity-governance/switch-active-workspace.md) | Done |
| [docs/use-cases/identity-governance/README.md](./identity-governance/README.md) | [docs/use-cases/identity-governance/invite-workspace-member.md](./identity-governance/invite-workspace-member.md) | Done |
| [docs/use-cases/identity-governance/README.md](./identity-governance/README.md) | [docs/use-cases/identity-governance/accept-workspace-invitation.md](./identity-governance/accept-workspace-invitation.md) | Done |
| [docs/use-cases/business-objects/README.md](./business-objects/README.md) | [docs/use-cases/business-objects/configure-field-rules.md](./business-objects/configure-field-rules.md) | Done |
| [docs/use-cases/business-objects/README.md](./business-objects/README.md) | [docs/use-cases/business-objects/define-business-object.md](./business-objects/define-business-object.md) | Done |
| [docs/use-cases/business-objects/README.md](./business-objects/README.md) | [docs/use-cases/business-objects/submit-business-object-record.md](./business-objects/submit-business-object-record.md) | Done |
| [docs/use-cases/rules/README.md](./rules/README.md) | [docs/use-cases/rules/evaluate-published-rules.md](./rules/evaluate-published-rules.md) | Done |
| [docs/use-cases/rules/README.md](./rules/README.md) | [docs/use-cases/rules/manage-rule-bindings.md](./rules/manage-rule-bindings.md) | Done |
| [docs/use-cases/rules/README.md](./rules/README.md) | [docs/use-cases/rules/manage-workspace-rule-definitions.md](./rules/manage-workspace-rule-definitions.md) | Done |
| [docs/use-cases/rules/README.md](./rules/README.md) | [docs/use-cases/rules/provide-built-in-rule-definitions.md](./rules/provide-built-in-rule-definitions.md) | Done |
| [docs/use-cases/solutions/README.md](./solutions/README.md) | [docs/use-cases/solutions/provision-reference-solution.md](./solutions/provision-reference-solution.md) | Done |
| [docs/use-cases/site-experience/README.md](./site-experience/README.md) | [docs/use-cases/site-experience/select-site-language.md](./site-experience/select-site-language.md) | Done |
| [docs/use-cases/site-experience/README.md](./site-experience/README.md) | [docs/use-cases/site-experience/select-site-theme.md](./site-experience/select-site-theme.md) | Done |

## Supporting Domains

| Domain | Layer | Responsibilities |
|---|---|---|
| [docs/use-cases/audit/README.md](./audit/README.md) | Audit | Audit supports every use case whose implementation status declares the Audit layer. |

## Rule

Each use case lives as a single Markdown file at `docs/use-cases/{domain}/{slug}.md`. Complete use cases keep exact proof in `docs/use-cases/{domain}/{slug}.evidence.md`. Journey-domain folders contain their `README.md` hub, use-case files, and matching evidence sidecars only. A module with no independent actor journey keeps one explicitly listed supporting-domain hub whose responsibility status is derived from named layers in its owning use cases.

Do not add placeholder use-case files.
