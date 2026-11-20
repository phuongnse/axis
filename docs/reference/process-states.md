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
| `cancelled` | An operator cancelled the instance while it was `running` or `waiting`. It is an end state. See [Cancel](process-operator-commands.md#cancel).                                                  |

A `waiting` instance waits for one of these:

- a human task to be completed, see [Task API](task-api.md#completing-a-task)
- a connector call to settle, see [Connector calls](connector-and-package-calls.md#connector-calls)
- a package operation call to settle, see [Package operations](connector-and-package-calls.md#package-operations)
- a timer, see [Timer step](process-flow.md#timer-step)
- an event, see [Wait for event step](process-flow.md#wait-for-event-step)
- the child of a sub-process step, see [Sub-process step](process-flow.md#sub-process-step)
- **Blocks.** _(planned for M38)_ the paths of a `parallel` or `repeat` step to end, see
  [paths](process-execution.md#paths)

Only `running` and `waiting` block a new start for the same process and record. A `failed` or
`cancelled` instance does not, so it never locks its record. An operator can cancel a `running` or
`waiting` instance, see [Cancel](process-operator-commands.md#cancel), and retry a `failed` one, see
[Retry](process-operator-commands.md#retry).

## Path states

_(planned for M38)_

An instance is at one or more steps, and each step is on a path, see
[paths](process-execution.md#paths). A path has its own state. The root path is the instance, so it has
the instance's state, and the states below are those of a path that a block starts.

| State       | Meaning                                                                                                                                                       |
| ----------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `running`   | The path has work that is ready or claimed by a worker.                                                                                                       |
| `waiting`   | The path waits for one of the things an instance waits for, for the block it started, or, in a repeat, for its turn to start.                         |
| `completed` | The path reached an `end` step. A pass that a skipping `end` ended is `completed` too, and it leaves its item out of the repeat's `gather`.                  |
| `failed`    | A step of the path failed and named no error path for it. The error is in the history.                                                                        |
| `cancelled` | The block that started the path cancelled it, or an operator cancelled the instance. It is an end state, and its open tasks are `closed`. _(planned for M38)_ An operator who cancels only the path leaves its open tasks `cancelled`, see [cancel one path or one task](process-operator-commands.md#cancel-one-path-or-one-task).                    |

The instance is `waiting` while its root path waits on a block. A `completed`, `failed` or `cancelled`
path never runs again.

## Task states

A task is `open` until exactly one of the things in the table ends it.

| State       | Meaning                                                                                                                                                                                                                       |
| ----------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `open`      | The task waits for a decision.                                                                                                                                                                                                |
| `completed` | The decision is recorded, and the instance moves on.                                                                                                                                                                          |
| `cancelled` | An operator cancelled the task's instance. The task has no decision and leaves every inbox. See [Cancel](process-operator-commands.md#cancel). _(planned for M38)_ An operator may also cancel the task's path, or the task alone, see [cancel one path or one task](process-operator-commands.md#cancel-one-path-or-one-task).                                                                                       |
| `expired`   | The task's `deadline` passed while it was `open`, and the instance moved to the deadline's `next` step. The task has no decision, leaves every inbox and cannot be completed. See [Escalations and deadline](task-deadlines.md#escalations-and-deadline). |
| `closed`    | _(planned for M38)_ The task was not needed, because its task for several people was decided without it, or its block cancelled its path. The task has no decision, leaves every inbox and cannot be completed: the task API answers a completion of it with a `409`. See [a task for several people](task-api.md#a-task-for-several-people) and [paths](process-execution.md#paths). |

A task has no other state. Its due date is only shown, so there is no overdue state: the task API
reports an open task past its due date as overdue, see [Task states](task-deadlines.md#task-states).
