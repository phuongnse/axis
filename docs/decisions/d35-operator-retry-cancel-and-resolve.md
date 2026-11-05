# D35. Operator retry, cancel and resolve

Status: **Agreed**

## Context

[D11](d11-durable-process-engine.md) makes the engine durable, but some states need a human
decision: a failed instance, a call whose outcome is unknown, and old-release work that a destructive
migration of [D30](d30-schema-migrations.md) waits for.
[Operations](../domain/knowledge.md#operations) asks that retry, cancellation and resolution are
authorized, audited commands.

## Decision

- **Each command is a grant of its own.** A `process` grant takes `retry`, `cancel` and `resolve`.
  None implies another, or the right to inspect. The subject record must be readable, as for a start.
- **Each command needs a reason** and writes a history row and an audit record with the user as the
  actor. The reason is kept only in the audit record.
- **Retry runs the failed step again** on the pinned release, over the subject record as it is now.
  A failed call is staged again under the same operation identity, so the system's idempotency key
  stops a second effect. A retry is refused when another instance now runs for the record, or when
  an applied migration changed a column that the pinned release uses.
- **Cancel ends running or waiting work,** with its open tasks and pending calls. It is refused while
  a call is sending or unknown, because the system may have acted. It does not undo steps that
  already ran. A cancelled instance does not block a new start, and cancel is the way to drain
  old-release work before a destructive migration.
- **Resolve settles an unknown call** as succeeded, with output checked as a response would be, or as
  failed. An output that does not fit changes nothing.
- **Retry and resolve take the application's start lock,** as a start does, so a retry cannot race
  the old-release check of a migration, and the output of a resolve can set off triggers as a task
  completion does.
- **The process inspector shows the commands the user's grants allow.** It warns before a retry only
  when the failed call is a package call that is not idempotent.

The detail is in [operator commands](../reference/process-inspection.md#operator-commands).

## Consequences

- An unknown call is never forgotten or sent blindly, so the promise of
  [D25](d25-connectors-deployment-bindings-and-secrets.md) and [D28](d28-extension-packages.md) of no
  second effect holds through every command.
- An operator's typing mistake in a resolve does not fail the instance.
- Axis does not compensate, so a cancel leaves the effects of earlier steps in place.
