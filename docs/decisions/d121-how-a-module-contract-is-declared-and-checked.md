# D121. How a module contract is declared and checked

Status: **Agreed**

The owner agreed this on 2026-11-01. It sets the shapes that
[D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md) left open for module
contracts, so that M24 can be broken into issues. It adds to D102 and changes none of its points.
Everything here is _(planned for M24)_. [Module contracts](../reference/module-contracts.md) holds
the detail.

- **A resource of its own:** a contract is a resource of the kind `contract`, one file for each
  module entity it covers, holding the lifecycle, the core validations, the effects and the grant
  ceiling. Only a module holds contracts, and a customization layer never names one.
- **Lifecycle:** an `enum` field that is required, not computed and not `sensitive` holds the
  state. Every value of the field is a state.
  - A record is created only in a state marked `initial`, and deleted only in a state marked
    `deletable`. No state is deletable unless it says so.
  - An update that changes the state needs a transition from the stored state to the new one whose
    guard is `true`.
- **The accountable user:** guards, core validations and effect values call `accountableUser()`,
  the user who answers for the write, and `previous(field)`, the value before the write.
  - For the record API, import and endpoints it is the caller. For a task completion it is the user
    who decides.
  - For a process step it is the user of the instance's last completed task, or the instance's
    starter before any task.
  - For a schedule, seed data and migrations it is `null`.
  - The instance keeps it, so a retried step reads the same user.
- **Effects only create records:** an effect creates a record in an entity of the same module, in
  the transaction of the transition, as an entry in a ledger. It never updates or deletes, and
  effects never chain.
- **Grant ceiling:** each role slot gets actions and fields, checked when the application compiles,
  and an optional record filter that the engine adds to every grant of the role with `and`.
  - Roles mapped to no slot get the ceiling's `others` entry.
  - Without `others`, such roles get no grant on the entity, as default deny asks.
- **Named points:** each guard, `initial` and `deletable` rule, core validation, effect and slot
  ceiling is a point named `<entity>/<part>/<name>`.
  - A `relax` entry on the application's module entry names one point and gives a plain-text
    reason. It marks the release off-standard.
  - The list of states and transitions is no point and never relaxes. Adding states and transitions
    is a change to the module, which the customization layers of M25 design.
- **Contract scenarios:** a module scenario marks the points it covers. Every publish and validate
  request runs the contract scenarios of every pinned module through the publisher, before
  activation. A scenario that covers a relaxed point is skipped and listed.
- **Every write:** `Axis.Data` checks the contract inside every record write. That covers the
  record API, task completions, process steps, connector and package outputs, imports, endpoints,
  scenarios and seed data. Migrations, recomputes, key rotation and restores are bulk changes of
  stored values and check no contract.
- **Studio:** the application's module entry shows each contract read-only, relaxes a point with
  its reason and shows the off-standard mark.

_Why:_ a separate resource keeps the line between what a module promises and what an application
may change as clear as a file boundary. That suits the layer of M25 and the version checks of M29.
The guard in D102's own example, "approved only by someone other than the requester", cannot be
written from record values alone. A process step writes as the system, and a field that records
the decider is written by the same steps a customization changes. Carrying the decider through the
instance, as workflow engines do for separation of duties, lets a guard refuse an approval that no
other person made, whatever step makes it. Ledger entries instead of updates keep concurrent
approvals from losing a change, and keep every change in the history. Comparing actions and fields
at compile time, and adding filters at run time, is as far as a ceiling can be checked, because
two filters cannot be compared. Capping roles that no slot covers keeps default deny. Lifecycle
rules on create and delete close the way around a transition: deleting an approved record and
creating it again. Relaxing only points, never the state graph, keeps each relax entry small and
readable. Extending the graph is an addition to the module, not a check turned off.
