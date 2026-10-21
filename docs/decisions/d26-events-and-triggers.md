# D26. Events and triggers

Status: **Agreed**

M5 lets things that happen start processes. The event and trigger resources,
the raise event step, sync and async event triggers, data change triggers and
schedule triggers are built. See
[configuration](../reference/integration-resources.md#events-and-triggers) for the
`event` and `trigger` resources, and
[processes](../reference/process-events.md) for the raise event
step and how each trigger starts a process.

- **One record per start:** a process has a subject record
  ([D19](d19-process-resource-and-worker-host.md)). So every trigger
  starts a process for one record of the process's subject entity.
  - Starting an operation without a process comes later.
  - So do processes without a subject.
- **Event resource:** an `event` has a name, the `entity` it is about and
  typed payload `fields`, of scalar types only.
  - An occurrence of the event names one record of that entity.
  - Events raised by the platform itself come later. Data change triggers
    cover record changes.
- **Raise event step:** an `operation` step with `operation: "raiseEvent"`
  names an event about the subject entity. Its `fields` map payload fields to
  expressions over the subject record.
  - The step's transaction stores the occurrence in `axis.events`, with the
    subject record, the payload and the release.
  - Events are kept even when no trigger listens, so a later wait for event
    step can match early events.
- **Trigger resource:** a `trigger` names its trigger kind in `type`:
  `event`, `dataChange` or `schedule`. Its `kind` stays `trigger`, the
  resource kind, as with steps, fields and widgets. It names the `process` it
  starts.
  - Inbound endpoints are their own resource, in
    [D27](d27-inbound-endpoints.md).
- **Event trigger:** it names an `event` whose entity is the process's
  subject entity. It has an optional `when` over the payload, and a `mode` of
  `async`, the default, or `sync`.
  - **Async:** the worker delivers the occurrence. It starts the process at
    most once per occurrence and trigger, even after a worker restart.
  - **Sync:** the start commits in the raising step's transaction, so both
    commit or neither does.
- **Data change trigger:** it names an `entity` and `on`: `create`, `update`
  or both. It has optional `fields`, so it fires only when one of them
  changed, and an optional `when` over the new record.
  - The start commits in the write's transaction.
  - **Which writes:** it fires on every committed write through the record
    command. That is the record API, a task form save, an `updateRecord` step
    and a connector output.
  - **Delete waits:** `delete` comes later, because a deleted record cannot
    be a subject.
- **Starts that cannot happen:**
  - When the process already runs for the record, the trigger skips the
    start. This is no error, and it is the guard against loops.
  - A start whose `startCondition` is false is skipped too.
  - Both are logged.
- **Schedule trigger:** it has a 5-field cron `schedule` and a required IANA
  `timeZone`.
  - It names a `dataSource` whose root entity is the process's subject
    entity, with fixed parameter values. Each occurrence starts the process
    for each returned record.
  - **Once per occurrence:** each occurrence runs once per tenant, across all
    workers.
  - **Fan-out:** records are started in batches. Each record starts at most
    once per occurrence, also after a worker restart in the middle.
  - **After downtime:** only the latest missed occurrence runs, once.
  - **Daylight saving:** a local time that does not exist runs at the next
    valid time. A repeated local time runs once.
  - **Time zone:** the schedule's own time zone does not settle the
    application time zone rule of
    [D22](d22-current-time-and-time-zones.md).
- **Release, actor and audit:**
  - Triggers come from the active release.
  - A start from a trigger pins the release active at the start, as every
    start does.
  - Triggers run as `system` and are not checked by policies, like other
    system work in [D24](d24-roles-and-policies.md).
  - The `process.started` audit record names the trigger and, for an event,
    the occurrence.
- **Errors count as false:** a trigger's `when`, or the process's
  `startCondition`, that hits a run-time error counts as false. The start is
  skipped and logged, and the write or the raising step still commits.
- **`fields` on updates only:** a data change trigger's `fields` filter
  applies to updates. A create always passes it.
- **Plain data source:** a schedule trigger needs a data source that is not
  grouped. Its fan-out covers every row, in record id order, and ignores the
  data source's `pageSize` and `sort`.
- **Release when it fires:** triggers are read from the release that is
  active when the trigger fires. For an async event trigger, that is when the
  event is delivered, not when it was raised.
- **Skipped counts as handled:** a skipped start still counts as handled for
  that occurrence and trigger. It is never tried again.
- **No own audit record for an event:** raising an event writes
  `process.stepCompleted`, as other operations do. Its history output holds
  the event's id. The `axis.events` row records the occurrence.

_Why:_ see [what applications use most](../domain/knowledge.md#what-applications-use-most)
and [execution](../domain/knowledge.md#execution). Processes run per record
([D19](d19-process-resource-and-worker-host.md)), so a trigger always
names the record it starts for. The one-running-instance rule stops loops,
so data change triggers can fire on system writes and let processes chain.
Receipts make every start happen at most once, after a crash or a restart.
Triggers run as `system`, like other system work in
[D24](d24-roles-and-policies.md), and a schedule's own time zone leaves
the application rule of [D22](d22-current-time-and-time-zones.md) open.
A broken trigger never blocks a user's save or fails a running process. This
matches how D24 treats a policy filter that fails: it counts as false.
