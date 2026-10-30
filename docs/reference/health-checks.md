# Health checks

The server's `/health/live` and `/health/ready` work as described below, as
[D37](../decisions/d37-health-checks-for-the-server-and-the-worker.md)
says.

**The answer.** Each endpoint answers JSON of the form
`{ "status": "Healthy" | "Degraded" | "Unhealthy" }` and nothing else.
`/health` needs no session, so the answer names no tenant, database or error.
Healthy and Degraded answer `200`, so a degraded host still passes a readiness
probe. Unhealthy answers `503`.

**Server `/health/live`.** It runs no checks. It is Healthy while the process
answers requests, with status `200`. It is never Degraded or Unhealthy.

**Server `/health/ready`.**

| Status    | HTTP  | When                                                                                                                |
| --------- | ----- | ------------------------------------------------------------------------------------------------------------------- |
| Healthy   | `200` | The platform database answers, startup activation has finished for every tenant, and every tenant database answers. |
| Degraded  | `200` | The platform database answers and activation has finished, but at least one tenant database does not answer.        |
| Unhealthy | `503` | The platform database does not answer, or startup activation has not finished for every tenant.                     |

Under `Tenancy:Source=directory`, the rows above hold
until the first load of the [tenant directory](../architecture.md#tenant-directory) has worked.
The server does not listen before that. After the first load, a platform
database that does not answer is a failed refresh, and it makes the server
Degraded (`200`), not Unhealthy. So does a refresh that fails while the platform
database still answers. The server then keeps serving from the last good
directory. Readiness returns to Healthy at the next refresh that works. Under
`directory`, no startup activation runs, because `ActivateOnStartup` must be
empty, so "startup activation has finished" holds from the start.

- A tenant on another schema version than the server's is not served, so it
  counts neither against readiness nor as a tenant database that does not
  answer. One tenant whose migration failed cannot keep a deploy from being
  ready.
- One tenant database that is down makes the server Degraded and not
  Unhealthy, so one tenant cannot take the others offline, see
  [tenancy](../architecture.md#tenancy-d10). Requests for that tenant fail and the others work.
- The tenant database checks run in parallel, and each gives up after 2
  seconds. A database that has not answered by then counts as not answering,
  so a hung database makes the server Degraded and does not stall the probe.
- The tenant is named in a warning in the logs on every probe, and in the
  gauge `axis.tenant.database.available`, never in the answer. The gauge is
  labelled `axis.tenant` with the tenant id. It is 1 when the tenant database
  answered the last probe and 0 when it did not. A scrape reads it from memory,
  so it never queries a tenant database. Before the first probe a tenant has no
  point.
- Today the server does not listen until startup activation has finished, see
  [startup activation](host-settings.md#startup-activation). So a
  probe gets no answer in that time. The activation rule matters once the
  server listens earlier.

**Worker.** The worker serves only `/health/live` and `/health/ready`, on the
address `Health:Urls`. The default is `http://127.0.0.1:5208`, so nothing
outside the host reaches it until someone changes the setting. See
[worker settings](process-execution.md#worker-settings). A tenant database
that stops answering after the worker saw it migrated is only logged, and does
not make the worker Degraded. Under `Tenancy:Source=directory`, the worker
answers `503` on `/health/ready` until its first read of the
[tenant directory](../architecture.md#tenant-directory) has worked, while `/health/live` stays
`200`. A failed refresh of the directory makes the worker Degraded (`200`),
and the worker keeps running the last good read. It is Healthy again after the
next refresh that works.

A tenant that waits for its schema version, see
[tenant directory](../architecture.md#tenant-directory), counts as ready for `/health/ready`, and
the worker stays Healthy while it waits. One tenant whose migration failed
cannot keep the worker from being ready.

The loop counts as stalled when it has not polled within twice
`Worker:LeaseDuration`, one minute by default. A poll interval would be too
short, because one work item can run for up to a lease.

| Endpoint        | Status    | HTTP  | When                                                                                                                                                  |
| --------------- | --------- | ----- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| `/health/live`  | Healthy   | `200` | The loop has polled within twice `Worker:LeaseDuration`.                                                                                              |
| `/health/live`  | Unhealthy | `503` | The loop has not polled within that time, so an orchestrator restarts the worker.                                                                     |
| `/health/ready` | Healthy   | `200` | Every tenant database has its processes migrations, and the loop has polled within twice `Worker:LeaseDuration`.                                      |
| `/health/ready` | Degraded  | `200` | A refresh of the tenant directory failed, and the last good read stays in use.                                                                        |
| `/health/ready` | Unhealthy | `503` | A tenant database lacks its processes migrations, the first read of the tenant directory has not worked, or the loop has not polled within that time. |

**Docker Compose.** The worker gets a health check on `/health/ready`. It runs
inside the container, and Compose does not publish the worker's health port.
The production Compose example has no container health checks, because the
production images hold no `curl`. It publishes the worker's health port on
`127.0.0.1` only, for probes from the host.
