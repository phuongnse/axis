# Process instance and task states

A process instance and a task each move through a small set of states, which decide what may happen
to them next. This page lists both. The `process` resource and its steps are in
[processes](processes.md).

## Instance states

An instance is `running` or `waiting` while it has work left, and ends in one of three end states.

| State       | Meaning                                                                                                                                                                                  |
| ----------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `running`   | The instance has work that is ready or claimed by a worker.                                                                                                                              |
| `waiting`   | The instance waits for something outside its own steps, as the list below says.                                                                                                         |
| `completed` | The instance reached an `end` step.                                                                                                                                                      |
| `failed`    | A step failed. The error is in the history.                                                                                                                                              |
| `cancelled` | An operator cancelled the instance while it was `running` or `waiting`. It is an end state. See [Cancel](process-inspection.md#cancel).                                                  |

A `waiting` instance waits for one of these:

- a human task to be completed, see [Task API](task-api.md#completing-a-task)
- a connector call to settle, see [Connector calls](process-execution.md#connector-calls)
- a package operation call to settle, see [Package operations](process-execution.md#package-operations)
- a timer, see [Timer step](process-flow.md#timer-step)
- an event, see [Wait for event step](process-flow.md#wait-for-event-step)
- the child of a sub-process step, see [Sub-process step](process-flow.md#sub-process-step)

Only `running` and `waiting` block a new start for the same process and record. A `failed` or
`cancelled` instance does not, so it never locks its record. An operator can cancel a `running` or
`waiting` instance, see [Cancel](process-inspection.md#cancel), and retry a `failed` one, see
[Retry](process-inspection.md#retry).

## Task states

A task is `open` until exactly one of three things ends it: a decision, a cancel or its deadline.

| State       | Meaning                                                                                                                                                                                                                       |
| ----------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `open`      | The task waits for a decision.                                                                                                                                                                                                |
| `completed` | The decision is recorded, and the instance moves on.                                                                                                                                                                          |
| `cancelled` | An operator cancelled the task's instance. The task has no decision and leaves every inbox. See [Cancel](process-inspection.md#cancel).                                                                                       |
| `expired`   | The task's `deadline` passed while it was `open`, and the instance moved to the deadline's `next` step. The task has no decision, leaves every inbox and cannot be completed. See [Escalations and deadline](task-deadlines.md#escalations-and-deadline). |

A task has no other state. Its due date is only shown, so there is no overdue state: the task API
reports an open task past its due date as overdue, see [Task states](task-deadlines.md#task-states).
