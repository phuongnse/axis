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

![Tenant Provisioning Flow](./diagrams/tenant-provisioning.png)

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
| Shared Infrastructure | ✅ Done | `AxisDbContext`, `TenantSchemaInterceptor`, `UnitOfWork`, `MessageBus` |
| Tenant Registration (US-001–002) | 🚧 In Progress | Domain + Application + Infrastructure done (in Identity module); API + email verification endpoint pending |
| Tenant Provisioning (US-003) | ⏳ Pending | Schema auto-creation on registration not yet wired |
| Organization Management (F02) | ⏳ Pending | — |
| Subscription Plans (F04) | ⏳ Pending | — |
| API | ⏳ Pending | — |
| Frontend | ⏳ Pending | — |

---

## Dependencies

- None (this is the foundation)

## Dependents

- [E02 — Identity & Access Management](../E02-identity-access/README.md)
- All other epics
