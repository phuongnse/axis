# Process operator commands

This page covers the operator commands that let an authorized user act on process work the engine
cannot finish alone: retry runs a `failed` instance again, cancel stops a live instance, and resolve
settles a call whose outcome is `unknown`. Each is an audited command. [Cancel one path or one task](#cancel-one-path-or-one-task)
ends one branch, pass or person's task while the case goes on.

## Operator commands

Three commands let an authorized user act on process work that the engine cannot finish alone.
Retry runs a `failed` instance again, cancel stops a `running` or `waiting` instance, and resolve
settles a connector or package call whose outcome is `unknown`. Each is an audited command with its
own grant, a required reason and one transaction. The [instance detail](process-inspection.md#instance-detail) lists the
commands the user may take, and the [process inspector widget](process-inspector-widget.md) shows
Cancel, Retry and Resolve.

| Route                                                                            | What it does                                                     |
| -------------------------------------------------------------------------------- | ---------------------------------------------------------------- |
| `POST /api/apps/{app}/processes/{process}/instances/{id}/retry`                  | Runs a failed instance again, see [retry](#retry)                |
| `POST /api/apps/{app}/processes/{process}/instances/{id}/cancel`                 | Stops a running or waiting instance, see [cancel](#cancel)       |
| `POST /api/apps/{app}/processes/{process}/instances/{id}/calls/{callId}/resolve` | Settles a call whose outcome is unknown, see [resolve](#resolve) |

Two more commands, **redeliver** and **discard**, act on a dead letter of the
process with the same `retry` grant, a required reason and an audit record, see
[Dead letters](process-events.md#routes). The widget shows Redeliver and
Discard on each open dead letter whose `commands` list them.

Cancel also cancels an instance's running and waiting sub-processes, see
[Cancel](#cancel). An instance without a subject needs only the grant, see
[processes without a subject](process-flow.md#processes-without-a-subject).

```http
POST /api/apps/{app}/processes/{process}/instances/{id}/retry
POST /api/apps/{app}/processes/{process}/instances/{id}/cancel
Content-Type: application/json

{ "reason": "The supplier code is fixed." }
```

```http
POST /api/apps/{app}/processes/{process}/instances/{id}/calls/{callId}/resolve
Content-Type: application/json

{
  "reason": "Order PO-2026-0001 exists in the purchasing system.",
  "outcome": "succeeded",
  "output": { "orderNumber": "PO-2026-0001" }
}
```

The content type follows the same rules as the record API, see
[Create, update and delete](record-api.md#create-update-and-delete). `{id}` is
the instance id and `{callId}` is the call's operation identity. A success is
`200` with the instance, in the shape of a start's body:
`{ "id", "process", "subjectId", "releaseId", "state" }`.

- `reason` is required. It must not be blank and has at most 500 characters. It is stored only in the
  details of the command's audit record, never in the step history.
- `outcome` is required for a resolve, and is `succeeded` or `failed`.
- `output` is for a resolve. It is allowed only with `succeeded`.

### Who may act

A `process` grant takes the actions `retry`, `cancel` and `resolve`, as well as
`start` and `inspect`, see [Authorization](security.md#authorization).
A [service account](security.md#service-accounts) may run the commands with a
bearer token, under the grants of its roles. It needs no session and no CSRF
header. The audit records name it as the actor, by its id.
Each is checked
on its own, and none implies another. The subject record must also be readable
by the user, by the same rule as a start. A subject record the user may not
read makes the instance an unknown instance, `404`, so the user cannot tell the
two apart. An instance without a subject has no record, so the grant is the
only check. No `read` check runs and there is no policy `409` for a pending
field. The check depends on the stored instance, so an instance with a subject
is never exempt from the read check. Every request is also checked on the server, whatever the widget
shows. In the [operator console](operator-console.md), the
operator role stands in for these grants and for the read check on the subject
record.

### Retry

Retry takes an instance that is `failed`. The instance stays on its pinned
release. Before it looks for the instance, it answers the policy `409` when a
`read` filter of the user reads a pending field, see
[Operator command errors](#operator-command-errors).

One transaction does this:

1. It takes the [start lock](processes.md#releases-and-running-work) as its first
   statement, as a start does. So it cannot race the old-release check of a
   destructive migration. When the active release changed under the lock, the
   command runs again on the new release. After three changes it answers `503`.
2. It locks the instance and requires it to be `failed`, otherwise `409`.
3. It requires that no other `running` or `waiting` instance exists for the
   process and the subject record, otherwise `409`. For a process without a
   subject, no other instance of the process may be `running` or `waiting`.
4. It requires that no applied `renameField`, `dropField`, `dropEntity` or
   `convert` migration changed a column that the pinned release uses,
   otherwise `409`. It uses the same has-the-column test as `AXC0178`, see
   [Migrations](migrations.md#migrations). That check counts only `running` and
   `waiting` instances, so a `failed` instance never blocks a destructive
   migration. Without this check, the retry would run old steps against a
   changed table. Only migrations applied since the pinned release count:
   those of the application whose file the pinned release does not hold. The
   release was activated after it applied every migration it holds, and its
   model already reflects them. Activation stores the columns each migration
   changed in `axis.applied_migrations`, see
   [Applied migrations](migrations.md#applied-migrations), and the retry reads
   them back. A row with no recorded columns counts as having changed every
   column. A pinned release whose stored content is missing
   counts as using every column, so any applied destructive migration of the
   application refuses the retry. A refused instance stays `failed`. The
   operator starts a new instance for the record.
5. It sets the instance to `running` on the same step, with the next revision,
   and clears `ended_at`.
6. It inserts a `process.step` work item for the step.
7. It writes a history row for the step, at the revision the instance had when
   it failed. Its output is `{ "command": "retry" }`.
8. It writes the audit record `process.retried` for the subject record, or with
   no record for an instance without a subject, with
   details `{ "step", "reason" }`. The actor is the user with their display
   name.

The worker then runs the step as usual, see [Running a step](process-execution.md#running-a-step).
It reads the subject record as it is now. A step that failed because of the
record's data can succeed after the record is fixed.

A `callConnector` step or a package operation step whose
call row is `failed` stages the call again under the **same operation identity**, see
[Staging and the call](process-execution.md#staging-and-the-call). The input is evaluated again
on purpose, so the retry sends the current data. A failed connector call keeps
its stored input, but the retry still evaluates it again. A failed package
call's stored input is cleared when it settles. The attempts count from zero,
so the step's full retry policy applies again. The attempt rows carry on
numbering after the earlier run, see [Attempts](process-inspection.md#attempts).

Retry has a risk for each kind of call. A retry of a connector call sends it again under the same
idempotency key, and some systems replay their stored failure for a key they have seen, so the retry
can fail the same way. The [process inspector widget](process-inspector-widget.md) warns before a
retry only when the failed call is a package call whose operation is not `idempotent: true`, because
such an operation may have acted before it failed, and a retry runs it again. A connector call needs
no warning, because every unknown outcome of one was sent again under the idempotency key or
[reconciled](process-execution.md#reconciliation), or was [resolved](#resolve) by an operator.

### Cancel

Cancel takes an instance that is `running` or `waiting`. It does not undo
steps that already ran, because Axis does not compensate a step's effects.
Before it locks anything, it answers the policy `409` when a `read` filter of
the user reads a pending field, see
[Operator command errors](#operator-command-errors).

One transaction does this:

1. When the instance is a sub-process, it locks the parent's row first. A
   cancel of a child and a cancel of its parent then meet on the parent, so
   the lock order is always parent before child.
2. It locks the instance's `open` tasks, then the instance, then its call
   rows. A task completion locks the task and then the instance, so locking
   the instance first would deadlock with a manager who approves at the moment
   an operator cancels. A task that was created while the cancel waited for the
   instance lock is closed too.
3. It requires the instance to be `running` or `waiting`, otherwise `409`.
4. It repeats step 2 for each `running` or `waiting` child, depth first,
   before it writes anything.
5. It requires that no call of the instance or of any child is `sending` or
   `unknown`, otherwise `409`. The problem names the call. The system may have
   acted, so the call must settle or be [resolved](#resolve) first. Cancel
   never forgets an external effect.
6. It sets the instance and each child to `cancelled`, with the next revision
   and `ended_at`.
7. It sets their `open` tasks to `cancelled`, and their `pending` calls to
   `cancelled` with their stored input cleared.
8. It deletes their work items.
9. It writes a history row for the step of each, at the revision it had. Its
   output is `{ "command": "cancel" }`.
10. It writes the audit record `process.cancelled` for the subject record of
    each, or with no record for an instance without a subject, with details `{ "step", "reason", "taskIds", "operationIds" }`. The
    two lists name the tasks and calls it cancelled. The actor is the user and
    the reason is the same for every instance.

**Paths.** _(planned for M38)_ Cancel also cancels the live paths of the instance. It locks them after
the instance, parents before children, and sets each to `cancelled` with its tasks, calls and work
items, as [paths](process-execution.md#paths) says.

Cancelling a child can fail its parent. When the cancelled instance is a child and its parent is
`waiting` on the sub-process step for this child, the cancel also fails the parent's step. The parent becomes `failed` with `ended_at`, and its
history row has the error `The sub-process of the step '{step}' was cancelled.`
and the output `{ "childInstanceId" }`. The audit record `process.failed` is
written with the actor `system` and details `{ "step" }`. A parent that is
itself being cancelled is not `waiting`, so it ends `cancelled`. A child that
is already `failed` stays as it is when its parent is cancelled.

A `cancelled` instance does not block a new start for the same record. Its
cancelled tasks leave every inbox, and a completion of one is `409`, see
[Task API errors](task-api.md#task-api-errors).

A cancel and a worker never both win. A worker that runs a step when the cancel commits cannot
commit after it, because the step changes the instance only at the revision it loaded, see [Running a step](process-execution.md#running-a-step). A worker that marks a call `sending`
either commits before the cancel, so the cancel finds the call `sending` and
answers `409`, or finds its work item deleted and sends nothing.

Cancel is also the way to drain old-release work before a destructive
migration, see [Migrations](migrations.md#limits). An `unknown` call must be
resolved first.

### Resolve

Resolve takes a call that is `unknown`, on an instance that is `waiting` on the
call's step. The operator has checked what the other system did, and says so.
It works for a connector call with no lookup and for a package call, which has
none. It also works for a connector call that waits for its lookup, in place of
the lookup.

One transaction does this:

1. It takes the [start lock](processes.md#releases-and-running-work) as its first
   statement, with the same rerun and `503` as a retry. Output fields can set
   off update triggers, so resolve follows the rule of a task completion.
2. It locks the instance and the call. It requires the call to be `unknown` and
   the instance to be `waiting` on its step, otherwise `409`. A lookup or a
   worker that settled the call first makes this a `409`.
3. For `succeeded`, it checks `output` against the operation's declared
   outputs, as a response body would be checked, see
   [Call outcomes](process-execution.md#call-outcomes). That is the connector operation's outputs,
   or the manifest's for a package call. Then it writes the step's output
   fields to the subject record with the record update rules, as a successful
   outcome does. An `output` that does not fit, or a write that the record
   refuses, is a `400`. Nothing changes and the call stays `unknown`.
   An instance without a subject has no record, so a `succeeded` resolve only
   checks `output` and writes no record. Its step maps no outputs.
   - It sets the call to `succeeded`. A package call clears its stored input,
     and a connector call keeps it, see [Call input kept](process-inspection.md#call-input-kept).
   - It sets the instance to `running` on `next`, with the next revision.
   - It writes the history row of the step, at the revision the instance
     waited at. Its output is
     `{ "operationId", "outcome", "next", "resolved": true }`, plus
     `subjectVersion` when it wrote output fields.
   - It writes `record.updated` when it wrote output fields, and inserts the
     work item of the next step.
4. For `failed`, it sets the call to `failed`. A package call clears its
   stored input, and a connector call keeps it, see
   [Call input kept](process-inspection.md#call-input-kept). It
   sets the instance to `failed` with `ended_at`, and writes a history row
   with output `{ "operationId", "outcome": "failed", "resolved": true }` and
   a fixed error sentence. It writes `process.failed` with details
   `{ "step" }`. The actor is the user.
5. For both outcomes, it deletes the call's `connector.call` or `package.call`
   work item, because a lookup may still be due.
6. For both outcomes, it writes `connector.callResolved` or
   `operation.callResolved` instead of `connector.called` or `operation.called`.
   Its details are `{ "step", "connector", "operation", "operationId", "outcome", "reason" }`
   for a connector call, and
   `{ "step", "package", "operation", "operationId", "outcome", "reason" }` for
   a package call. The actor is the user. So each settled call has one audit
   record, and it shows who settled it by hand.

A resolved call keeps its last HTTP status and last error from its final
attempt. Only its state and its settled time change, and a package call also
clears its stored input. A resolve records no
instance-ended metric, as a cancel does not.

A resolve with a mistake in `output` is a `400` and does not fail the
instance. The operator can still resolve the call as `failed` on purpose. A
later [retry](#retry) of that instance stages the call again under the same
operation identity.

When the operator resolves a call as `failed` and the other system had in fact
acted, a retry can create a second effect. Axis trusts the operator's judgement
there.

### Cancel one path or one task

_(planned for M38)_

A case can go on without one part of it. This command ends one branch or one pass that a block
started, or one open task of a task for several people. The rest of the case then runs as if that
part had not run. The `cancel` grant is needed, and the subject record must be readable, as
[who may act](#who-may-act) says.

| Route                                                                                                  | What it does                                                    |
| ------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------- |
| `POST /api/apps/{app}/processes/{process}/instances/{id}/paths/{pathId}/cancel` _(planned for M38)_     | Ends one branch or pass that a block started, with a reason     |
| `POST /api/apps/{app}/processes/{process}/instances/{id}/tasks/{taskId}/cancel` _(planned for M38)_     | Ends one open task of a task for several people, with a reason  |

The body is `{ "reason" }`, with the rules of [cancel](#cancel). A success is `200` with the instance,
in the shape of a start's body, as an instance cancel answers.

**Path.** The root path is the instance, so the instance cancel covers it. A path that a block started
is one branch or one pass. One transaction does this:

1. It answers the policy `409` of [Cancel](#cancel) before it locks anything.
2. It locks the open tasks of the path and of the paths inside it, then the instance, then those paths,
   parents before children, as [paths](process-execution.md#paths) says, then their calls.
3. It requires the path to be `running` or `waiting`, otherwise `409`.
4. It locks the `running` or `waiting` child instances that a sub-process step on those paths started,
   depth first, as [Cancel](#cancel) does for its children.
5. It requires that no call of the path, of the paths inside it or of those children is `sending` or
   `unknown`, otherwise `409`. The problem names the call in a `callId` member. The system may have
   acted, so the call must settle or be [resolved](#resolve) first.
6. It sets the path and the paths inside it to `cancelled`, with the next revision and `ended_at`. It
   cancels the child instances as [Cancel](#cancel) does for its children.
7. It sets their open tasks to `cancelled`, not `closed`. It sets their `pending` calls to `cancelled`
   with their stored input cleared.
8. It deletes their work items.
9. It writes a history row for each path at the revision it had. Its output is `{ "command": "cancel" }`.
10. It writes the audit record `process.pathCancelled`, with details `{ "pathId", "block", "step", "reason", "taskIds", "operationIds" }`.
    The two lists name the tasks and calls it cancelled. The actor is the user. The record is written
    for the subject record of the instance, or with no record for an instance without a subject.
11. The cancelled path then counts as a path that did not run. A branch counts as a branch whose `when`
    is false, so it no longer counts toward its block's rule. A pass counts as a skipped pass, so its
    item is left out of `gather`, and the next pass in order starts, see [repeat step](process-blocks.md#repeat-step).
12. The same transaction checks the block's rule, as [Going on](process-execution.md#paths) says. When
    the rule holds, or every path of the block has ended, the block goes on and cancels the rest.

**Task.** A task for one person has no group, and no other answer could move its path on. Its case
takes the path cancel or the instance cancel instead. One transaction cancels one open task of a task
for several people:

1. It answers the policy `409` of [Cancel](#cancel) before it locks anything.
2. It locks every task of the group in id order, then the instance, then the path, as
   [a task for several people](task-api.md#a-task-for-several-people) does.
3. It requires the task to be `open`, to have a group, and to leave at least one other task of the group
   `open` or `completed`, otherwise `409`.
4. It sets the task to `cancelled`, and it deletes the task's pending `task.escalate` and `task.deadline`
   items.
5. It writes a history row on the task's step, at the path's revision, with the output
   `{ "command": "cancel", "taskId" }`.
6. It writes the audit record `task.cancelled`, with details `{ "taskId", "step", "reason" }`. The actor
   is the user.
7. The rule of completion then counts only the group's tasks that are not `cancelled`. When no answer
   still to come can change the outcome, the same transaction decides the task, as the deciding answer
   does, see [a task for several people](task-api.md#a-task-for-several-people). The deciding task and
   the accountable user then come from the group's latest completed task. When no task of the group is
   completed, the instance keeps both, see [the deciding task](module-contracts.md#the-deciding-task).

**Errors.** The checks run in the order of [Operator command errors](#operator-command-errors), with the
`cancel` grant. A failure writes nothing.

| Command         | Cause                                                                                                                                                                       |
| --------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Cancel a path   | `404`: No path of this instance exists with this id. The path is unknown, is not a UUID in the hyphenated form, or is the root path. |
| Cancel a task   | `404`: No task of this instance exists with this id. The task is unknown, or is not a UUID in the hyphenated form.                  |
| Cancel a path   | `409`: The path is not running or waiting. The title is "The path is not running or waiting.". |
| Cancel a path   | `409`: A call of the path, of a path inside it or of one of its child instances is `sending` or `unknown`. The problem names the call in a `callId` member. The title is the one of an instance cancel, "A call of this instance may have reached the other system. It must settle or be resolved first.". |
| Cancel a task   | `409`: The task is not open. The title is "The task is not open.". |
| Cancel a task   | `409`: The task has no group. The title is "Only a task for several people can be cancelled on its own.". |
| Cancel a task   | `409`: The cancel would leave the group with no open or completed task. The title is "The task is the last task of its group that can still be answered.". |

### Operator command errors

Errors are problem details, as in the [record API](record-api.md#errors). The
titles are fixed and never contain text from the request. The shape of the
answers matches the [start endpoint](processes.md#start-endpoint).

A request is checked in this order, and the first failure is the response:

1. The CSRF header, `403`, for a session request only. A bearer token needs none.
2. The session or the bearer token, `401`.
3. The application and the process, `404`.
4. The matching grant on the process, `403`: `retry`, `cancel` or `resolve`.
5. The content type, `415`.
6. The body, `400`. For a resolve this is the body's shape: `reason`, `outcome` and
   `output` as an object. Each output value is checked later, in the
   transaction, because the declared outputs come from the call's pinned
   release. A bad `reason` and a bad output value are reported one after the
   other, not together.

One transaction then checks the instance, and for a resolve the call. A cancel
or a retry first answers the policy `409` below, before it looks for the
instance. It answers `404` for the instance, the subject record and the call,
and then the `409` checks.

- **`401`.** Nobody is signed in. The title is the record API's "Sign in to
  read or write this data.". A bad bearer token gets `WWW-Authenticate: Bearer`
  and its own title, as in [process inspection](process-inspection.md#process-inspection).
- **`403`.** The user has no grant for the command on the process. The title
  is the record API's "You have no grant for this action.". The CSRF header
  failure is a `403` too.
- **`404`.** An unknown application, an application with no active release, or
  an unknown process. In the transaction, also:
  - an unknown instance, an `{id}` that is not a UUID in the hyphenated form,
    or an instance of another process. The cancel's title is "No instance of
    this process exists with this id.";
  - an instance whose subject record the user may not read;
  - for a resolve, an unknown `{callId}`, a `{callId}` that is not a UUID in the
    hyphenated form, or a call of another instance. The title is "No call of this
    instance exists with this id.".
- **`400`.** The body is a validation problem keyed by JSON Pointer. Every
  invalid part is reported in the same response.
  - A missing, blank or too long `reason` is keyed `/reason`.
  - A missing `outcome`, or one that is not `succeeded` or `failed`, is keyed
    `/outcome`.
  - An `output` with `failed`, or an `output` that is not an object, is keyed
    `/output`.
  - An output that is not declared, or has a value of the wrong type, is keyed
    `/output/{name}`. So is a required output that is missing, and an output
    whose field write the record refuses, with the record API's message.
  - Any other property, a repeated property and a body that is not a JSON
    object get the record API's keys and messages, such as "Unknown
    property.".
- **`409` for a policy that reads a pending field.** Cancel, retry and resolve. A
  `read` filter of the user's grants on the subject entity reads a
  [pending field](migrations.md#recomputing-computed-fields). The title is the
  record API's "Your access to this entity cannot be checked while a field is
  being recomputed.", with no `errors`. Nothing is written.
- **`409`.** The state does not allow the command. Nothing is written.

  | Command | Cause |
  | --- | --- |
  | Retry | The instance is not `failed`. The title is "The instance is not failed.". |
  | Retry | Another instance of the process is `running` or `waiting` for the record. A start that commits first on the unique index gives the same answer. The title is the start endpoint's: "A running or waiting instance of this process already exists for this record.". |
  | Retry | Another instance of a process without a subject is `running` or `waiting`. The title is "A running or waiting instance of this process already exists.". |
  | Retry | An applied migration renamed, dropped or converted a column that the pinned release uses. The title is "A migration changed a column that the instance's release uses.". |
  | Cancel | The instance is not `running` or `waiting`. The title is "The instance is not running or waiting.". |
  | Cancel | A call of the instance is `sending` or `unknown`. The problem names the call in a `callId` member. The title is "A call of this instance may have reached the other system. It must settle or be resolved first.". |
  | Resolve | The call is not `unknown`. The title is "The call's outcome is not unknown.". |
  | Resolve | The instance is not `waiting` on the call's step. The title is "The instance is not waiting on the call's step.". |

- **`503`.** Retry and resolve only. The application's active release changed
  on each of three attempts, found under the
  [start lock](processes.md#releases-and-running-work). The server already ran the command
  again on the new release each time. Nothing was written, and the client may
  send the request again. The title is the start endpoint's: "The
  application's active release changed while the request ran. Try again.".

### Walkthrough: an unknown purchase order

Suppose the `PurchasingSystem` connector declares neither `idempotency` nor a
`lookup`. The request still ends with exactly one purchase order.

1. The finance reviewer approves request `PR-2026-00042`. `markApproved` runs,
   `announceApproved` raises `PurchaseRequestApproved`, and it queues
   `createPurchaseOrder`.
2. The step stages call C as `pending` and sets the instance `waiting`. Worker
   A marks C `sending` and sends `POST /purchase-orders`.
3. The purchasing system creates `PO-2026-0001`. Its response is lost, and the
   request times out.
4. A timeout is an unknown outcome. The connector declares no `idempotency`, so
   C is not sent again. It declares no `lookup`, so nothing can ask the system.
   C becomes `unknown`, its work item is deleted, and the instance stays
   `waiting`. A history row with output `{ "operationId", "outcome": "unknown" }`
   makes it visible. A second start for the request is blocked.
5. An operator who holds a `resolve` grant on the process checks the purchasing
   system. It holds `PO-2026-0001` for `PR-2026-00042`.
6. The operator posts a resolve for C with a reason, `outcome` `succeeded` and
   `output` `{ "orderNumber": "PO-2026-0001" }`.
7. One transaction takes the start lock and checks that C is `unknown` and the
   instance is `waiting` on `createPurchaseOrder`. It checks the output
   against the operation's declared output. It writes
   `purchaseOrderNumber = PO-2026-0001` and `record.updated`, and sets C to
   `succeeded`. The instance becomes `running` on `assignPaymentReference`. It
   writes the history row, and one `connector.callResolved` with the operator
   as its actor and their reason. It queues the next step.
8. The worker runs `assignPaymentReference`, `assignPaymentDueDate` and then
   `done`, which completes the instance.

The purchasing system holds one order, and the request records its number. No
`connector.called` was written, because C settled by hand, and the audit trail
shows who did it.

- **Resolved as failed, then retried.** Had the operator found no order, they
  would resolve C with `outcome` `failed`. C becomes `failed` and the instance
  becomes `failed` on `createPurchaseOrder`, with `process.failed` written. The
  operator can then post a retry. The instance goes back to `running` on the
  same step, which stages C again under the same operation identity with fresh
  input and 0 attempts. Without `idempotency`, a wrong "no order" finding means
  a second order, so the operator must check first.
