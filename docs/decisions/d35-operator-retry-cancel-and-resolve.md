# D35. Operator retry, cancel and resolve

Status: **Agreed**

Authorized users can retry a failed process instance, cancel running or
waiting work, and resolve a call whose outcome is unknown. Each is an audited
command that needs its own grant. Cancel and retry are built. Resolve is
built for connector and package calls. See
[processes](../reference/process-inspection.md#operator-commands) for the routes, the
states each command accepts, the errors and a walkthrough.
The owner confirmed these points on issue #478 on 2026-10-17. The owner made
two changes to the proposal, which are marked below.

- **Who may act:**
  - A `process` grant takes three new actions, `retry`, `cancel` and
    `resolve`, next to `start`. Each is checked on its own. None implies
    another, and none implies a right to inspect the instance (#477).
  - The subject record must be readable by the user, as for a start.
    Otherwise the instance is `404`.
  - Each command takes a required `reason` of at most 500 characters.
  - Each command writes a history row and an audit record with the user, the
    reason and the instance: `process.retried`, `process.cancelled`,
    `connector.callResolved` or `operation.callResolved`. A call resolved as
    failed also writes `process.failed`. The actor is the user, not `system`.
  - The reason is stored only in the audit record's details, not in step
    history.
- **Retry:**
  - It takes only a `failed` instance. Any other state is `409`.
  - The instance stays on its pinned release.
  - The step that failed runs again, reading the subject record as it is now.
  - A call that failed is staged again under the **same operation identity**,
    with its attempts counted from zero. The system's idempotency key then
    stops a second effect.
  - The instance goes back to `running`, and a call step makes it `waiting`
    again. When another instance of the same process now runs for the record,
    the retry is `409`.
  - **Start lock (changed by the owner):** retry takes the application's start
    lock as the first statement of its transaction, as a start does. So it
    cannot race the old-release check of a destructive migration. A change of
    active release under the lock runs the command again, and three changes
    are a `503`.
  - A retry is also `409` when an applied migration has renamed, dropped or
    converted a column that the instance's pinned release uses. The
    old-release check counts only `running` and `waiting` instances, so a
    failed instance never blocks a destructive migration, and the retry would
    run old steps against a changed table.
- **Cancel:**
  - It takes a `running` or `waiting` instance. It becomes `cancelled`, a new
    end state that does not block a new start.
  - Its open tasks become `cancelled`, a new task state. They leave every
    inbox.
  - A `pending` call becomes `cancelled`, and its work item is removed.
  - A call that is `sending` or `unknown` makes the cancel `409`. The system
    may have acted, so the call must settle or be resolved first. Cancel never
    forgets an external effect.
  - A step that a worker is running when the cancel commits cannot commit
    after it. The instance revision fences it.
  - Cancel does not undo steps that already ran. There is no compensation, as
    D25 says. It comes later.
  - Cancel is the way to drain old-release work before a destructive
    migration.
- **Resolve:**
  - It takes only an `unknown` call, on an instance that is `waiting` on the
    call's step.
  - `"outcome": "succeeded"` with `output` values settles the call as
    succeeded. The output is checked against the operation's declared output,
    as a response would be. The instance moves on.
  - `"outcome": "failed"` settles the call as failed, and the instance
    becomes `failed`. A later retry stages the call again under the same
    identity.
  - **Start lock:** resolve takes the start lock too, because its output
    fields can set off update triggers, as a task completion does.
  - An `output` that does not fit, or an output write that the record refuses,
    is a `400`. Nothing changes and the call stays `unknown`. An operator's
    typing mistake does not fail the instance. The operator can still resolve
    the call as `failed` on purpose.
- **Where users see it:**
  - The process inspector widget (#477) shows Retry, Cancel and Resolve when
    the user's grants allow them. The server checks every request.
  - **Retry warning (changed by the owner):** the widget warns before a retry
    only when the failed call is a package call whose operation is not
    `idempotent: true`. A failed connector call is safe to resend, because
    every unknown outcome was resent under the idempotency key or reconciled.

_Why:_ [D11](d11-durable-process-engine.md) makes the engine durable,
and a failed instance, a stuck call and old work that cannot be drained are
the places where durability needs a human decision.
[D24](d24-roles-and-policies.md) makes every action a grant, so each
command gets its own. [D25](d25-connectors-deployment-bindings-and-secrets.md)
and [D28](d28-extension-packages.md) leave an unknown call for an
operator, and promise no second effect, so retry reuses the operation identity
and cancel refuses a call that may have acted.
[D30](d30-schema-migrations.md) waits for old-release work before a
destructive migration, which needs a way to drain it, and the start lock keeps
a retry from racing that check.
[Operations](../domain/knowledge.md#operations) says retry, cancellation and
resolution are authorized, audited commands.
