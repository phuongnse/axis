# D37. Health checks for the server and the worker

Status: **Agreed**

The server's readiness covers the platform database and startup activation, and
one tenant database that is down makes the server degraded, not unhealthy. The
worker gets its own health endpoints. The server's readiness and the worker's
health endpoints are built. So is the tenant metric. See
[health checks](../architecture.md#health-checks) for the answers
of each endpoint.
The owner confirmed these points on issue #480 on 2026-10-17.

- **Server:**
  - `/health/live` stays as it is: the process answers.
  - `/health/ready` is healthy when the platform database answers and startup
    activation has finished for every tenant.
  - A tenant database that does not answer makes the server **degraded**, not
    unhealthy. Readiness still passes, so one tenant cannot take the others
    offline. The tenant is named in the logs and in a metric, not in the
    answer.
  - The answer holds only the overall status. It names no tenant, database or
    error, because `/health` needs no session.
- **Worker:**
  - The worker serves `/health/live` and `/health/ready` on its own address,
    the setting `Health:Urls`. The default is `http://127.0.0.1:5208`. It
    serves nothing else.
  - Ready means every tenant database is migrated and the worker's loop has
    polled within twice `Worker:LeaseDuration`, one minute by default. A poll
    interval would be too short, because one work item can run for up to a
    lease.
  - A worker loop that stops polling makes `/health/live` fail, so an
    orchestrator restarts it.
  - Docker Compose gives the worker a health check on `/health/ready`.
- **Tenant directory:**
  [D42](d42-tenant-directory-in-the-platform-database.md) adds a
  Degraded state on both hosts for a failed directory refresh. The
  worker is then no longer never Degraded. Both are built.

_Why:_ [Operations](../domain/knowledge.md#operations) asks that failed and stuck
work is visible. Without a health endpoint, a stuck worker shows only when
work piles up, and Compose cannot wait for it. The server's
readiness ignores tenant databases and activation. Degraded, not unhealthy,
follows [D10](d10-tenant-aware-from-day-one-full-tenancy-later.md): one
tenant must not take the others offline. `/health` needs no session, so the
answer names nothing that an open endpoint should not show.
