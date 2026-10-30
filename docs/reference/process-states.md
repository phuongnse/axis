# Process instance and task states

The states of a process instance and of a task, moved out of [processes.md](processes.md), which holds the
`process` resource, its steps and its tables. The conventions of [architecture.md](../architecture.md) apply:
sections marked _(planned for Mx)_ are not built yet.

## Instance states

| State | Meaning |
| --- | --- |
| `running` | The instance has work that is ready or claimed by a worker. |
| `waiting` | The instance waits for a human task to be completed or for a connector call to settle, see [Connector calls](process-execution.md#connector-calls). It also waits for a package operation call, see [Package operations](process-execution.md#package-operations), for the child of a sub-process step, see [Sub-process step](process-flow.md#sub-process-step), and for a timer, see [Timer step](process-flow.md#timer-step). It also waits for an event, see [process flow](process-flow.md). |
| `completed` | The instance reached an `end` step. |
| `failed` | A step failed. The error is in the history. |
| `cancelled` | An operator cancelled the instance while it was `running` or `waiting`. It is an end state. See [Cancel](process-inspection.md#cancel). |

Only `running` and `waiting` block a new start for the same process and
record. A `failed` or `cancelled` instance does not, so it never locks its
record. An operator can cancel a `running` or `waiting` instance, see
[Cancel](process-inspection.md#cancel). An operator can retry a `failed` instance, see [Retry](process-inspection.md#retry).

## Task states

| State | Meaning |
| --- | --- |
| `open` | The task waits for a decision. |
| `completed` | The decision is recorded, and the instance moves on. |
| `cancelled` | An operator cancelled the task's instance. The task has no decision and leaves every inbox. See [Cancel](process-inspection.md#cancel). |
| `expired` | The task's `deadline` passed while it was `open`, and the instance moved to the deadline's `next` step. The task has no decision and leaves every inbox. It cannot be completed. See [Escalations and deadline](task-deadlines.md#escalations-and-deadline). |

A task has no other state. Its due date is only shown, so there is no overdue
state. The task API reports an open task past its due date as overdue, see
[Task states](task-deadlines.md#task-states).
