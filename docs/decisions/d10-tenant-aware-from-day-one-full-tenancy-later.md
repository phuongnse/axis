# D10. Tenant-aware from day one, full tenancy later

Status: **Agreed**

## Context

Axis serves several organizations from one installation. Adding tenant context afterwards touches
every query and job, which is expensive, while full tenant management before a second customer
delivers nothing.

## Decision

- **A database per tenant.** Each tenant has its own PostgreSQL database.
- **Every request and every job runs inside a resolved `TenantContext`,** and every database
  connection, cache key, file path, job and log scope carries the tenant.
- **Tenant management comes in its own decisions:** the tenant directory in
  [D42](d42-tenant-directory-in-the-platform-database.md), provisioning and migrations in
  [D43](d43-tenant-provisioning-and-migrations-across-tenant-databases.md), and cells in
  [D123](d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md).

The detail is in [tenancy](../architecture.md#tenancy-d10).

## Consequences

- A test with two tenant databases proves that no data leaks between them.
- One tenant must not take the others offline, which
  [D37](d37-health-checks-for-the-server-and-the-worker.md) follows.
