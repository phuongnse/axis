# D10. Tenant-aware from day one, full tenancy later

Status: **Agreed**

- **Database per tenant:** each tenant has its own PostgreSQL database.
- **Request scope:** every request and every background job runs inside a
  resolved `TenantContext`. All database connections, cache keys, file paths,
  jobs and log scopes carry the tenant.
- **First milestones:** tenants come from a configuration file. A test with
  two tenant databases proves that no data leaks between them.
- **Later milestone:** the tenant directory, provisioning, migrations across
  many databases and hosting profiles. The tenant directory is in
  [D42](d42-tenant-directory-in-the-platform-database.md). Provisioning
  and migrations across tenant databases are in
  [D43](d43-tenant-provisioning-and-migrations-across-tenant-databases.md).

_Why:_ adding tenant context afterwards touches every query and job, which is
expensive. Building full tenant management before there is a second customer
delivers nothing.
