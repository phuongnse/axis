# D69. Timer, wait for event and sub-process steps, loops and create record

Status: **Agreed**

M14 gives processes three new step types, loops and a `createRecord`
operation. The owner agreed this on 2026-10-25. It is _(planned for M14)_. See
[process flow](../reference/process-flow.md). It replaces the "Wait for
event, timer and sub-process come later" point of
[D19](d19-process-resource-and-worker-host.md), and the "Reopen without a
loop" point of [D56](d56-a-service-desk-as-the-third-sample-application.md).

- **Timer step:** `{ "type": "timer", "wait": "PT4H", "next" }`, or `until`,
  a `date-time` expression, instead of `wait`.
  - `wait` is a fixed-length duration, by the rule of `dueIn` (`AXC0084`).
  - The step's transaction sets the instance `waiting` and queues a
    `process.timer` work item due at the time. An `until` in the past is due at
    once. An `until` that gives `null` fails the step.
  - The item resumes the instance at the revision it waits at, so a stale item
    does nothing.
- **Wait for event step:** `{ "type": "waitEvent", "event", "when", "timeout":
  { "after", "next" }, "next" }`. `when` and `timeout` are optional.
  - The event's entity is the subject entity. The step waits for an occurrence
    about the instance's own subject record, raised after the instance
    started.
  - It takes the earliest such occurrence that this instance has not used yet
    and whose `when`, over the payload, is true. A table of matches,
    unique per instance and occurrence, makes each occurrence wake one wait
    once, also across the passes of a loop.
  - When a match already exists, the step goes on at once. Otherwise the
    instance waits. Event delivery wakes waiting instances as well as starting
    triggers, see [D72](d72-a-delivery-limit-and-dead-letters.md).
  - `timeout.after` is a fixed-length duration. When it passes first, the
    instance takes `timeout.next`. The revision check lets only the first of
    the event and the timeout win.
- **Sub-process step:** `{ "type": "subProcess", "process", "subject",
  "parameters", "next" }`.
  - `subject` is a reference expression to the child's subject record. It may
    be left out when the child's subject entity is the parent's, and then
    names the same record. A child without a subject takes `parameters`
    instead, see [D70](d70-processes-without-a-subject-scheduled-jobs-and-delete-triggers.md).
  - The child is pinned to the parent's release, not the active one, so the
    contract the compiler checked holds.
  - The parent waits. A child that completes resumes the parent at `next`. A
    child that fails leaves the parent `waiting`, and an operator retries the
    child. A cancelled child fails the parent's step. Cancelling the parent
    cancels its running children.
  - A child that cannot start, because it already runs for that record or its
    `startCondition` is not true, fails the parent's step.
  - A cycle in the graph of processes that call each other is a new
    diagnostic.
- **Loops:** a cycle is allowed when every cycle passes through a waiting
  step: a `task`, a `timer` or a `waitEvent`. `AXC0073` then reports only a
  cycle with no waiting step, which would run for ever.
  - Each pass writes new history rows, told apart by revision.
  - A step can stage a connector call or a package call once per pass. The
    unique index on the call tables moves from (instance, step) to (instance,
    step, staged revision). Retry and resolve act on the latest call of the
    step.
- **Create record:** `{ "type": "operation", "operation": "createRecord",
  "entity", "set", "next" }`.
  - `entity` names a root entity. `set` maps its fields to expressions with
    the scope of a `when`.
  - It creates the record through the record create command, as `system`, in
    the step's transaction. Sequences, computed fields, validations, field
    defaults, the `record.created` audit record and data change triggers all
    apply. A creator field gets `system`. A validation failure fails the step.
  - Process expressions gain `id`, the subject record's id, typed as a
    reference to the subject entity. So `"set": { "ticket": "id" }` links the
    new record back.
  - Its history output is `{ "next", "recordId" }`.
- **Service desk:** a reopened ticket loops back to the agent's task, in the
  same instance. The status `reopened` and submitting the ticket again go.
- **Effect on earlier decisions:** D19 and D56 link here where this decision
  replaces their points.

_Why:_ see [knowledge](../domain/knowledge.md#execution). Waiting on a timer or
an event is a step of its own, so the instance stays `waiting` in the database
and survives restarts, as a task does. A loop that passes through a waiting
step cannot spin, so the old ban on cycles can go. Matching events by subject
and using each occurrence once keeps a loop from consuming the same event
twice. A child pinned to its parent's release keeps the compiled contract
between them. Creating records through the record command keeps one write path
with every rule.
