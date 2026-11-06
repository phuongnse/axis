# D121. How a module contract is declared and checked

Status: **Agreed**

## Context

[D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md) binds each module to a
contract on its data and leaves its shapes open. Its own example, "approved only by someone other
than the requester", cannot be written from record values alone, because a process step writes as
the system and a field that records the decider is written by the steps a customization changes.

## Decision

- **A resource of its own.** A contract is a `contract` resource, one file for each module entity it
  covers, with the lifecycle, the core validations, the effects and the grant ceiling. Only a module
  holds contracts, and a layer only adds states and transitions to one, as
  [D122](d122-how-a-customization-layer-is-written-and-applied.md) agrees.
- **The lifecycle covers create and delete.** A required, non-computed, non-sensitive `enum` field
  holds the state. A record is created only in a state marked `initial` and deleted only in one
  marked `deletable`, and an update that changes the state needs a transition whose guard is true.
- **The accountable user is carried through the instance.** `accountableUser()` gives the user who
  answers for the write: the caller, the user who decides a task, or for a process step the user of
  the instance's last completed task, and `null` for a schedule, seed data and migrations.
  `decisionTask()` gives the task the write follows, and `previous(field)` the value before the
  write. The instance keeps them, so a retried step reads the same values.
- **Effects only create records.** An effect creates a record of the same module in the
  transaction of the transition, as a ledger entry. It never updates or deletes, and effects never
  chain.
- **A ceiling is checked in two steps.** Each role slot gets entries shaped like grants. Actions and
  fields are checked when the application compiles, and the entries' filters are added to the role's
  grants at run time, because two filters cannot be compared. Roles mapped to no slot get the
  `others` entry, or no grant.
- **Points relax, the state graph does not.** Each guard, `initial` and `deletable` rule, core
  validation, effect and slot ceiling is a named point, and a `relax` entry with a reason turns one
  off and marks the release off-standard. The list of states and transitions is no point.
- **Contract scenarios run before activation.** A module scenario marks the points it covers, and
  every publish and validate runs the contract scenarios of every pinned module through the
  publisher. A scenario that covers a relaxed point is skipped and listed.
- **Every write, inside `Axis.Data`.** The contract is checked inside every record write, whatever
  makes it. Migrations, recomputes, key rotation and restores change stored values in bulk and check
  no contract.

The detail is in [module contracts](../reference/module-contracts.md).

## Consequences

- What a module promises and what an application may change are split by a file boundary, which
  suits the layers and the version checks of upgrades.
- A guard refuses an approval that no other person made, whatever step makes it, as workflow engines
  enforce separation of duties.
- Deleting an approved record and creating it again cannot go around a transition.
- A running total is a sum of entries rather than a field.
- Each relax entry stays small, and adding states is an addition to the module, never a check turned
  off.

## Alternatives

- **A contract inside the entity file.** The promise would sit in a file that a layer changes.
- **Effects that update records.** Two approvals at once could lose an update, and the history would
  lose the steps.
