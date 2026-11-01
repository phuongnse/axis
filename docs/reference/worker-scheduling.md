# Worker scheduling

Detailed reference for how the worker shares its time between the tenants of a [cell](cells.md). It
follows [D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) and
[D123](../decisions/d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md).
Everything in this file is _(planned for M31)_. It applies to every installation, not only to a cell,
so the worker has one way to run. How the worker runs one work item is in
[process execution](process-execution.md), and it does not change. The conventions of
[architecture.md](../architecture.md) apply.

**Today.** The worker has one loop. It visits the tenants one after another, and runs a tenant until
it has no work item left, then polls every tenant again after `Worker:PollInterval`. So a tenant with a
large backlog holds back every other tenant, and an idle cell still polls every database each round.

## Fair share

The worker runs weighted deficit round-robin over the tenants that have work due.

- **Active list.** The worker keeps the tenants that have work due in a list, in round order. A
  tenant joins at the end when a poll finds work due, or when it is woken. It leaves when a claim
  finds nothing due.
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
  next tenant, and the tenant keeps its place in the list.
- **Housekeeping.** Deleting expired sessions, sweeping files and clearing old notifications and
  import files run on a timer of their own for each tenant, at most every 5 minutes, outside the
  round. Today they run at each poll.
- **What a weight buys.** A tenant of weight 2 runs twice as many items in a round as a tenant of
  weight 1 when both have a backlog. A tenant alone in the list runs at full speed, whatever its
  weight.

## Waking a tenant

An idle tenant is polled less and less often, and a wake signal brings it back at once.

- **Back-off.** After a poll that finds no work due, the tenant's next poll waits twice as long as the
  last wait, from `Worker:PollInterval` up to `Worker:IdlePollCeiling`, 30 seconds by default. A poll
  that finds work resets the wait.
- **Next due time.** A poll also reads the earliest `due_at` of the tenant's pending work items, such
  as a timer, a retry or an escalation. The worker polls the tenant again at that time, if it comes
  before the back-off ends.
- **Wake signal.** When the server or the publisher commits a transaction that adds a work item due
  now, it sends `NOTIFY axis_work, '<tenant id>'` on the platform database, after the commit. The
  worker holds one direct connection that runs `LISTEN axis_work`, see
  [connections](cells.md#connections). A signal puts the tenant at the end of the active list and
  resets its back-off. An installation under `Tenancy:Source=configuration` with no platform database
  sends no signal, and its worker finds work by polling alone.
- **A lost signal.** A signal is a hint, never the only way work is found. A lost one delays work by
  at most `Worker:IdlePollCeiling`. When the `LISTEN` connection drops, the worker connects again and
  then polls every tenant once, as if each were woken.
- **What wakes whom.** A signal names only a tenant id. It holds no record, process or user.

## Metrics

- `axis.work_items.claim_delay` keeps its `axis.tenant` attribute, as in the
  [worker metrics](process-execution.md#worker-metrics). The [cell load test](performance.md#cell-load-test) checks it for every
  tenant, not only the busy ones.
- `axis.worker.active_tenants`, a gauge, is the length of the active list.
- `axis.worker.polls`, a counter, counts the polls with the attribute `axis.poll.found` `true` or `false`, so the cost of idle tenants shows.
