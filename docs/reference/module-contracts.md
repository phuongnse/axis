# Module contracts

A module contract is what a module guarantees, whatever an application changes: the lifecycle of
its entities, its core validations, the effects of a transition, a grant ceiling for each role
slot, and its contract scenarios. The engine checks it on every write. An application turns off one
named point of it with a `relax` entry and a reason, and its release is then marked off-standard.
[D102](../decisions/d102-built-in-modules-open-to-change-and-bound-by-a-contract.md) agrees the
idea, and [D121](../decisions/d121-how-a-module-contract-is-declared-and-checked.md) agrees the
shapes below. The `contract` resource and the compile checks of its `lifecycle` and its `ceiling`
are built. So is the lifecycle check on every record write, with the record API's `409` and its log
line. So is the compile check of a module scenario's `contract.covers`. A refusal on another write
path fails as that path already fails, and logs nothing yet. The
filters of a role's grant ceiling narrow its grants at run time. The rest of this page is
_(planned for M24)_.

The conventions of [architecture.md](../architecture.md) apply. Diagnostic codes come with the
issues that build each check, as new rows of the [diagnostic codes](diagnostic-codes.md) table.

## The contract resource

A contract is a resource of the kind `contract`, one file for each module entity it covers. Only a
module holds contracts: a `contract` file in an application folder is a diagnostic. A
[customization layer](customization-layers.md) _(planned for M25)_ only adds states and transitions
to a contract, so the contract stays apart from the parts of the module that an application
changes.

This contract covers the `Request` entity of an approvals module. A request is created as a draft,
is submitted by its requester and is decided by someone else. An approval writes one entry in the
module's `Decision` entity:

```json
{
  "id": "5c1d3a0e-8a0f-4e57-9a43-1e0f6d0c2b71",
  "kind": "contract",
  "name": "RequestContract",
  "formatVersion": 1,
  "entity": "Request",
  "lifecycle": {
    "field": "status",
    "states": [
      { "name": "draft", "initial": true, "deletable": true },
      { "name": "submitted" },
      { "name": "returned", "deletable": true },
      { "name": "approved" },
      { "name": "rejected" }
    ],
    "transitions": [
      {
        "name": "submit",
        "from": ["draft", "returned"],
        "to": "submitted",
        "guard": "accountableUser() == requester",
        "message": { "textKey": "approvals.request.onlyRequesterSubmits" }
      },
      {
        "name": "approve",
        "from": ["submitted"],
        "to": "approved",
        "guard": "accountableUser() != null and accountableUser() != requester",
        "message": { "textKey": "approvals.request.noSelfApproval" }
      },
      {
        "name": "reject",
        "from": ["submitted"],
        "to": "rejected",
        "guard": "accountableUser() != null and accountableUser() != requester",
        "message": { "textKey": "approvals.request.noSelfApproval" }
      },
      { "name": "return", "from": ["submitted"], "to": "returned" }
    ]
  },
  "validations": [
    {
      "name": "amountFrozenAfterSubmit",
      "expression": "previous(status) in ('draft', 'returned') or amount == previous(amount)",
      "message": { "textKey": "approvals.request.amountFrozen" },
      "field": "amount"
    }
  ],
  "effects": [
    {
      "name": "approvalRecorded",
      "on": "approve",
      "create": {
        "entity": "Decision",
        "values": { "request": "id", "outcome": "'approved'", "decidedBy": "accountableUser()" }
      }
    }
  ],
  "ceiling": {
    "slots": {
      "requester": [
        {
          "actions": ["read", "create", "update", "delete"],
          "fields": ["title", "amount", "description", "status"],
          "filter": "requester == currentUser()"
        }
      ],
      "approver": [
        { "actions": ["read"] },
        { "actions": ["update"], "fields": ["status", "note"], "filter": "status == 'submitted'" }
      ]
    },
    "others": [{ "actions": ["read"] }]
  }
}
```

- **Shape.** `id`, `kind`, `name` and `formatVersion` as for every resource. `entity` names an
  entity of the same module by its plain name. A module has at most one contract for each entity.
  `lifecycle`, `validations`, `effects` and `ceiling` are each optional, but a contract has at least
  one of them.
