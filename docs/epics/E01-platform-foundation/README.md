# E01 — Platform Foundation

[← Back to Epics](../README.md)

---

## Overview

Establish the multi-tenant SaaS foundation that all other modules depend on. This epic covers tenant provisioning, organization lifecycle management, and the data isolation strategy that keeps each tenant's data completely separate.

## Business Value

Without this foundation, nothing else works. Every feature in every other epic runs on top of the multi-tenancy infrastructure built here.

## Phase

**MVP** — Must be completed first. All other epics depend on this.

---

## Features

| ID | Feature | Description |
|---|---|---|
| [F01](./features/F01-tenant-registration.md) | Tenant Registration & Provisioning | Self-service sign-up, org creation, schema provisioning |
| [F02](./features/F02-organization-management.md) | Organization Management | Edit org profile, settings, branding |
| [F03](./features/F03-tenant-isolation.md) | Tenant Data Isolation | Schema-per-tenant enforcement, middleware, tenant resolution |
| [F04](./features/F04-subscription-plans.md) | Subscription Plan Management | Plan tiers, feature gating, billing metadata |

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
| Tenant isolation (F03 US-008–009 backend) | ⚠️ Partial | `TenantSchemaInterceptor` + `TenantOrganizationAccessMiddleware` shipped. Cross-tenant API integration-test coverage is being reworked — see [F03](./features/F03-tenant-isolation.md) |
| Organization Management (F02 US-005–007 backend) | ✅ Done | Profile, settings + usage, scheduled deletion + hard-delete job ✅. Frontend settings UI ⏳ — see [F02](./features/F02-organization-management.md) |
| Frontend | ⏳ Pending | Registration, verify, provisioning, settings, pricing |

---

## Open work (agents)

| Priority | Item | Where |
|----------|------|--------|
| **Backend next** | F03: finish stable cross-tenant API integration-test coverage in current CI fixture | [F03](./features/F03-tenant-isolation.md) |
| Backend | Optional: F04 bulk workflow import when product needs US-011 bulk AC | [F04](./features/F04-subscription-plans.md) |
| Frontend | F01 US-002 verify + provisioning wait + auto sign-in; F04 pricing page; all F02 settings wireframes | [F01](./features/F01-tenant-registration.md), [F04](./features/F04-subscription-plans.md) |

Epic-level checkboxes above remain spec-only; status is in feature **Implementation status** callouts.

---

## Dependencies

- None (this is the foundation)

## Dependents

- [E02 — Identity & Access Management](../E02-identity-access/README.md)
- All other epics
