# Worker scheduling

Detailed reference for how the worker shares its time between the tenants of a [cell](cells.md). It
follows [D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) and
[D123](../decisions/d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md).
[Fair share](#fair-share), the concurrency quotas, the wake signal, back-off, the next due time and the
metrics are built. All of it applies to every installation, not only to a cell, so the worker has
one way to run. How the worker runs one work item is in
[process execution](process-execution.md), and it does not change. The conventions of
[architecture.md](../architecture.md) apply.

**Today.** Each tenant has its own next poll time. A poll checks that the tenant is ready, reads its earliest
due time, and adds the tenant to the active list when it has work due. The loops run the tenants in the list.
A tenant leaves the list when a claim finds nothing due and none of its other items runs, or when a run fails, and
a later poll adds it again. A tenant that has work is polled every `Worker:PollInterval`. A tenant with none is polled
less and less often, so an idle cell does not poll every database each round, see [waking a tenant](#waking-a-tenant).
Under `Tenancy:Source=directory` the worker also listens for the wake signal that the server and the publisher send.
A signal for a known tenant checks that tenant and adds it to the list at once, so new work does not wait for the next poll.

## Fair share

The worker runs weighted deficit round-robin over the tenants that have work due.

- **Active list.** The worker keeps the tenants that have work due in a list, in round order. A
  tenant joins at the end when a poll finds work due or when it is woken. It
  leaves when a claim finds nothing due and none of its other items runs, because a running item may
  queue the next one. While other items of the tenant run, an empty claim only pauses the tenant until
  one of them ends. A poll adds every ready tenant in one step, in id order, so
  the first tenant does not run alone while the checks of the next one run.
- **Quantum and weight.** Each round, every tenant in the list gets `Worker:Quantum` work items
  times its `workerWeight`, see [quotas](tenant-quotas.md#quotas), added to its deficit. The quantum
  is 10 by default and the weight 1. A tenant runs items while its deficit is at least 1, and each
  item it runs takes 1. Then the next tenant runs. A tenant whose list entry ends keeps no deficit,
  so an idle tenant cannot save up a burst.
- **Several loops.** The worker runs `Worker:Concurrency` loops, 4 by default, that share the active
  list. Each takes the next tenant whose turn it is. Two loops may run the same tenant at once, as
  two workers may today: the claim takes items with `FOR UPDATE SKIP LOCKED`, so no item runs twice.
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

- **Back-off.** After a poll that finds no work due, the tenant's next poll waits twice as long as the
  last wait, from `Worker:PollInterval` up to `Worker:IdlePollCeiling`, 30 seconds by default. A poll
  that finds work resets the wait. A tenant that is not ready, or whose read failed, is tried again
  every `Worker:PollInterval` with no back-off, so a tenant that waits for its migrations or comes back from a
  database outage does not wait long. `Worker:IdlePollCeiling` must be at least `Worker:PollInterval`, or the
  worker stops at start.
- **Next due time.** A poll also reads the earliest `due_at` of the tenant's claimable work items, such
  as a timer, a retry or an escalation. They are the items of the kinds the worker handles that no live
  lease holds, the same ones a claim takes. The worker polls the tenant again at that time, if it comes
  before the back-off ends. The time is measured on the database clock.
- **Schema versions and directory.** The worker reads the recorded schema versions, reads the tenant directory
  again when `Tenancy:RefreshInterval` has passed, and reports that it is alive, at most every `Worker:PollInterval`,
  whatever the polls of the tenants are. A tenant's housekeeping runs when the tenant itself is polled, so at least
  once per `Worker:IdlePollCeiling`.
- **Wake signal.** When the server or the publisher commits a transaction that adds a work item due
  now, it sends `NOTIFY axis_work, '<tenant id>'` on the platform database, after the commit. This
  sending side is built: the server sends once for each tenant a request touched, when the request
  ends, and the publisher sends for the tenant it published or rolled back to, when the activation
  left a schedule trigger past due. Each send is cut off after about 2 seconds, and a failed send is
  logged and never fails the request or the publish. The receiving side is built: the
  worker holds one direct connection, named `axis-worker-listen`, that runs `LISTEN axis_work`, see
  [connections](cells.md#connections). A signal for a known tenant checks that tenant and puts it at the end of the active
  list at once. It runs no housekeeping. It resets the tenant's back-off, so the tenant's next poll comes
  at most one `Worker:PollInterval` later, and a next poll that was due sooner, such as a timer, is not moved later.
  A signal that names an unknown tenant is ignored. An installation under `Tenancy:Source=configuration` with no platform database
  sends no signal, and its worker finds work by polling alone.
- **A lost signal.** A signal is a hint, never the only way work is found. A lost one delays work by
  at most `Worker:IdlePollCeiling`. When
  the `LISTEN` connection drops, the worker connects again at once, retries every `Worker:PollInterval` while
  that fails, and then resets every tenant's back-off and polls every tenant, as if each were woken. It does the same after the first connection.
- **What wakes whom.** A signal names only a tenant id. It holds no record, process or user.

## Metrics

- `axis.work_items.claim_delay` keeps its `axis.tenant` attribute, as in the
  [worker metrics](process-execution.md#worker-metrics). The [cell load test](performance.md#cell-load-test) _(planned for M31)_ checks it for every
  tenant, not only the busy ones.
- `axis.worker.active_tenants`, a gauge, is the length of the active list. It has no attributes.
- `axis.worker.polls`, a counter, counts the polls of a tenant with the attribute `axis.poll.found` `true` or `false`, so the cost of idle tenants shows. It has no tenant attribute.
