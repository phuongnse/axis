# D155. Processes run paths at once, repeat steps, give tasks to many people and take error paths

Status: **Agreed**

## Context

[D11](d11-durable-process-engine.md) asks for a durable engine. A process followed one path: it was at
one step at a time, a loop had to pass through a task, a timer or a wait for an event, and a failed
step stopped the instance until an operator acted. An organization runs work that this cannot hold:
legal and finance read a contract side by side, two of three board members approve a purchase, every
line of an order is checked, a status is asked again until it changes, and a refused order goes back
to the buyer. [D154](d154-every-capability-an-organization-needs-has-a-home-and-a-capability-map-keeps-it-so.md)
puts this work first. This decision replaces D19 and D69 and keeps every part of them that still holds.

## Decision

- **A process runs for one record,** of the entity it names as its subject, or without a subject as
  [D70](d70-processes-without-a-subject-scheduled-jobs-and-delete-triggers.md) agrees.
- **One running instance per process and record,** pinned to the release active at its start.
- **Steps name the step that follows.** The step types are `decision`, `task`
  ([D20](d20-human-tasks-task-inbox-and-forms.md)), `operation`, `end`, `timer`, `waitEvent`,
  `subProcess`, and the blocks `parallel` and `repeat`. Decisions route by expressions and named
  rules, so a threshold is configuration.
- **Paths at once and repeats are blocks with a structure,** as AWS Step Functions and Power Automate
  shape them, not split and join gateways placed anywhere. A `parallel` step holds branches, and a
  `repeat` step holds a body, each a list of steps of its own. A path that starts in a block ends in
  it, no step outside a block leads into it, and blocks nest. A block goes on to its `next` when its
  rule of completion holds, so no path is left waiting for a join that never comes.
- **A parallel block is done when all its branches are,** or, as it says, when any of them is, when N
  of them are, or when a condition holds. A branch may have a `when`, so only the branches it chooses
  run. When the rule holds, the branches still running are cancelled, and the cancel is kept in the
  history.
- **A repeat runs its body in one of four ways:** for each item of a list, such as child rows,
  references or the rows of a data source; a number of times; while a condition holds, checked before
  each pass; or until a condition holds, checked after each pass. A repeat for each item or a number of
  times runs its passes in order, or at once up to a limit of passes at a time. A repeat while or until
  a condition holds names a largest number of passes, and may name a longest time. Any repeat may wait
  between passes and stop early when a condition holds, and a step of its body may skip an item. A
  pass knows its item and its number, and gathers what its steps give.
- **A loop back to an earlier step passes through a waiting step:** a `task`, a `timer` or a
  `waitEvent`. A repeat ends by its own limits instead, so neither can spin.
- **An instance is at one or more steps at once.** Each path keeps its own position, revision and
  history, and only the first resume of a path at its revision counts.
- **A task may go to several people,** chosen by an expression when it starts, with a rule of
  completion: all of them, any of them, a majority, or N of them choosing the same outcome. The task
  is decided as soon as no answer still to come can change its outcome, and the open tasks of the
  others are then closed. Each person has a task of their own and each answer is kept. A person may be
  added to an open task.
- **Waiting is a step.** A `timer` step waits for a duration or until a time. A `waitEvent` step waits
  for one event, or for the first of several, each with its own next step, about the instance's own
  subject, with an optional timeout. The instance stays `waiting` in the database, as at a task.
- **An occurrence wakes a wait once.** A wait takes the earliest unused occurrence about its subject
  raised since the instance started, and event delivery wakes it
  ([D72](d72-a-delivery-limit-and-dead-letters.md)).
- **A sub-process runs on its parent's release,** and the parent waits until it ends. Processes may not
  call each other in a cycle.
- **A process keeps data of its own,** in typed variables it declares beside its subject, which its
  steps read and set and which inspection shows under the same rule of masking as a call's input.
- **A step that still fails after the retries its own policy allows takes its error path,** when it
  names one for that kind of failure. Without one, the failure ends its path, a block whose path failed
  fails, and the instance fails, and only an operator retries it
  ([D35](d35-operator-retry-cancel-and-resolve.md)). A connector call
  ([D25](d25-connectors-deployment-bindings-and-secrets.md)) and a package operation
  ([D148](d148-packages-in-c-typescript-or-python-run-in-runners-that-axis-or-the-customer-hosts.md))
  take that policy from their step.
- **An operator may cancel one branch or one task** with a reason, as for an instance.
- **A `createRecord` operation goes through the record command,** as `system` in the step's
  transaction, with every rule of a create.
- **A worker host, `Axis.Worker`, runs the steps,** never the server.
- **Each step is one transaction** with its business writes, its path's state and revision, its
  history, its audit records and the next work item.
- **Workers claim work with leases and fencing.**
- **Limits keep an instance bounded:** the paths it runs at once, the items of a repeat, the passes of
  a repeat and the depth of blocks.

The detail is in [processes](../reference/processes.md),
[process flow](../reference/process-flow.md) and
[process execution](../reference/process-execution.md#execution).

## Consequences

- A crash leaves no partial step, and another worker takes over an item whose lease expired.
- The one-running-instance rule stops a record from being submitted twice, and stops triggers from
  looping ([D26](d26-events-and-triggers.md)).
- A long wait costs a row and a work item, not a thread, and a loop cannot spin.
- A call step in a loop or a repeat stages a call on every pass, so a call is known by its step, its
  path and its pass.
- A child keeps the contract the compiler checked, even after a newer release is activated.
- The compiler checks that every block is closed and that its rules and limits hold, so no instance
  waits for a path that cannot come.
- An instance can be at several steps, so the designer draws blocks as frames, and the inspector, the
  instance page and the operator console show every path that runs.
- A module contract that lets a step be decided once counts a task for several people as decided once,
  when its rule holds.
- Every installation runs a worker host beside the server.

## Alternatives

- **Split and join gateways placed anywhere, as BPMN allows.** They hold any shape, but a join can wait
  for a path that never comes, and the compiler cannot always tell. The blocks hold the shapes an
  organization uses, and a BPMN model of that shape maps to them.
- **A step type for each kind of loop.** One `repeat` with four kinds shares the limits, the wait between
  passes and the early stop.
- **Keep the waiting step in every loop.** A list of order lines would need a task on every line.
- **Keep a failed step for an operator only.** Every refusal by another system would stop the work until
  someone looked.
