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

| Use case | Description |
|---|---|---|
| [Tenant Registration & Provisioning](tenant-registration.md) | Self-service sign-up, org creation, schema provisioning |
| [Organization Management](organization-management.md) | Edit org profile, settings, branding |
| [Tenant Data Isolation](tenant-isolation.md) | Schema-per-tenant enforcement, middleware, tenant resolution |
| [Subscription Plan Management](subscription-plans.md) | Plan tiers, feature gating, billing metadata |
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
| [Tenant registration](tenant-registration.md) | ✅ Done | Register org, email verification, plan selection, provisioning — backend complete. Frontend verify/provisioning/pricing ⏳ |
| [Subscription plans](subscription-plans.md) | ✅ Done | `GET /api/plans`, 402 limits (workflow / user / execution), Redis read-through counters, platform plan change. Frontend pricing UI ⏳. **Deferred:** atomic execution counter; fail-closed Redis. |
| Tenant provisioning | ✅ Done | Kafka-driven per-module provisioning, coordinator retries, `GET /api/auth/provisioning-status`. Frontend wait screen ⏳ — see [tenant-registration](tenant-registration.md) |
| [Tenant isolation](tenant-isolation.md) | ✅ Done | `TenantSchemaInterceptor`, `TenantOrganizationAccessMiddleware`, and cross-tenant API integration tests |
| [Organization management](organization-management.md) | ✅ Done | Profile, settings + usage, scheduled deletion + hard-delete job ✅. Frontend settings UI ⏳ |
| Frontend | ⏳ Pending | Verify flow, provisioning wait, settings, pricing |

---

## Open work (agents)

| Priority | Item | Where |
|----------|------|--------|
| **Backend** | ✅ platform-foundation backend use cases complete ([tenant-registration](tenant-registration.md) through [subscription-plans](subscription-plans.md)). Optional: bulk workflow import when product needs [import-export](../workflow-builder/import-export.md) bulk AC | [subscription-plans](subscription-plans.md) |
| Frontend | [Tenant registration](tenant-registration.md) email verification, provisioning wait, auto sign-in; [subscription-plans](subscription-plans.md) pricing page; [organization-management](organization-management.md) settings wireframes | [tenant-registration](tenant-registration.md), [subscription-plans](subscription-plans.md) |

Domain-level checkboxes above remain spec-only; status is in use-case **Implementation status** callouts.

---

## Dependencies

- None (this is the foundation)

## Dependents

- [Identity & Access](../identity-access/README.md)
- All other domains
