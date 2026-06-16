# Platform Foundation

> **Navigation**: [← Use Cases](../README.md) · [← docs/README.md](../../README.md)

---

## Overview

Establish the multi-tenant SaaS foundation that all other modules depend on. This domain covers tenant provisioning, Tenant lifecycle management, and the data isolation strategy that keeps each tenant's data completely separate.

## Business Value

Without this foundation, nothing else works. Every feature in every other domain runs on top of the multi-tenancy infrastructure built here.

## Implementation order

Ship first — tenant registration, isolation, and subscription plans are prerequisites for every other domain.

---

## Use Cases

### Registration

| Use case | Summary |
|---|---|
| [Select a subscription plan during registration](plan-at-signup/) | Choose a subscription plan during registration so that I know what features and limits I have access to. |
| [Register a new tenant](register-tenant/) | Register a tenant on the Axis platform with an official tenant contact email, verify that contact channel, and… |

### Subscription plans

| Use case | Summary |
|---|---|
| [Change tenant plan (admin override)](admin-change-plan/) | Manually change a tenant's plan so that I can support early customers and testing without a billing integration. |
| [Enforce plan limits at the API](enforce-limits/) | Enforce subscription plan limits at the API so that Tenants cannot exceed their subscription without upgrading. |
| [View available plans](view-plans/) | Compare available subscription plans so that I can choose the one that fits my needs. |

### Tenant settings

| Use case | Summary |
|---|---|
| [Delete Tenant](delete-tenant/) | Permanently delete my Tenant so that all our data is removed from the platform. |
| [Update Tenant profile](tenant-profile/) | Update my Tenant's name and logo so that the platform reflects our brand. |
| [View tenant settings](tenant-settings/) | View all tenant settings in one place so that I have full visibility into the configuration. |

### Tenant isolation

| Use case | Summary |
|---|---|
| [Tenant resolution from JWT](tenant-from-jwt/) | Resolve the active tenant from the JWT on every request so that downstream code never needs to think about tenant… |
| [Automatic tenant scoping on every request](tenant-scope/) | Every database query to be automatically scoped to the requesting tenant so that data isolation is enforced at the… |

### Other

| Use case | Summary |
|---|---|
| [Open workspace start](workspace-start/) | Open the first authenticated workspace screen so that the user understands their account state, workspace access, and… |



---

## Diagrams

tenant onboarding journey (tenant contact email → verify → provisioning): [register-tenant § Diagrams](./register-tenant/README.md#diagrams) (`register-tenant-journey`, `tenant-provisioning`). First-owner identity setup is a separate setup-token handoff that continues at `/register` and is owned by `register-tenant`. Standalone users register through [identity-access/register-user](../identity-access/register-user/) without a tenant.

---

## Acceptance Criteria (domain)

- [ ] A new Tenant can register and be fully provisioned with isolated tenant schemas after tenant email verification.
- [ ] No tenant can read or write data belonging to another tenant under any circumstances.
- [ ] Tenant schema is automatically created and migrated on registration.
- [ ] Tenant can update its profile (name, logo, settings) without affecting other tenants.

---

## Implementation Status

| Layer | Status | Notes |
|---|---|---|
| Shared Domain | ✅ Done | `Entity`, `AggregateRoot`, `ValueObject`, `IDomainEvent`, `Result<T>` |
| Shared Application | ✅ Done | `ICommand/IQuery`, `ICommandHandler/IQueryHandler`, `ValidationBehavior`, `ITenantContext` |
| Shared Infrastructure | ✅ Done | `TenantSchemaInterceptor`, per-module `UnitOfWork` ([ADR-017](../../TECH_STACK.md#adr-017-axisshared-is-abstractions-only-no-shared-implementation)); **OpenTelemetry** host wiring on `Axis.Api` ([ADR-018](../../TECH_STACK.md#adr-018-opentelemetry-sdk-with-grafana-stack-for-observability), [patterns § OpenTelemetry](../../playbooks/patterns.md#opentelemetry-observability)) |
| [Register tenant](register-tenant/) | ⚠️ Partial | Backend/API split is implemented: tenant contact email + tenant verification + tenant provisioning stay here. Standalone user registration is complete in [identity-access/register-user](../identity-access/register-user/); first-user setup-token handoff polish and the dedicated register-tenant frontend remain here. |
| [Subscription plans](view-plans/) | ✅ Done | `GET /api/plans`, pricing data, 402 limits — see [enforce limits](enforce-limits/). Frontend pricing UI ⏳ |
| [Tenant isolation](tenant-scope/) | ✅ Done | `TenantSchemaInterceptor`, `TenantAccessMiddleware`, cross-tenant API tests |
| [Tenant management](tenant-profile/) | ✅ Done | Profile, settings + usage, scheduled deletion + hard-delete job ✅. Frontend settings UI ⏳ |
| Frontend | ⏳ Pending | Register-tenant journey (incl. verify screens), provisioning wait, settings, pricing |

---

## Open work (agents)

| Priority | Item | Where |
|----------|------|--------|
| Frontend | [Register tenant](register-tenant/) tenant onboarding, [pricing](view-plans/), [tenant settings](tenant-settings/) | see **Use Cases** table above |

Domain-level checkboxes above remain spec-only; status is in use-case **Implementation status** callouts.

---

## Dependencies

- None (this is the foundation)

## Dependents

- [Identity & Access](../identity-access/README.md)
- All other domains
