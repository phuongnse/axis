# Worker scheduling

The worker shares its time between tenants, so that one tenant with a large backlog cannot hold back
the others. This page covers how the worker finds the tenants that have work, how it shares its time
between them, how an idle tenant is woken, and the metrics that show it. The same scheduling runs on
every installation, a [cell](cells.md) or not, so the worker has one way to run. How the worker runs
one work item is in [process execution](process-execution.md).

The worker polls each tenant on a schedule of its own. A poll checks that the tenant is ready, reads
its earliest due time, and adds the tenant to the active list when it has work due, and the worker's
loops run the tenants in that list. A tenant that has work is polled every `Worker:PollInterval`, and
a tenant with none is polled less and less often, so an idle cell does not poll every database each
round. Under `Tenancy:Source=directory` a wake signal from the server or the publisher adds a tenant
to the list at once, so new work does not wait for the next poll.

## Fair share

The worker runs weighted deficit round-robin over the tenants that have work due.

- **Active list.** The worker keeps the tenants that have work due in a list, in round order. A
  tenant joins at the end when a poll finds work due or when it is woken. A poll adds every ready
  tenant in one step, in id order, so the first tenant does not run alone while the checks of the
  next one run.
- **Leaving the list.** A tenant leaves the list when a claim finds nothing due and none of its other
  items runs, because a running item may queue the next one, or when a run fails. While other items
  of the tenant run, an empty claim only pauses the tenant until one of them ends. A later poll adds
  the tenant again.
- **Quantum and weight.** Each round, every tenant in the list gets `Worker:Quantum` work items
  times its `workerWeight`, see [quotas](tenant-quotas.md#quotas), added to its deficit. The quantum
  is 10 by default and the weight 1. A tenant runs items while its deficit is at least 1, and each
  item it runs takes 1. Then the next tenant runs. A tenant whose list entry ends keeps no deficit, so
  an idle tenant cannot save up a burst.
- **Several loops.** The worker runs `Worker:Concurrency` loops, 4 by default, that share the active
  list. Each takes the next tenant whose turn it is. Two loops may run the same tenant at once, as two
  workers may: the claim takes items with `FOR UPDATE SKIP LOCKED`, so no item runs twice.
- **Several workers.** Each worker keeps its own list and deficits. Over several workers the shares
  stay fair on average, with no coordination between them.
- **Concurrency quotas.** In a cell, a connector or package call needs a lease of the tenant's
  `concurrentConnectorCalls` quota before it is claimed, see
  [work at once](tenant-quotas.md#work-at-once). With no lease free, the tenant's turn passes to the
  next tenant, and the tenant keeps its place in the list. The tenant is idle until one of its items
  ends in that worker, or until the next poll.
- **Housekeeping.** Deleting expired sessions, sweeping files and clearing old notifications and
  import files run on a timer of their own for each tenant, at most every 5 minutes, outside the
  round.
- **What a weight buys.** A tenant of weight 2 runs twice as many items in a round as a tenant of
  weight 1 when both have a backlog. A tenant alone in the list runs at full speed, whatever its
  weight.

## Waking a tenant

An idle tenant is polled less and less often, and a wake signal brings it back at once.

- **Back-off.** After a poll that finds no work due, the tenant's next poll waits twice as long as
  the last wait, from `Worker:PollInterval` up to `Worker:IdlePollCeiling`, 30 seconds by default. A
  poll that finds work resets the wait. A tenant that is not ready, or whose read failed, is tried
  again every `Worker:PollInterval` with no back-off, so a tenant that waits for its migrations or
  comes back from a database outage does not wait long. `Worker:IdlePollCeiling` must be at least
  `Worker:PollInterval`, or the worker stops at start.
- **Next due time.** A poll also reads the earliest `due_at` of the tenant's claimable work items,
  such as a timer, a retry or an escalation: the items of the kinds the worker handles that no live
  lease holds, the same ones a claim takes. The worker polls the tenant again at that time, if it
  comes before the back-off ends. The time is measured on the database clock.
- **Schema versions and directory.** The worker reads the recorded schema versions, reads the tenant
  directory again when `Tenancy:RefreshInterval` has passed, and reports that it is alive, at most
  every `Worker:PollInterval`, whatever the polls of the tenants are. A tenant's housekeeping runs
  when the tenant itself is polled, so at least once per `Worker:IdlePollCeiling`.
- **Sending the signal.** When the server or the publisher commits a transaction that adds a work
  item due now, it sends `NOTIFY axis_work, '<tenant id>'` on the platform database, after the
  commit. The server sends once for each tenant that a request touched, when the request ends. The
  publisher sends for the tenant it published to or activated a stored release in, when the
  activation left a schedule trigger past due. Each send is cut off after about 2 seconds, and a failed send is logged and never
  fails the request or the publish. An installation under `Tenancy:Source=configuration` with no
  platform database sends no signal, and its worker finds work by polling alone.
- **Receiving the signal.** The worker holds one direct connection, named `axis-worker-listen`, that
  runs `LISTEN axis_work`, see [connections](cells.md#connections). A signal for a known tenant
  checks that tenant and puts it at the end of the active list at once, and runs no housekeeping. It
  resets the tenant's back-off, so the tenant's next poll comes at most one `Worker:PollInterval`
  later, and a next poll that was due sooner, such as a timer, is not moved later. A signal that
  names an unknown tenant is ignored.
- **A lost signal.** A signal is a hint, never the only way work is found, so a lost one delays work
  by at most `Worker:IdlePollCeiling`. When the `LISTEN` connection drops, the worker connects again
  at once and retries every `Worker:PollInterval` while that fails. Then it resets every tenant's
  back-off and polls every tenant, as if each were woken. It does the same after the first
  connection.
- **What wakes whom.** A signal names only a tenant id. It holds no record, process or user.

## Metrics

The worker's metrics show whether the shares stay fair and what idle tenants cost.

| Metric | Instrument | Attributes | What it shows |
| --- | --- | --- | --- |
| `axis.work_items.claim_delay` | As in the [worker metrics](process-execution.md#worker-metrics) | `axis.tenant` | The [cell load test](performance.md#cell-load-test) checks it for every tenant but the one with the backlog, not only the busy ones |
| `axis.worker.active_tenants` | Gauge | None | The length of the active list |
| `axis.worker.polls` | Counter | `axis.poll.found`, `true` or `false`, and no tenant | The polls of a tenant, so the cost of idle tenants shows |
