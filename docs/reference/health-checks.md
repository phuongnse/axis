# Health checks

The server, the worker and the agent host each serve `/health/live` and `/health/ready`, so that an
orchestrator can restart a host that hangs and route traffic only to a host that is ready. This page says what each
endpoint answers and when. The reasons are in
[D37](../decisions/d37-health-checks-for-the-server-and-the-worker.md).

## The answer

Each endpoint answers JSON of the form `{ "status": "Healthy" | "Degraded" | "Unhealthy" }` and
nothing else. `/health` needs no session, so the answer names no tenant, database or error. Healthy
and Degraded answer `200`, so a degraded host still passes a readiness probe. Unhealthy answers `503`.

## Server

The server's liveness says only that the process answers, and its readiness says whether the platform
database and the tenant databases answer.

**`/health/live`** runs no checks. It is Healthy, with status `200`, while the process answers
requests, and it is never Degraded or Unhealthy.

**`/health/ready`** answers this:

| Status    | HTTP  | When                                                                                                                |
| --------- | ----- | ------------------------------------------------------------------------------------------------------------------- |
| Healthy   | `200` | The platform database answers, startup activation has finished for every tenant, and every tenant database answers. |
| Degraded  | `200` | The platform database answers and activation has finished, but at least one tenant database does not answer.        |
| Unhealthy | `503` | The platform database does not answer, or startup activation has not finished for every tenant.                     |

- **One tenant down.** One tenant database that is down makes the server Degraded and not Unhealthy,
  so one tenant cannot take the others offline, see [tenancy](../architecture.md#tenancy-d10).
  Requests for that tenant fail and the others work.
- **Another schema version.** A tenant on another schema version than the server's is not served, so
  it counts neither against readiness nor as a tenant database that does not answer. One tenant whose
  migration failed cannot keep a deploy from being ready.
- **Timeouts.** The tenant database checks run in parallel, and each gives up after 2 seconds. A
  database that has not answered by then counts as not answering, so a hung database makes the server
  Degraded and does not stall the probe.
- **Naming the tenant.** The tenant is named in a warning in the logs on every probe, and in the gauge
  `axis.tenant.database.available`, never in the answer. The gauge is labelled `axis.tenant` with the
  tenant id. It is 1 when the tenant database answered the last probe and 0 when it did not. A scrape
  reads it from memory, so it never queries a tenant database. Before the first probe a tenant has no
  point.
- **Startup activation.** The server does not listen until startup activation has finished, see
  [startup activation](host-settings.md#startup-activation), so a probe gets no answer in that time.

### Under the directory source

Under `Tenancy:Source=directory`, the table holds until the first load of the
[tenant directory](../architecture.md#tenant-directory) has worked, and the server does not listen
before that. No startup activation runs, because `ActivateOnStartup` must be empty, so "startup
activation has finished" holds from the start.

After the first load, a platform database that does not answer is a failed refresh, and it makes the
server Degraded (`200`), not Unhealthy. So does a refresh that fails while the platform database still
answers. The server then keeps serving from the last good directory, and readiness returns to Healthy
at the next refresh that works.

## Worker

The worker serves only `/health/live` and `/health/ready`, on the address `Health:Urls`. The default
is `http://127.0.0.1:5208`, so nothing outside the host reaches it until someone changes the setting,
see [worker settings](process-execution.md#worker-settings).

The worker counts as stalled when the poll has not run, or one work item has run, for longer than
twice `Worker:LeaseDuration`, one minute by default. A poll interval would be too short, because one
work item can run for up to a lease.

| Endpoint        | Status    | HTTP  | When                                                                                                                               |
| --------------- | --------- | ----- | ---------------------------------------------------------------------------------------------------------------------------------- |
| `/health/live`  | Healthy   | `200` | The poll has run, and no work item has run, within twice `Worker:LeaseDuration`.                                                   |
| `/health/live`  | Unhealthy | `503` | The poll has not run, or one work item has run, for longer than that, so an orchestrator restarts the worker.                      |
| `/health/ready` | Healthy   | `200` | Every tenant database has its processes migrations, and the worker is not stalled.                                                 |
| `/health/ready` | Degraded  | `200` | A refresh of the tenant directory failed, and the last good read stays in use.                                                     |
| `/health/ready` | Unhealthy | `503` | A tenant database lacks its processes migrations, the first read of the tenant directory has not worked, or the worker is stalled. |

- **A tenant database that stops answering** after the worker saw it migrated is only logged, and does
  not make the worker Degraded.
- **A tenant that waits for its schema version**, see
  [tenant directory](../architecture.md#tenant-directory), counts as ready, and the worker stays
  Healthy while it waits. One tenant whose migration failed cannot keep the worker from being ready.
- **Under the directory source**, `/health/ready` answers `503` until the worker's first read of the
  tenant directory has worked, while `/health/live` stays `200`. A failed refresh of the directory makes
  the worker Degraded (`200`), and the worker keeps running the last good read. It is Healthy again
  after the next refresh that works.

## Agent host

The agent host serves only `/health/live` and `/health/ready`, on the address `Health:Urls`, and
answers `404` elsewhere. The default is `http://127.0.0.1:5209`, so nothing outside the host reaches it
until someone changes the setting.

Both endpoints are Healthy, with status `200`, while the process runs. The agent host has no check
that can make either one Degraded or Unhealthy.

## Docker Compose

The development `compose.yaml` at the repository root gives the worker a health check on
`/health/ready`. It runs inside the container, and Compose does not publish the worker's health port.
The production Compose example has no container health checks, because the production images hold no
`curl`. It publishes the worker's health port on `127.0.0.1` only, for probes from the host, see
[on premises](installation.md#on-premises).
