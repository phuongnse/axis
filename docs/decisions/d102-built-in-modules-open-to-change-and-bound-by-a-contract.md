# D102. Built-in modules open to change and bound by a contract

Status: **Proposed**

The owner set this direction on 2026-10-29. Axis ships built-in modules: the
standard processes that Axis defines, from which businesses build their
applications. Every business has rules of its own that no module author can
foresee. A business must be able to add them anywhere, without writing its
process from scratch, while the values at the core of the module and of the
platform still hold. The owner agreed to the open model and to the two levels
of invariants below. The rest is open. Nothing here is built, and no milestone
builds it yet.

- **Four layers:**
  - The platform: the engines. They hold no domain and never name a built-in
    module.
  - Built-in modules: configuration that Axis maintains, versions and ships
    with the installation, compiled by the same compiler as any application.
    They are not platform code.
  - Built-in applications: thin applications that declare modules, a site,
    the role slot mapping of
    [D89](d89-modules-that-hold-processes-pages-and-policies-and-depend-on-other-modules.md)
    and texts.
  - The business's customization: a layer over the modules, in the
    business's own application.
- **Composing:** a larger application declares more modules, for example
  `organization`, `employees`, `leave` and `purchasing`. There is no
  application inside an application. Applications still share no stored data,
  as [D40](d40-reusable-modules.md) says.
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
- **Two levels of invariants:**
  - **Platform invariants never relax:** server-side default-deny
    authorization, audit records, one decision per task, durable execution and
    release pinning.
  - **Module invariants relax only on purpose:** an application may relax a
    named point of a module's contract with a `relax` entry that gives a
    reason. The release is then marked off-standard. Studio and the release
    history show the mark, the module's upgrade promise no longer covers that
    point, and the contract scenarios that test it are skipped and listed.
- **A layer, not a copy:**
  - The application's changes to a module are stored as a layer over the
    module version it pins. The layer names what it changes by stable ids: of
    resources, and of the elements inside them, such as process steps, fields,
    form sections and policy grants.
  - Compilation applies the layer to the module, then checks the result,
    contract included.
  - An upgrade is a three-way merge of the old module version, the new one and
    the layer. A change to an element that the new version did not touch
    applies as it is. An element that both changed is a conflict. Studio shows
    it, and the author resolves it before the next publish.
  - A change to a module's contract is a new major version of the module.
- **Extension points are stable seams, not limits:** a module may mark rule
  slots and process slots as extension points. A change made there is
  promised never to conflict on an upgrade within the same major version. A
  change made elsewhere is still allowed, and may need a merge on an upgrade.
- **Eject:** an application may copy a module into its own resources, as a
  template copy does
  ([D51](d51-creating-applications-and-resources-from-studio.md)). It then gets
  no upgrades and no contract.
- **Studio:** shows each module resource with the application's layer applied,
  marks what the layer changed, shows the contract read-only, and lists the
  conflicts after an upgrade. Modules themselves are still not edited in
  Studio.
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
- **Samples:** the samples become the first built-in modules and applications
  over time, for example `departments` as `organization` and leave requests as
  `leave`. `samples/` then keeps only the fixtures for platform features that
  no built-in module uses.
- **Open questions:**
  - The first domain, and the two or three real businesses whose rules shape
    its contract.
  - Whether two applications may share a module's data, such as one employee
    list for HR and purchasing. D40 says no, and this proposal keeps that.
  - Who writes a customization: a business analyst in Studio, a partner or a
    developer.
  - How fine an element is for the merge. An expression would be one element,
    with no merge inside it.
  - Module migrations for changes that are not additive.
  - The format: [D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)
    changes each format in place until the first production release. Layers
    that businesses keep need format versions after that.
- **Effect on earlier decisions, once agreed:** D40's rule that a module only
  grows additively gives way to contract versions and migrations. D89's
  "Studio does not edit modules" gains the layer above. D46's "samples only
  prove the platform" and the samples rule of [AGENTS.md](../../AGENTS.md)
  gain built-in modules beside the samples.

_Why:_ a module that allows changes only where its author opened a slot
becomes a cage, because its author cannot know every business. A business
then copies the module and loses every upgrade. A database shows the other way:
the code may do anything, while its constraints refuse every bad write,
whatever code makes it. Binding the contract to the module's data and
transitions, not to its process steps, lets a business change the process
anywhere and still never break the contract. Storing the changes as a layer
over stable ids, not as a copy, keeps upgrades possible. The cost is a merge
when a business changed what an upgrade also changes, a contract that is hard
to write well, and a large amount of new platform work: lifecycles with
guards, core validations, transition effects, element ids, layers, the
three-way merge and contract scenarios on publish.
