# Customization layers

A customization layer is an application's changes to a module it declares. Each layer file names one
resource of the module and holds only what the application changes in it. Compilation applies the
layers to the pinned module version, then checks the result, the [module contract](module-contracts.md)
included. [D102](../decisions/d102-built-in-modules-open-to-change-and-bound-by-a-contract.md) agrees
the idea, and [D122](../decisions/d122-how-a-customization-layer-is-written-and-applied.md) agrees
the shapes below. The layer resource and the checks of [the layer resource](#the-layer-resource)
and [applying a layer](#applying-a-layer) are built: compilation merges the `changes` of a layer
into its module resource before any check, and the release stores the layer and the module's own
files side by side, a layer places an element with `after` and `before`, and a diagnostic about a part
that a layer changed is reported in the layer file. A layer on a `contract` adds states and transitions
to its lifecycle, see [extending a lifecycle](#extending-a-lifecycle). A layer on a module entity adds
fields and validations, see [module entities](#module-entities). A layer removes a whole resource,
see [applying a layer](#applying-a-layer). Studio's support for layers and the sections below that are
not about applying are _(planned for M25)_. Module upgrades, the three-way merge and eject are _(planned for M29)_.

The conventions of [architecture.md](../architecture.md) apply. Diagnostic codes come with the
issues that build each check, as new rows of the [diagnostic codes](diagnostic-codes.md) table.

## The layer resource

A layer is a resource of the kind `layer`. One file changes one module resource, and lives at
`customizations/<module id>/<resource name>.json` in the application folder.

This layer adds a director's approval above a threshold to the `RequestApproval` process of the
`approvals` module. The manager's approval now goes to a decision step, and the step above the
threshold is new:

```json
{
  "id": "c0a5d1f4-2b8e-4c39-9f1e-6a7d3b2e8c10",
  "kind": "layer",
  "formatVersion": 1,
  "target": "approvals/RequestApproval",
  "changes": {
    "steps": [
      {
        "name": "managerApproval",
        "outcomes": [{ "name": "approve", "next": "directorCheck" }]
      },
      {
        "name": "directorCheck",
        "after": "managerApproval",
        "type": "decision",
        "branches": [{ "name": "aboveThreshold", "when": "amount >= DirectorThreshold()", "next": "directorApproval" }],
        "otherwise": "markApproved"
      },
      {
        "name": "directorApproval",
        "after": "directorCheck",
        "type": "task",
        "label": { "textKey": "acme.directorApproval" },
        "assignee": { "role": "director" },
        "form": "approvals/RequestReview",
        "dueIn": "P3D",
        "outcomes": [
          { "name": "approve", "label": { "textKey": "task.approve" }, "next": "markApproved" },
          { "name": "reject", "label": { "textKey": "task.reject" }, "next": "markRejected" }
        ]
      }
    ]
  }
}
```

- **Shape.** `id`, `kind` and `formatVersion` as for every resource, `target`, and `changes`.
- **Target.** `target` names a resource of a module that `application.json` declares, as
  `<module id>/<name>`. The file's folder must be that module's id and its file name the resource's
  name, so a reader finds the layer of a resource without opening every file. A target that names
  nothing in a declared module is `AXC0347`, and a file anywhere else is `AXC0349`. Two layers with
  one target are `AXC0350`, reported on the later file. Each of these checks reports at `/target`.
- **Letter case.** The module id in a target is matched exactly. The resource name is matched
  ignoring letter case, as resource names are elsewhere. The file path must equal
  `customizations/<target as written>.json`, exactly.
- **The module manifest.** `module.json` is targeted as `<module id>/module`. That counts only when
  the module has no resource named `module`, and it is not allowed, see below.
- **An ambiguous target.** Names are unique only within one kind, so a module may hold a data
  source and a page that share a name, and `<module id>/<name>` cannot tell them apart. The compiler
  does not guess: such a target is `AXC0352`. A resource whose name another kind in the module
  shares cannot be targeted yet. The lasting fix, such as a `targetKind` property, is open.
- **What a layer may target.** Every kind a module holds, except `scenario` and `module.json`, which
  are `AXC0348`. A layer in a module folder is refused like any kind a module may not hold,
  `AXC0202`. A
  `contract` takes only added states and transitions, see
  [extending a lifecycle](#extending-a-lifecycle).
- **Changes.** `changes` has the shape of the target resource, with only what changes. The rules of
  [applying a layer](#applying-a-layer) say how each part is read.
- **What never changes.** The target's `id`, `kind`, `name` and `formatVersion`, which a layer that
  sets them gets `AXC0351` for at `/changes/<property>`, and the `name` of
  any element. A rename is a remove and an add, so every reference to the old name becomes a
  diagnostic.
- **The application's own resources.** A new resource of the business, such as an entity of its
  own, is an ordinary resource of the application, not a layer.

## Applying a layer

Compilation applies each layer to the resource of the pinned module version, before any check.

- **Objects** merge property by property. A property in `changes` replaces the module's value. A
  property that `changes` leaves out keeps the module's value.
- **Named lists** merge element by element, by the element's key in the
  [list keys](#list-keys) table:
  - an element whose key the module's list has changes that element, by the same rules;
  - an element whose key the module's list lacks is added;
  - an element with `"remove": true` and its key removes the module's element. It holds nothing
    else.
- **Value lists**, such as a grant's `actions`, a notification's `to` or an index's `fields`, are
  one value. A layer that gives one replaces the whole list.
- **Expressions** are one value. A layer that changes one replaces the whole expression.
- **Order.** An added element goes to the end of its list, unless it has `"after": "<key>"` or
  `"before": "<key>"`, never both. A changed element with `after` or `before` moves there and keeps its
  other changes. The key, the anchor, names another element of the same list after the merge, so it
  can be a module element or one that the layer adds. Placements apply one at a time, in the order
  the elements appear in the layer, after all adds and removes, so an author who chains placements
  writes them in the order they should apply. Order counts in an entity's fields, a form's wizard
  steps and fields, a page's widgets, actions, row actions, toolbar actions, columns and tabs, and a
  process's steps, where the order only sets how Studio lists them, because the steps are a graph.
  Form sections join when they are keyed. Any other list takes no `after` or `before`: an escalation's
  `after` is its duration, and elsewhere the property is an unknown property. The merged resource
  never holds `after` or `before`.
- **Removing a whole resource.** A layer whose `changes` is exactly `{ "remove": true }` removes the
  resource from the application. The compiled model leaves it out, so Studio, the runtime and the
  site metadata do not see it. The release still stores the module's file as the module wrote it, and
  compiling the release applies the layer again. A reference to the removed resource, from the
  application or from the module, is reported in the layer file at `/changes/remove`, with the code
  that the broken reference has anyway and a message that names the referring file. A reference
  counts when its value is `<module id>/<name>`, or the plain name in a file of the module, ignoring
  letter case. Removing an entity that a [module contract](module-contracts.md) covers is `AXC0374`,
  reported at `/changes/remove`, and the layer is not applied. A `remove` beside any other property in
  `changes` is `AXC0365` at `/changes`. A layer that removes a `contract` is `AXC0369`.
- **Then the checks.** The merged resource is checked as any resource of its kind: its schema, its
  references, its expressions, the contract and the grant ceiling. Each diagnostic is then moved to
  where the author made the change:
  - A diagnostic inside a part that the layer added or replaced is reported in the layer file, at
    the path of that part under `/changes`, such as `/changes/steps/2/next`.
  - A diagnostic at an element that the layer changed as a whole, such as a required property
    missing from a changed step, is reported at that element under `/changes`.
  - A diagnostic in the module's own part whose value names an element that the layer removed,
    ignoring letter case, is reported in the layer file at that remove. An example is a step's
    `next` that names a removed step.
  - Any other diagnostic in the module's own part stays in the module's file, at the path in the
    merged resource. So does a diagnostic at the root or at `id`, `kind`, `name` or
    `formatVersion`, which a layer never changes.

  Only the diagnostics of the layered resource itself move. A diagnostic in another resource that
  names a removed element stays in that resource's file. The exception is a reference to a whole
  resource that a layer removes, see _Removing a whole resource_ above.
- **Keys and names.** Keys are matched ignoring letter case, and a changed element keeps the
  module's spelling of its key. The root `id`, `kind`, `name` and `formatVersion` are never merged.
  A layer on a `scenario` is stored and not applied _(planned for M25)_.
- **Checks of the layer itself.** A key in `changes` that the module's resource does not have, as a
  change, a remove or an anchor of `after` or `before`, is a diagnostic. So is a property that the
  kind does not have. A layer never matches by position. The checks are `AXC0364` for an unknown key
  in a remove, `AXC0365` for a remove that holds more than its key, in a list or in `changes`, `AXC0366` for an unknown
  property, `AXC0367` for an element with both `after` and `before` and `AXC0368` for an anchor that
  names no other element of the merged list, each reported at its path under `/changes` in the layer
  file. A layer with one of them is not applied. An element whose key the module's list lacks is an
  added element, so only a remove or an anchor can name an unknown key. After a version change, a
  changed element whose key the new version dropped is `AXC0372`, see
  [versions until M29](#versions-until-m29).

## List keys

Every element of a list that a layer can change has a key. Most lists use `name`, which becomes
required where it is not yet, in modules and applications alike, as the one-format rule of
[D46](../decisions/d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md) asks.
Studio gives each new element a name. The samples, templates and built-in modules get theirs in the
same change.

| Kind | List | Key |
| --- | --- | --- |
| `entity` | `fields`, `validations`, `indexes` | `name` |
| `process` | `steps`, a task's `outcomes`, a decision's `branches`, a task's `escalations` | `name` |
| `policy` | `grants` | `name` |
| `page` | `widgets`, a widget's `actions`, `rowActions`, `toolbarActions`, `columns` and `tabs`, at every depth | `name` |
| `form` | `sections`, `steps` | `name` |
| `form` | a section's `fields` | `field` |
| `dataSource` | `fields`, `parameters` | `name` |
| `dataSource` | `aggregate.groupBy` | `field` |
| `seed` | `records` | `id` |
| `contract` | `lifecycle.states`, `lifecycle.transitions` | `name` |

`Axis.Configuration` holds this table in code, and a unit test checks it against the resource
schemas.

- A key is unique in its list, ignoring letter case. An entity's `validations` and `indexes` require
  `name` (`AXC0004` when it is missing) and report a repeated name as `AXC0343`. A policy's `grants` require `name` too (`AXC0004` when it is missing) and report a repeated name as `AXC0353`. A decision's `branches` and a task's `escalations` require `name` as well (`AXC0004` when it is missing) and report a repeated name as `AXC0359`, and a form's `steps` do the same and report it as `AXC0360`. Each of these names is unique in its own list: one decision's branches, one task's escalations or one form's steps. A widget's `name` is unique on its page,
  whatever its depth, so a layer finds a widget inside a section or a tab by its name. A repeated name on a page, in a widget's own list of actions, columns or tabs, is `AXC0361`. A page's widgets, actions, row actions, toolbar actions, columns and tabs require `name` (`AXC0004` when it is missing).
- An element of a list that the table does not name is part of a value list.
- A list that the table still marks as pending in code, such as a form's `sections`, is a value list
  until its naming issue keys it. A layer that changes one gives the whole list.
- A text's `texts` is a map, so a layer on a module text changes or adds keys as an object merges.
  A business that wants another label changes the text, never a name.

## Module entities

A layer on a module entity changes the application's copy of it, which is the application's own
table. Adding fields and adding validations are built: activation adds the column of an added field
to the copy, and the record API reads and writes the field. Refusing a removed field that the module
still reads is built, and an application [migration](migrations.md#migrations) drops the column of a removed field.
Changing a field is _(planned for M25)_.

- **Adding fields** adds columns to the copy. A new field is the application's, and the module's
  forms, pages and processes do not show it until a layer on them adds it.
- **Changing a field** _(planned for M25)_. A layer may change a label, a default, `required`, a length, a range or an
  enum's `values`. A change of type, and a field removed, follow the
  [migration](migrations.md#migrations) rules of the application's own entities. The
  application's `migration` names the entity as `<module id>/<entity>`. Without it, activation is
  refused, and the schema diagnostic (`AXC0015` for a column with no field, `AXC0016` for an
  incompatible change) is reported in the layer file at the remove or at the changed property.
  The active release stays.
- **Removing a field** that the module still reads is refused with `AXC0375` at the remove, `/changes/fields/{i}`
  of the layer file. The readers are the entity's contract, and the module's data sources and forms on the entity
  that the application does not also layer, because a layer of its own can stop the reading. The names match
  ignoring letter case. The message names the first reader's file. The application's
  [migration](migrations.md#migrations) drops the column of the removed field, by naming `<module id>/<entity>`.
- **Validations** that the layer adds run with the entity's own, before the core validations.

## Extending a lifecycle

An application adds states and transitions to a module's lifecycle. It never changes or removes
the module's.

- **Adding a state.** A layer on the entity adds the value to the state field's `values`. A layer
  on the contract adds the state to `lifecycle.states`, with its own `initial` and `deletable`. A
  value in one without the other is `AXC0371`. It is reported in the contract's layer at the state
  or in the entity's layer at the field's `values`. An added state cannot set `protected`, because
  protection is the module's promise. That is `AXC0369`.
- **Adding a transition.** The contract's layer adds it to `lifecycle.transitions`, with `name`,
  `from`, `to`, and its own `guard` and `message`. The message's text key needs no module prefix,
  because the text belongs to the application. The record API checks an added transition and its guard
  on every write as it checks the module's, and refuses a change that no transition allows with a `409`.
  An added guard sees the module's rules and modules, not the application's own rules.
- **Protected states.** A state the contract marks `"protected": true` is entered only by the
  contract's own transitions. An added transition whose `to` is a protected state is `AXC0370`, reported
  at its `to`. The record API refuses such a transition as well. A module protects the states that
  its promise is about, such as `approved` and `rejected`.
- **Only additions.** A contract layer that changes or removes anything, or that holds anything but
  added states and transitions, is `AXC0369`, reported in the layer file at the element or property.
  An element whose `name` is one of the module's, ignoring letter case, is a change. A layer with
  `AXC0369` or `AXC0370` is not applied. Changing a module's guard is a
  [relax entry](module-contracts.md#relax-entries), not a layer.
- **Named points.** An added transition is the application's, so it is no point of the module's
  contract and a relax entry cannot name it. Nor are the `initial` and `deletable` rules of an
  added state. A relax entry that names one is `AXC0356`.

## In Studio

Studio shows each module resource with the application's layer applied, and an author changes it
as they change any resource of their own. The [authoring API](authoring.md) writes the layer.

- **Editable.** The module groups list every resource of the modules the application uses, by
  `<module id>/<name>`, and each opens in the editor of its kind instead of the read-only view.
  Module scenarios stay read-only, and a contract shows read-only with its added states and
  transitions, see [module contracts in Studio](module-contracts.md#in-studio).
- **Marks.** An element the layer added shows **Added**, one it changed **Changed**, and the
  editor's header shows that the resource has a layer. A removed element stays listed as **Removed**
  in a muted row.
- **Reset.** Each added, changed or removed element has **Reset to module**, which takes it out of
  the layer. The resource's header has **Reset all**, which deletes the layer file.
- **Saving writes the difference.** Studio sends the edited resource whole. The authoring API
  compares it with the pinned module version, by the list keys, and writes the layer that holds
  only the difference: changed properties, added elements with their place, and removes. A save
  that leaves no difference deletes the layer file. The file is never edited by hand to save from
  Studio. A save is refused when it changes `id`, `kind`, `name` or `formatVersion`, drops a
  property of the module, leaves a keyed element without a key, repeats a key or changes only the
  letter case of one.
- **Refused changes.** A save is checked like any save. A change that breaks the contract, such as
  a step that moves a request to `approved` past its guard, publishes as a failed contract scenario
  or a refused write, and a change that adds a transition into a protected state is a diagnostic in
  the editor.

## Versions until M29

- **Pinning.** A layer applies to the module version the application pins. Until module upgrades
  _(planned for M29)_ merge three ways, a newer installed version in range is applied with the same
  layer, by keys.
- **A key the new version lacks.** The compile compares the version it picks with the copy of the
  module in the application's active release. A layer element that changes a key which the old
  version has and the new one lacks is `AXC0372` at that key in the layer file, and the element is not
  added. A missing anchor (`AXC0368`) or a removed target after a version change fails the same way.
  Each affected module also gets one `AXC0373` on its entry in `application.json`. It names the
  module, the version in the active release, the new version and the layer files that no longer apply.
  The publish, `axis-admin activate` or startup stores no release, the release in use stays active and
  the failed request carries these diagnostics. With no active release, or with the same version as the
  active release, the compare does not run and an element whose key the module lacks is an added
  element. Only the step from the active version to the new one is compared.
- **Built-in modules.** No new version of a built-in module ships to a business that has a layer
  over it before M29, as
  [D117](../decisions/d117-small-businesses-first-and-the-milestones-in-three-phases.md) agrees.
