# Platform Foundation

[← Back to Use Cases](../README.md)

---

## Overview

Establish the multi-tenant SaaS foundation that all other modules depend on. This epic covers tenant provisioning, organization lifecycle management, and the data isolation strategy that keeps each tenant's data completely separate.

## Business Value

Without this foundation, nothing else works. Every feature in every other epic runs on top of the multi-tenancy infrastructure built here.

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

## Acceptance Criteria (Epic Level)

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
| Tenant Registration (US-001–004 backend) | ✅ Done | US-001–004 backend complete. Frontend verify/provisioning/pricing ⏳ |
| Subscription Plans (F04 US-010–012 backend) | ✅ Done | `GET /api/plans`, 402 limits (workflow / user / execution), Redis read-through counters, platform plan change. Frontend pricing UI ⏳. **Deferred:** atomic execution counter; fail-closed Redis. |
| Tenant Provisioning (US-003 backend) | ✅ Done | Kafka-driven per-module provisioning, coordinator retries, `GET /api/auth/provisioning-status`. Frontend wait screen ⏳ |
| Tenant isolation (F03 US-008–009 backend) | ✅ Done | `TenantSchemaInterceptor`, `TenantOrganizationAccessMiddleware`, and cross-tenant API integration tests — see [F03](tenant-isolation.md) |
| Organization Management (F02 US-005–007 backend) | ✅ Done | Profile, settings + usage, scheduled deletion + hard-delete job ✅. Frontend settings UI ⏳ — see [F02](organization-management.md) |
| Frontend | ⏳ Pending | Verify flow, provisioning wait, settings, pricing |

---

## Open work (agents)

| Priority | Item | Where |
|----------|------|--------|
| **Backend** | ✅ E01 backend US complete (F01–F04). Optional: F04 bulk workflow import when product needs US-011 bulk AC | [F04](subscription-plans.md) |
| Frontend | F01 US-002 verify + provisioning wait + auto sign-in; F04 pricing page; all F02 settings wireframes | [F01](tenant-registration.md), [F04](subscription-plans.md) |

Epic-level checkboxes above remain spec-only; status is in use-case **Implementation status** callouts.

---

## Dependencies

- None (this is the foundation)

## Dependents

- [E02 — Identity & Access Management](../identity-access/README.md)
- All other domains
