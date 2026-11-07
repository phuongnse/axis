# D40. Reusable modules

Status: **Agreed**

## Context

Applications need the same parts again and again, such as departments with their managers.
Copying them by hand makes every copy drift, and a shared part that an application uses without
declaring it is a hidden dependency that fails only at run time, as
[authoring and packaging](../domain/knowledge.md#authoring-and-packaging) warns. Entity tables are
named after the entity's id, so one entity used by two applications in a tenant would share a
table.

## Decision

- **A module is a versioned folder of resources** with a manifest, `module.json`, that gives its id,
  SemVer version and label. It holds entities, data sources, rules, sequences, forms, events, texts
  and seeds, and the processes, pages, policies, triggers, scenarios and contracts that
  [D89](d89-modules-that-hold-processes-pages-and-policies-and-depend-on-other-modules.md),
  [D103](d103-modules-hold-triggers-scenarios-and-connector-slots.md) and
  [D121](d121-how-a-module-contract-is-declared-and-checked.md) add. It never holds roles, and
  it may depend on other modules, as D89 says.
- **Installed, declared and pinned like packages.** Modules are installed per installation.
  `application.json` declares each module it uses with a version range of the forms that
  [D28](d28-extension-packages.md) gives packages, and compilation pins the highest installed version
  in range and copies its files into the release.
- **Named by module.** An application names a module resource as `<module id>/<name>` and calls a
  module rule as `<module id>:<name>(…)`. A module resource names resources of its own module and
  of the modules it declares. A reference to an undeclared module, or to a name the pinned version lacks, is a compile error.
- **A copy per application.** A module's entities, sequences and seeds belong to each application
  that uses it. Each module resource gets an id derived from the application's id and its own, so
  two applications in a tenant get separate tables and counters, and applications never share
  stored data.
- **Texts travel with the module.** Its text keys start with `<module id>.`, and it ships texts for
  every locale of each application that uses it.
- **An upgrade keeps the data.** A newer version that drops an entity or a field, or changes a
  field's type, without a module migration is refused at activation.
- **Module migrations** _(planned for M29)_. A module ships migrations, which activation runs on each
  application's copy, as [D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md)
  says.

The manifest, the rules and the diagnostic codes are in
[modules](../reference/modules-and-templates.md#modules).

## Consequences

- A module's dependencies are declared, pinned and copied into the release, so a release keeps
  working after its module version is removed from the installation, and a missing name fails the
  compile.
- Modules and packages share one way to version, install and pin reusable parts.
- A module resource keeps its derived id across the module's versions.
- Each application holds its own data, so there is no master data that applications share.

## Alternatives

- **One table per module entity, shared by the applications of a tenant.** Shared data would need
  reads and grants across applications.
