# Platform Foundation

> **Navigation**: [← Use Cases](../README.md) · [← docs/README.md](../../README.md)

---

## Overview

Establish the multi-workspace SaaS foundation that all other modules depend on. This domain covers workspace provisioning, Workspace lifecycle management, and the data isolation strategy that keeps each workspace's data completely separate.

## Business Value

Without this foundation, nothing else works. Every feature in every other domain runs on top of the multi-workspace isolation infrastructure built here.

## Implementation order

Ship first — workspace registration, isolation, and subscription plans are prerequisites for every other domain.

---

## Use Cases

### Registration

| Use case | Summary |
|---|---|
| [Select a subscription plan during registration](plan-at-signup/) | Choose a subscription plan during registration so that I know what features and limits I have access to. |
| [Register a new workspace](register-workspace/) | Register a workspace on the Axis platform with an official workspace contact email, verify that contact channel, and… |

### Subscription plans

| Use case | Summary |
|---|---|
| [Change workspace plan (admin override)](admin-change-plan/) | Manually change a workspace's plan so that I can support early customers and testing without a billing integration. |
| [Enforce plan limits at the API](enforce-limits/) | Enforce subscription plan limits at the API so that Workspaces cannot exceed their subscription without upgrading. |
| [View available plans](view-plans/) | Compare available subscription plans so that I can choose the one that fits my needs. |

### Workspace settings

| Use case | Summary |
|---|---|
| [Delete Workspace](delete-workspace/) | Permanently delete my Workspace so that all our data is removed from the platform. |
| [Update Workspace profile](workspace-profile/) | Update my Workspace's name and logo so that the platform reflects our brand. |
| [View workspace settings](workspace-settings/) | View all workspace settings in one place so that I have full visibility into the configuration. |

### Workspace isolation

| Use case | Summary |
|---|---|
| [Workspace resolution from JWT](workspace-from-jwt/) | Resolve the active workspace from the JWT on every request so that downstream code never needs to think about workspace… |
| [Automatic workspace scoping on every request](workspace-scope/) | Every database query to be automatically scoped to the requesting workspace so that data isolation is enforced at the… |

### Other

| Use case | Summary |
|---|---|
| [Open workspace start](workspace-start/) | Open the first authenticated workspace screen so that the user understands their account state, workspace access, and… |



---

## Diagrams

workspace onboarding journey (workspace contact email → verify → provisioning): [register-workspace § Diagrams](./register-workspace/README.md#diagrams) (`register-workspace-journey`, `workspace-provisioning`). First-owner identity setup is a separate setup-token handoff that continues at `/register` and is owned by `register-workspace`. Standalone users register through [identity-access/register-user](../identity-access/register-user/) without a workspace.

---

## Acceptance Criteria (domain)

- [ ] A new Workspace can register and be fully provisioned with isolated workspace schemas after workspace email verification.
- [ ] No workspace can read or write data belonging to another workspace under any circumstances.
- [ ] Workspace schema is automatically created and migrated on registration.
- [ ] Workspace can update its profile (name, logo, settings) without affecting other workspaces.

---

## Implementation Status

| Layer | Status | Notes |
|---|---|---|
| Shared Domain | ✅ Done | `Entity`, `AggregateRoot`, `ValueObject`, `IDomainEvent`, `Result<T>` |
| Shared Application | ✅ Done | `ICommand/IQuery`, `ICommandHandler/IQueryHandler`, `ValidationBehavior`, `IWorkspaceContext` |
| Shared Infrastructure | ✅ Done | `WorkspaceSchemaInterceptor`, per-module `UnitOfWork` ([ADR-017](../../TECH_STACK.md#adr-017-axisshared-is-abstractions-only-no-shared-implementation)); **OpenTelemetry** host wiring on `Axis.Api` ([ADR-018](../../TECH_STACK.md#adr-018-opentelemetry-sdk-with-grafana-stack-for-observability), [patterns § OpenTelemetry](../../playbooks/patterns.md#opentelemetry-observability)) |
| [Register workspace](register-workspace/) | ⚠️ Partial | Backend/API split is implemented: workspace contact email + workspace verification + workspace provisioning stay here. Standalone user registration is complete in [identity-access/register-user](../identity-access/register-user/); first-user setup-token handoff polish and the dedicated register-workspace frontend remain here. |
| [Subscription plans](view-plans/) | ✅ Done | `GET /api/plans`, pricing data, 402 limits — see [enforce limits](enforce-limits/). Frontend pricing UI ⏳ |
| [Workspace isolation](workspace-scope/) | ✅ Done | `WorkspaceSchemaInterceptor`, `WorkspaceAccessMiddleware`, cross-workspace API tests |
| [Workspace management](workspace-profile/) | ✅ Done | Profile, settings + usage, scheduled deletion + hard-delete job ✅. Frontend settings UI ⏳ |
| Frontend | ⏳ Pending | Register-workspace journey (incl. verify screens), provisioning wait, settings, pricing |

---

## Open work (agents)

| Priority | Item | Where |
|----------|------|--------|
| Frontend | [Register workspace](register-workspace/) workspace onboarding, [pricing](view-plans/), [workspace settings](workspace-settings/) | see **Use Cases** table above |

Domain-level checkboxes above remain spec-only; status is in use-case **Implementation status** callouts.

---

## Dependencies

- None (this is the foundation)

## Dependents

- [Identity & Access](../identity-access/README.md)
- All other domains
