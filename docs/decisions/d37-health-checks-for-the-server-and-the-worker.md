# D37. Health checks for the server and the worker

Status: **Agreed**

## Context

[Operations](../domain/knowledge.md#operations) asks that failed and stuck work is visible. Without a
health endpoint, a stuck worker shows only when work piles up, and Docker Compose cannot wait for it.
A server readiness that ignores the tenant databases and activation says little.

## Decision

- **The server's readiness covers the platform database and startup activation.**
- **A tenant database that is down makes the server Degraded, not Unhealthy,** so readiness still
  passes and one tenant cannot take the others offline, as
  [D10](d10-tenant-aware-from-day-one-full-tenancy-later.md) asks. The tenant is named in the logs
  and a metric.
- **The answer holds only the overall status,** because `/health` needs no session.
- **The worker serves its own `/health/live` and `/health/ready`** on an address that serves nothing
  else. A loop that stops polling fails liveness, so an orchestrator restarts it.
- **A failed refresh of the tenant directory makes both hosts Degraded,** as
  [D42](d42-tenant-directory-in-the-platform-database.md) says.

Each endpoint's answers are in [health checks](../reference/health-checks.md).

## Consequences

- A stuck worker shows on its probe, and Compose can wait for it.
- A probe never shows which tenant fails, so an operator reads the logs or the metric.