- **Child entities.** A contract covers an owner entity. Its validations aggregate over child rows
  as an entity validation does. A child entity has no contract of its own.
- **Names.** Transitions, states, validations and effects each have a `name`, unique in its list
  ignoring letter case. The name is the element's API name and never changes meaning, see
  [named points](#named-points). Labels and messages are texts whose keys start with
  `<module id>.`.
- **Expressions.** A guard, a core validation and an effect's values use the
  [expression language](expressions.md). They read the record's own fields and its child rows
  through aggregates, as an entity validation does, and call the rules of the contract's own
  module. They may also call [`previous`](#previous-values),
  [`accountableUser`](#the-accountable-user) and [`decisionTask`](#the-deciding-task). A ceiling filter is a policy filter, see
  [grant ceiling](#grant-ceiling).

## Lifecycle

The lifecycle is a state machine on one field of the entity.

- **State field.** `field` names an `enum` field of the entity that is `required`, not computed and
  not `sensitive`. Lists, filters and policies read the state, so it stays plain.
- **States.** `states` lists every value of the field's `values`, each once, and nothing else.
  - `initial: true` marks a state a record may be created in. At least one state is initial.
  - `deletable: true` marks a state a record may be deleted in. No state is deletable unless it
    says so.
  - `protected: true` _(planned for M25)_ marks a state that only the contract's own transitions
    enter, see [extending a lifecycle](customization-layers.md#extending-a-lifecycle).
- **Transitions.** Each has a `name`, `from`, a non-empty list of states, a `to` state, and an
  optional `guard` and `message`.
  - A pair of a `from` state and the `to` state belongs to one transition at most.
  - A `to` state that is also in `from` is a diagnostic. Staying in a state is no transition.
  - A `guard` must be boolean. Without one, the transition is allowed whenever its states match.
  - `message` is the text shown when the guard refuses the write. Without it, a fixed text is used.
- **What the engine checks:**
  - A create is refused unless the new record's state is initial.
  - An update that changes the state is refused unless a transition goes from the stored state to
    the new state, and its guard is `true`. A guard that gives `null` or stops with a run-time error
    refuses the write, as a validation does.
  - An update that keeps the state makes no transition. The core validations still run.
  - A delete is refused unless the stored state is deletable.
- **Extending the lifecycle.** An application cannot add states or transitions in M24, and a relax
  entry never can, see [relax entries](#relax-entries). A
  [customization layer](customization-layers.md#extending-a-lifecycle) adds them _(planned for
  M25)_, except into a protected state.

## Core validations

`validations` holds the module's core validations. Each has a `name`, an `expression`, a `message`
and a `field`, with the meaning of an [entity validation](resource-files.md#entity-logic), plus
the [`previous`](#previous-values) and [`accountableUser`](#the-accountable-user) functions.

- They run on every create and update of the entity, after the entity's own validations, on the
  record as it will be stored.
- A failure is answered like a failing entity validation, with the core validation's message at its
  field.
- `previous` lets a core validation freeze a field once a state is reached, as
  `amountFrozenAfterSubmit` does above.

## Effects of a transition

An effect creates one record when a transition happens, in the transaction of the write that made
the transition.

- **Shape.** `name`, `on`, which names a transition of the same contract, and `create`, with an
  `entity` of the same module, by its plain name, and `values`, a map from field name to an
  expression.
- **Values.** The expressions read the record after the write, so `id` is the record's id, and may
  call `previous` and `accountableUser`. Each expression's type must fit its field. A field that
  `values` leaves out gets its default.
- **The created record:**
  - It gets its defaults, computed fields, sequence numbers and validations, as a
    `createRecord` operation does.
  - It is checked against its own entity's contract: its state, when it has a lifecycle, must be
    initial, and its core validations run.
  - Its creator fields get the accountable user, or stay `null` when there is none.
  - Effects do not check the grants of the person who made the write. The contract makes the write,
    as a `createRecord` step does.
- **Order.** The effects of one transition run in their declaration order, after the record is
  stored. An effect never makes a transition, so effects never chain.
- **Once per transition.** The effect is in the same transaction as the transition. A write that
  rolls back leaves no effect, and a retried step makes it again only with the transition.
- **Failure.** An effect that cannot be written, for example because a validation of its entity
  fails, rolls the whole write back. The write is refused and names the effect.
- **Only creates.** An effect never updates or deletes a record. A running total is the sum of the
  records that effects create, as a ledger is, so two transitions at once never lose an update and
  every change keeps its history. An effect is undone by a later transition whose effect writes the
  opposite entry.
- **Audit and triggers.** The created record writes `record.created`, with the actor of the write
  that made the transition and details `{ "version", "effect" }`, where `effect` is the effect's
  named point. Data change triggers fire for it as for any create.

## Grant ceiling

`ceiling` sets the widest grants each role may get on the entity, from application policies and
module policies alike.

- **Shape.** `slots` maps a role slot of the module to a list of ceiling entries. `others` is a list
  of the same entries and covers every role mapped to no slot of the module. An entry has the shape
  of a grant without a target, `{ "actions", "fields", "filter" }`, so one slot may read every
  record and update only some:
  - `actions` lists entity actions: `read`, `create`, `update`, `delete`, `export` and `import`.
  - `fields`, when present, lists the fields grants may cover. Without it, every field may be
    covered.
  - `filter`, when present, is a [policy filter](roles-and-policies.md). It may call
    `currentUser()`.
  - Without `others`, a role mapped to no slot may get no grant on the entity at all, as default
    deny asks.
- **Which ceiling a role gets.** A role the application maps to one or more slots of the module gets
  the entries of all of them. A role mapped to none gets `others`. A role in a module the
  application reaches only through a dependency gets the slots the chain gives it.
- **Checked when the application compiles.** For each action of a grant on the entity, the role's
  entries that list the action must exist, and the grant's fields must be within the union of their
  `fields`. Otherwise it is a diagnostic at the grant. A grant with no `fields` list is outside
  entries that all have one. A module policy that goes past its own module's ceiling is reported
  the same way.
- **Applied when a request runs.** A filter cannot be compared with another at compile time, so for
  each action the engine adds to every grant of the role the `or` of the filters of the entries
  that list the action, with `and`. An entry without a filter adds no condition. The record API,
  the data source endpoint, import and export then answer as for any grant filter.
- **What it does not cover.** Process `start` grants, data source grants and endpoints are outside
  the ceiling. A data source still reaches only the records and fields that the root entity's read
  grants reach.

## Previous values

`previous(field)` gives the value the named field held before the write, with the field's type.

- On a create it gives `null`.
- It reads only the entity's own fields, not a child collection or a computed field's expression.
  A child collection, a `references` field, an unknown name or any argument that is not a bare field
  name is `AXC0337`. An aggregate's item expression cannot call it.
- It is allowed only in a contract's guards, core validations and effect values. Anywhere else it is
  `AXC0336`.
- It is evaluated in the server and the worker, never translated to SQL.

## The accountable user

`accountableUser()` gives, as `text`, the user who answers for the write, or `null` when no person
does. It is how a guard knows who decided.

| The write is made by | The accountable user is |
| --- | --- |
| The record API and import | The user of the session, or the service account |
| An inbound endpoint call | The calling service account |
| A task completion that writes values | The user who completes the task |
| A process step: `updateRecord`, `createRecord`, connector and package outputs | The user of the instance's last completed task, or, before any task, the instance's starter |
| An operator command that writes, such as resolving a call | The operator |
| An effect | The accountable user of the write that made the transition |
| Seed data, a schedule trigger, a migration | `null` |
| A test scenario step | The scenario user the step names with `as`, by the same rules |

- **The instance's starter.** The user of the process start endpoint, the service account of an
  endpoint call, or, for an instance a trigger starts, the accountable user of the write or event
  that fired it. A schedule trigger starts with `null`. A sub-process starts with its parent's
  accountable user.
- **Kept by the instance.** The instance keeps its accountable user. Each task completion replaces
  it with the user who completed the task, in the completion's transaction, so a retried step reads
  the same user.
- **Allowed where.** In a contract's guards, core validations and effect values, and in the `set` of
  an `updateRecord` or `createRecord` step, which writes it as the value the write will be judged
  by, such as who decided a step. Anywhere else it is a diagnostic, as `currentUser()` outside a
  policy filter is.

## The deciding task

`decisionTask()` gives, as `text`, the id of the task whose completion the write follows, or `null`
when no task decision led to it. It lets a contract tell one decision from the next, so a module can
promise that each step is decided once.

- **Value.** For a task completion, the task it completes. For a process step, the instance's last
  completed task, kept with the [accountable user](#the-accountable-user) and replaced at the same
  time. For every other write, `null`.
- **Use.** A core validation that allows a counter to move only with a new decision compares the
  value a step stores with the one it held before: `stepDecision == decisionTask() and stepDecision
  != previous(stepDecision)`. A layer that adds a second write after one decision then cannot move
  the counter twice.
- **Allowed where.** Where `accountableUser()` is allowed.
- **Why not `currentUser()`.** `currentUser()` is the session's user and binds a SQL parameter.
  A process step has no session, and its write must still be judged by who decided it. So a step
  that sets `approved` before any task was decided runs with the starter as its accountable user,
  and the guard `accountableUser() != requester` refuses it. A schedule that sets it runs with
  `null`, and `accountableUser() != null` refuses it.

## Where the contract is checked

`Axis.Data` checks the contract inside every record write, so no caller can go around it.

- **Every write.** The record API, task completions, process steps, connector and package outputs,
  call resolution, imports, inbound endpoints, test scenarios and seed data.
- **Not a write of a record.** A [migration](migrations.md#migrations), a recompute of stored
  computed fields, a data key rotation and a restore change stored values in bulk and check no
  contract.
- **Seed data.** A seed record of an entity with a lifecycle must be in an initial state, or the
  seed is a diagnostic (`AXC0338`). A synced seed that changes a stored state must follow a
  transition, and its guard runs with a `null` accountable user. A refusal is `AXC0033`: it stops
  startup seeding, rolls back every seed write, and the log names the point that refused it.
- **Order in a write.** Computed fields, the entity's validations, the core validations, then the
  lifecycle check, then storage, then the effects. The record API's grant checks come before, as
  [the record API](record-api.md#errors) orders them. In Axis SaaS, the tenant's quota check of
  [D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) comes before all
  of them _(planned for M31)_, so a write refused for a quota checks no contract.

## When a write is refused

- **Record API.** A core validation failure is a `400` with the `errors` shape of a validation. A
  lifecycle refusal is a `409` with the fixed title "The record's state does not allow this
  change." and `errors` keyed `/values/<state field>`, whose message is the transition's `message`,
  or names the states when no transition matches. A delete in a state that is not deletable is the
  same `409`, keyed by the state field. A failing effect is a `409` with the fixed title "A
  required change could not be recorded." and the effect's named point. Nothing is written.
- **Task completion.** The completion is refused with the same answers, and the task stays open.
- **Process step.** The step fails, as a refused `createRecord` does, and the instance ends
  `failed`. The [process inspector](process-inspection.md#step-errors-in-inspection) and the
  [operator console](operator-console.md#views) show the error with the named point. The operator fixes
  the cause and retries, or resolves.
- **Import.** The row fails with the message, as a validation failure does, see
  [import](import-export.md#import).
- **Inbound endpoint.** The call is answered as a validation failure is, see
  [inbound endpoint responses](inbound-endpoints.md#responses).
- **Logs.** A refusal logs the named point and the record id, never a value.

## Named points

Each part of a contract is a named point that a relax entry and a contract scenario can name. A
point is written `<entity>/<part>/<name>`:

| Point | What it is |
| --- | --- |
| `Request/transitions/approve` | The guard of the `approve` transition |
| `Request/states/approved/initial` | The rule that no record is created as `approved`, because the state is not initial |
| `Request/states/approved/deletable` | The rule that no record is deleted while `approved`, because the state is not deletable |
| `Request/validations/amountFrozenAfterSubmit` | A core validation |
| `Request/effects/approvalRecorded` | An effect |
| `Request/ceiling/requester` | The ceiling entries of the `requester` slot |
| `Request/ceiling/others` | The ceiling of roles mapped to no slot |

The list of states and transitions is no point. It never relaxes.

## Relax entries

An application turns off a named point of a module it declares with a `relax` entry on that
module's entry in `application.json`:

```json
"modules": [
  {
    "id": "approvals",
    "version": "^1.0.0",
    "roles": { "requester": "employee", "approver": "director" },
    "relax": [
      {
        "point": "Request/transitions/approve",
        "reason": "We are three people, and the director approves their own requests."
      }
    ]
  }
]
```

- **Shape.** `point` names a point of the pinned version's contracts. `reason` is plain text of 1 to
  500 characters, written by the author for the people who read the release history.
- **Effect of each point:** a relaxed transition runs without its guard, a relaxed `initial` or
  `deletable` rule allows the create or the delete in that state, a relaxed core validation and a
  relaxed effect do not run, and a relaxed ceiling no longer limits the roles it covers.
- **Checks.** An unknown point, a point named twice, and a missing or blank reason are diagnostics.
  A point that a newer pinned version no longer has is the same diagnostic, so a relax entry never
  silently stops meaning anything.
- **Platform invariants never relax.** Default-deny authorization, audit records, one decision per
  task, durable execution and release pinning are no points, so no entry can name them.

## Off-standard releases

A release whose application has at least one relax entry is off-standard.

- The release keeps the list of relaxed points with their reasons, and the
  [release history](storage.md#release-history) shows it.
- Studio's release history and the operator console's publishing view show an off-standard mark,
  and the list of points and reasons on the release.
- The module's upgrade promise no longer covers a relaxed point _(planned for M29)_.

## Contract scenarios

A module holds test scenarios, as
[D103](../decisions/d103-modules-hold-triggers-scenarios-and-connector-slots.md) agrees, and marks
some of them as contract scenarios.

- **Module scenarios** _(built)_. A `scenario` file in a module names entities and processes of its module by
  their plain names, and its `users` give role slots of the module instead of roles. When it runs
  for an application, each slot becomes the role the application maps it to, and it runs against
  the application as compiled. It runs with the application's own scenarios, in Studio and with
  `Axis.Scenarios`, see [test scenarios](test-scenarios.md). A module's scenario files stay out of
  the release, as an application's do.
- **Contract scenarios** _(built for the compile check)_. A module scenario with
  `"contract": { "covers": [<points>] }` is a contract scenario. `covers` lists at least one
  [named point](#named-points) of the module that the scenario tests, and an empty list fails the
  schema. The points come from the module's own contracts and are matched exactly, with the letter
  case the contract writes. A point the module lacks is `AXC0339` at `/contract/covers/{i}` of the
  module's scenario file. An application's own scenario with `contract` is `AXC0340` at
  `/contract`. The compiled module scenarios keep their `covers`. Whether the scenario really
  tests its points is not checked.
- **On publish.** Every publish and every validate request of an application runs the contract
  scenarios of every module it pins, before activation, through the
  [publisher](authoring.md#publishing). A failing one fails the request, which names the
  module, the scenario and its failing step. Nothing is activated.
- **Relaxed points.** A contract scenario that covers a relaxed point is skipped. The publish result
  lists each skipped scenario with the point that skipped it.

## In Studio

- **Contracts.** A **Module contracts** group, below the module policies, lists the contracts of the
  modules the application uses, by `<module id>/<name>`, as the other module groups do. A contract
  opens read-only, with its read-only notice: the lifecycle as a state diagram, then the core
  validations, the effects and the ceilings, each with its named point.
- **Relax.** Each point has a **Relax** action, which asks for the reason and writes the `relax`
  entry on the module's entry in `application.json`. A relaxed point shows its reason and an
  **Undo relax** action, which removes the entry.
- **Mark.** The application's header and its release history show the off-standard mark while any
  entry is present.
- **Diagnostics.** A grant past a ceiling, a relax entry for an unknown point and a failing contract
  scenario show where Studio shows other diagnostics and publish results.
