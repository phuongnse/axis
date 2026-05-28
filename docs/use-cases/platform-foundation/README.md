# Platform Foundation

[← Back to Use Cases](../README.md)

---

## Overview

Establish the multi-tenant SaaS foundation that all other modules depend on. This domain covers tenant provisioning, organization lifecycle management, and the data isolation strategy that keeps each tenant's data completely separate.

## Business Value

Without this foundation, nothing else works. Every feature in every other domain runs on top of the multi-tenancy infrastructure built here.

## Phase

**MVP** — Must be completed first. All other domains depend on this.

---

## Use Cases

| Use case | Summary |
|---|---|
| [Automatic tenant provisioning](automatic-tenant-provisioning.md) | my organization's environment to be ready immediately after email verification so that I can start using the platform... |
| [Automatic tenant scoping on every request](automatic-tenant-scoping-on-every-request.md) | every database query to be automatically scoped to the requesting tenant so that data isolation is enforced at the in... |
| [Change organization plan (admin override)](change-organization-plan-admin-override.md) | manually change an organization's plan so that I can support early customers and testing without a billing integration. |
| [Delete organization](delete-organization.md) | permanently delete my organization so that all our data is removed from the platform. |
| [Enforce plan limits at the API](enforce-plan-limits-at-the-api.md) | API endpoints to enforce plan limits so that organizations cannot exceed their subscription without upgrading. |
| [Register a new organization](register-a-new-organization.md) | register my organization on the Axis platform so that I can start building workflows for my team. |
| [Select a subscription plan during registration](select-a-subscription-plan-during-registration.md) | choose a subscription plan during registration so that I know what features and limits I have access to. |
| [Tenant resolution from JWT](tenant-resolution-from-jwt.md) | resolve the active tenant from the JWT on every request so that downstream code never needs to think about tenant ide... |
| [Update organization profile](update-organization-profile.md) | update my organization's name and logo so that the platform reflects our brand. |
| [Verify email and activate account](verify-email-and-activate-account.md) | verify my email address so that my account is activated and I can access the platform. |
| [View available plans](view-available-plans.md) | compare available subscription plans so that I can choose the one that fits my needs. |
| [View organization settings](view-organization-settings.md) | view all organization settings in one place so that I have full visibility into the configuration. |


---

## Diagrams

![Tenant Provisioning Flow](./diagrams/tenant-provisioning.svg)

---

## Acceptance Criteria (domain)

- [ ] A new organization can register and be fully provisioned (own schema, admin account) within 60 seconds.
- [ ] No tenant can read or write data belonging to another tenant under any circumstances.
- [ ] Tenant schema is automatically created and migrated on registration.
- [ ] Organization can update its profile (name, logo, settings) without affecting other tenants.

---

## Implementation Status

| Layer | Status | Notes |
|---|---|---|
| Shared Domain | ✅ Done | `Entity`, `AggregateRoot`, `ValueObject`, `IDomainEvent`, `Result<T>` |
| Shared Application | ✅ Done | `ICommand/IQuery`, `ICommandHandler/IQueryHandler`, `ValidationBehavior`, `ITenantContext` |
| Shared Infrastructure | ✅ Done | `TenantSchemaInterceptor`, per-module `UnitOfWork` ([ADR-017](../../TECH_STACK.md#adr-017-axisshared-is-abstractions-only-no-shared-implementation)); **OpenTelemetry** host wiring on `Axis.Api` ([ADR-018](../../TECH_STACK.md#adr-018-opentelemetry-sdk-with-grafana-stack-for-observability), [patterns § OpenTelemetry](../../playbooks/patterns.md#opentelemetry-observability)) |
| [Register org](register-a-new-organization.md) | ✅ Done | Self-service signup + verification email — backend complete. Frontend polish ⏳ |
| [Tenant provisioning](automatic-tenant-provisioning.md) | ✅ Done | Kafka-driven per-module provisioning, coordinator retries, `GET /api/auth/provisioning-status`. Frontend wait screen ⏳ |
| [Subscription plans](view-available-plans.md) | ✅ Done | `GET /api/plans`, pricing data, 402 limits — see [enforce limits](enforce-plan-limits-at-the-api.md). Frontend pricing UI ⏳ |
| [Tenant isolation](automatic-tenant-scoping-on-every-request.md) | ✅ Done | `TenantSchemaInterceptor`, `TenantOrganizationAccessMiddleware`, cross-tenant API tests |
| [Organization management](update-organization-profile.md) | ✅ Done | Profile, settings + usage, scheduled deletion + hard-delete job ✅. Frontend settings UI ⏳ |
| Frontend | ⏳ Pending | Verify flow, provisioning wait, settings, pricing |

---

## Open work (agents)

| Priority | Item | Where |
|----------|------|--------|
| **Backend** | ✅ platform-foundation backend use cases complete. Optional: bulk workflow import when product needs [import-export](../workflow-builder/bulk-export-all-workflows.md) AC | [enforce-plan-limits-at-the-api.md](enforce-plan-limits-at-the-api.md) |
| Frontend | [Verify email](verify-email-and-activate-account.md), [provisioning wait](automatic-tenant-provisioning.md), [pricing](view-available-plans.md), [org settings](view-organization-settings.md) wireframes | see **Use Cases** table below |

Domain-level checkboxes above remain spec-only; status is in use-case **Implementation status** callouts.

---

## Dependencies

- None (this is the foundation)

## Dependents

- [Identity & Access](../identity-access/README.md)
- All other domains
