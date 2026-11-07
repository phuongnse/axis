# D102. Built-in modules open to change and bound by a contract

Status: **Agreed**

## Context

Axis ships built-in modules: the standard processes from which businesses build their applications.
Every business has rules of its own that no module author can foresee, and must be able to add them
anywhere without writing its process from scratch, while the values at the core of the module and
of the platform still hold.

## Decision

- **Four layers.** The platform holds the engines, which hold no domain and never name a built-in
  module. Built-in modules are configuration that Axis maintains under `modules/`, each with its own
  SemVer version, compiled by the same compiler as any application. Built-in applications under
  `applications/` are thin: they declare modules, map their role slots and add a site and texts. A
  business's customization is a layer over the modules, in its own application.
- **Composing, not nesting.** A larger application declares more modules, and there is no
  application inside an application. Two applications share no module data, as
  [D40](d40-reusable-modules.md) says, and talk through events and endpoints.
- **Open by default.** An application may change any part of a module: process steps, rules,
  validations, fields, forms, pages, policies and triggers, and it may add entities that reference
  module entities. There is no list of places where a change is allowed.
- **A contract bound to data, not to processes.** Each module declares a contract on its entities,
  which the engine checks on every write, whatever makes it: a lifecycle of states with guarded
  transitions, core validations, effects of a transition, a grant ceiling for each role slot, and
  contract scenarios that run on every publish. [D121](d121-how-a-module-contract-is-declared-and-checked.md)
  sets its shapes.
- **Two levels of invariants.** Platform invariants never relax: default-deny authorization, audit
  records, one decision per task, durable execution and release pinning. A module's invariants relax
  only on purpose: an application turns off one named point of the contract with a `relax` entry and
  a reason, and its release is marked off-standard. The module's upgrade promise then no longer
  covers that point.
- **A layer, not a copy.** The application's changes are files under `customizations/<module id>/`
  that name module resources and their elements by name and hold only what changes.
  Compilation applies the layer to the pinned module version and then checks the result, contract
  included. [D122](d122-how-a-customization-layer-is-written-and-applied.md) sets its shapes.
- **Elements are named by their API name,** the name that expressions, forms and data sources
  already use. Their labels are texts, so a business that wants another label never changes a name.
- **Upgrades merge and migrate.** A module ships migrations of the [D30](d30-schema-migrations.md)
  kinds, which activation runs on each application's copy and which rewrite the layer's references.
  An element that is gone without a migration fails the module's build. An upgrade merges the old
  version, the new one and the layer three ways, property by property, with an expression as one
  property, and the author resolves each conflict before the next publish. A change to a contract is
  a new major version of the module.
- **Extension points are stable seams, not limits.** A module may mark rule slots and process slots
  as extension points, where a change never conflicts on an upgrade within a major version. A change
  elsewhere is still allowed and may need a merge.
- **Eject is the last way out.** An application may copy a module into its own resources, as a
  template copy does. The copy gets no upgrades and no contract.
- **A business customizes in Studio.** A business analyst or an administrator changes a module in
  Studio, which shows it with the layer applied, marks what the layer changed, shows the contract
  read-only, lists the conflicts after an upgrade and writes the layer. A developer or a partner
  writes extension packages only for what an expression cannot do. A module's own files are never
  edited in Studio.
- **The first built-in modules are workflow modules,** `organization`, `approvals`, `service-desk`
  and `assets`, because each business sets their rules itself and they need no review against the
  law. HR modules such as employees and leave wait until a practitioner can review their legal
  rules. The samples become built-in modules and applications, except the purchase requests of
  [D94](d94-purchase-requests-prove-the-extensibility-milestone.md) and the leave requests, which wait
  for the HR modules. `samples/` then keeps only fixtures for platform features that no built-in
  module uses.
- **One format until the first production release.** Until then the one-format rule of
  [D46](d46-an-enterprise-application-platform-for-any-domain-and-the-milestones-after-m9.md) holds, and a format
  change updates the built-in modules in the same change. From then on each resource kind has a
  format version, and Axis ships converters that rewrite older module, application and layer files,
  so the engines hold no compatibility code. No production release comes before the layers.

The detail is in [module contracts](../reference/module-contracts.md),
[customization layers](../reference/customization-layers.md) and
[built-in modules](../reference/built-in-modules.md), and the order of the milestones in the
[roadmap](../roadmap.md).

For example, a leave request contract moves a request to `approved` only with a decision by someone
other than the requester. A business that adds a director's approval for staff on probation changes
the process, and a branch that sets `approved` without a decision is still refused, whatever step
makes it.

## Consequences

- A business changes the process anywhere and still never breaks the contract, as a database's
  constraints refuse every bad write whatever code makes it.
- Upgrades stay possible, at the cost of a merge when a business changed what an upgrade also
  changes.
- A good contract is hard to write, and it is refined with the first businesses that use a module.
- The platform needs much new work, from guarded lifecycles to the three-way merge.
- Built-in modules replace samples that only prove the platform with configuration that real
  businesses use. They are configuration, not platform code.

## Alternatives

- **Change only at extension points.** The module becomes a cage, because its author cannot know
  every business, and a business would copy it and lose every upgrade.
- **Name elements by a separate id.** Every expression and form still names the element, so a
  rename changes them anyway. Names keep the files readable, and a missing name is a compile error,
  never a silent change.
