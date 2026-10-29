# D102. Built-in modules open to change and bound by a contract

Status: **Agreed**

The owner agreed this on 2026-10-29. Axis ships built-in modules: the
standard processes that Axis defines, from which businesses build their
applications. Every business has rules of its own that no module author can
foresee. A business must be able to add them anywhere, without writing its
process from scratch, while the values at the core of the module and of the
platform still hold. M24 builds the contracts, M25 the customization layers,
and M26 the first built-in domain, see the [roadmap](../roadmap.md#milestones).
Everything here is _(planned for M24 to M26)_.

- **Four layers:**
  - The platform: the engines. They hold no domain and never name a built-in
    module.
  - Built-in modules: configuration that Axis maintains and ships with the
    installation, compiled by the same compiler as any application. They are
    not platform code. Each has its own SemVer version, apart from the
    platform version, and lives under `modules/` at the repository root.
  - Built-in applications: thin applications that declare modules, a site,
    the role slot mapping of
    [D89](d89-modules-that-hold-processes-pages-and-policies-and-depend-on-other-modules.md)
    and texts. They live under `applications/` at the repository root.
  - The business's customization: a layer over the modules, in the
    business's own application.
- **Composing:** a larger application declares more modules, for example
  `organization`, `employees`, `leave` and `purchasing`. There is no
  application inside an application. Two applications share no module data,
  as [D40](d40-reusable-modules.md) says. They talk through events and
  endpoints. Sharing data between applications would need a decision of its
  own.
- **Open by default:** an application may change any part of a module's
  implementation: add, change or remove process steps, rules, validations,
  fields, forms, pages, policies and triggers, and add entities of its own
  that reference module entities. There is no list of places where a change
  is allowed.
- **A contract bound to data, not to processes:** each module declares a
  contract on its entities. The engine checks it on every write, whatever
  makes the write: a process step, the record API, an import, a package
  operation or a test scenario. So no process change can go around it.
  - **Lifecycle:** an entity's states, held in an enum field, and its allowed
    transitions, each with a guard expression. A write that makes a transition
    the contract does not list, or whose guard is false, is refused.
  - **Core validations:** validations the contract marks as core. They always
    run, after the application's own validations.
  - **Effects of a transition:** records the contract writes when a transition
    happens, in the same transaction, for example a leave balance entry when a
    request is approved. They happen once per transition, whatever process
    made it, and a customization cannot remove them.
  - **Grant ceiling:** the widest grants each role slot may get on the
    module's entities. Application policies give no more than that.
  - **Contract scenarios:** test scenarios
    ([D32](d32-test-scenarios.md)) that the module marks as contract. They
    run on every publish of an application that uses the module, against the
    application as customized. A failing one stops the publish.
  - Each part of the contract is a named point, so a relax entry can name it.
- **Two levels of invariants:**
  - **Platform invariants never relax:** server-side default-deny
    authorization, audit records, one decision per task, durable execution and
    release pinning.
  - **Module invariants relax only on purpose:** an application turns off one
    named point of a module's contract with a `relax` entry that gives a
    reason, for example a company of three whose director approves their own
    leave. The release is then marked off-standard. Studio and the release
    history show the mark, the module's upgrade promise no longer covers that
    point, and the contract scenarios that test it are skipped and listed.
- **Elements are named:**
  - A layer names a module resource as `<module id>/<name>`, and an element
    inside it, such as a process step, a field, an outcome, a form section or
    a policy grant, by its `name`. The name is the element's API name, which
    expressions, forms, data sources and columns already use. Its label is a
    text, so a business that wants another label never changes a name.
  - A module that renames or removes an element declares it in a migration.
  - Building a module compares it with its previous version. An element that
    is gone without a migration that renames or removes it fails the build.
  - _Why names and not ids:_ a separate id would let a rename skip the
    migration, but every expression and form still names the element, so a
    rename already changes them. Names keep the files readable by people and
    agents, need no new property in any format, and a missing name is a
    compile error, never a silent change.
- **A layer, not a copy:**
  - The application's changes to a module are files under
    `customizations/<module id>/` in the application folder. Each file names
    the module resource it changes and holds only what it changes. A list of
    elements matches by name, not by position. `"remove": true` removes an
    element. A new resource of the business is an ordinary resource of the
    application.
  - Compilation applies the layer to the module version the application pins,
    then checks the result, contract included.
  - An upgrade is a three-way merge of the old module version, the new one and
    the layer, property by property within each element. A change to a
    property the new version did not touch applies as it is. A property that
    both changed is a conflict. An expression is one property, with no merge
    inside it. Studio shows each conflict, and the author resolves it before
    the next publish.
  - A change to a module's contract is a new major version of the module.
- **Module migrations:** a module ships migrations of the
  [D30](d30-schema-migrations.md) kinds, such as `renameField`, `dropField`
  and `convert`. Activation runs them on each application's copy of the
  module. A rename rewrites the layer's references. A removed element that
  the layer uses is a diagnostic.
- **Extension points are stable seams, not limits:** a module may mark rule
  slots and process slots as extension points. A change made there is
  promised never to conflict on an upgrade within the same major version. A
  change made elsewhere is still allowed, and may need a merge on an upgrade.
- **Eject:** an application may copy a module into its own resources, as a
  template copy does
  ([D51](d51-creating-applications-and-resources-from-studio.md)). The copy
  leaves the module for good: it gets no upgrades, and no module contract
  holds on it. It is the last way out, for a business whose layer would
  rewrite most of the module.
- **Who customizes:** a business analyst or an administrator of the business,
  in Studio, is the main way. A developer or a partner writes extension
  packages only for what an expression cannot do. Both write the same layer
  files ([D5](d5-configuration-is-authored-as-files-first.md)).
- **Studio:** shows each module resource with the application's layer applied,
  marks what the layer changed, shows the contract read-only, writes the
  layer, and lists the conflicts after an upgrade. Modules themselves are
  still not edited in Studio.
- **The first domain is HR:** `organization`, `employees` and `leave`. Every
  business needs it, its rules differ from one business to the next, and
  labour law gives it module invariants, such as annual leave of at least the
  legal minimum. The contract of each module is drawn from the rules of two or
  three real businesses, which the owner names when M26 is planned.
- **Samples:** the samples become built-in modules and applications over
  time, starting with `departments` as `organization` and leave requests as
  `leave` in M26. `samples/` then keeps only the fixtures for platform
  features that no built-in module uses.
- **Format after the first production release:** until then, the one-format
  rule of
  [D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)
  holds, and a format change updates the built-in modules in the same pull
  request, as it updates the samples. From the first production release, each
  resource kind has a format version, and Axis ships converters that rewrite
  older module, application and layer files. The engines hold no
  compatibility code. No production release comes before the layers of M25,
  because a business without them would copy the modules and lose every
  upgrade.
- **Order:** M24, M25 and M26 follow M20, one after the other, and come before
  M21 and M22, as
  [D101](d101-the-extensibility-milestone-is-split-into-six.md)'s order now
  reads.
- **Example, leave requests:**
  - The contract: a request moves from `submitted` to `approved` only with a
    decision by someone other than the requester, and an approval writes one
    balance entry.
  - The defaults: calendar days, 12 days a year, approval by the manager.
  - One business counts working days without public holidays, and adds a
    director's approval for staff on probation. Another refuses a request that
    overlaps its stock count period, held in an entity of its own.
  - Neither rewrites the process. A branch that sets `approved` without a
    decision is refused by the engine, whatever step makes it.
- **Effect on earlier decisions:** D40's rule that a module only grows
  additively and ships no migrations gives way to module migrations. D89's
  Studio point gains the layer, and its upgrade point the migrations. D46's
  point that samples only prove the platform gains built-in modules, which
  are configuration, not platform code. D101's order puts M24 to M26 before
  M21 and M22.

_Why:_ a module that allows changes only where its author opened a slot
becomes a cage, because its author cannot know every business. A business
then copies the module and loses every upgrade. A database shows the other way:
the code may do anything, while its constraints refuse every bad write,
whatever code makes it. Binding the contract to the module's data and
transitions, not to its process steps, lets a business change the process
anywhere and still never break the contract. Storing the changes as a layer
over named elements, not as a copy, keeps upgrades possible. Built-in modules
that grow out of the samples replace samples that only prove the platform with
configuration that real businesses use. The cost is a merge when a business
changed what an upgrade also changes, a contract that is hard to write well,
and a large amount of new platform work: lifecycles with guards, core
validations, transition effects, layers, the three-way merge, module
migrations and contract scenarios on publish.
