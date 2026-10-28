# D40. Reusable modules

Status: **Agreed**

Modules are versioned folders of resources that applications reuse.
The manifest, installation, declaring, pinning, the module files stored in
the release, and the compile of a module's entities, sequences, seeds, events,
data sources and forms with ids derived from the application are built. So are the
`target`, `sequence` and `dataSource` references, the module texts that join the
application's texts by locale, a module's rules and the calls to them from
application expressions, module validations that call rules of their own
module, and the codes `AXC0207` to `AXC0214`. So are activating
module entities, seeding and the record API for module entities. So is the
sample module `departments`. See the
[Modules section](../reference/modules-and-templates.md#modules) of the configuration
reference for the manifest, the diagnostic codes and the rules, and
[storage](../reference/storage.md) for the derived ids. The owner
confirmed these points on issue #571 on 2026-10-18.

- **What a module is:**
  - A folder of resources with a manifest, `module.json`, of kind `module`.
    The manifest has an `id` (lower-case letters, digits and `-`), a SemVer
    `version`, a `label` and optional `packages`.
  - A module may hold entities, data sources, rules, sequences, forms, events,
    texts and seeds. Processes, pages, roles and policies stay out, because
    they would need application roles and sites to be known inside the module.
    Any other kind in a module is a diagnostic.
  - A module does not depend on other modules.
  - [D89](d89-modules-that-hold-processes-pages-and-policies-and-depend-on-other-modules.md)
    replaces these two points in M20: a module may hold processes, pages and
    policies, uses role slots instead of roles, and may depend on other
    modules.
- **Installation and pinning:**
  - Modules are installed per installation under
    `Modules:Path/<id>/<version>/`, as packages are. The server, the worker and
    the scenario runner read them.
  - `application.json` declares `modules`, each with an `id` and a version
    range of the same forms as packages: `x.y.z`, `^x.y.z` or `~x.y.z`.
  - Compilation pins the highest installed version in each range. It copies the
    module's files into the release under `modules/<id>/`, so the content hash
    covers them.
  - A release keeps working when its module version is later removed from the
    installation, because the release holds the files.
  - A newer installed version in range gives a new release at the next
    activation, as for packages.
- **References:**
  - An application resource names a module resource as `<module id>/<name>`,
    for example `departments/Department`.
  - A module resource names only resources of its own module.
  - A reference to an undeclared module, or to a name the pinned version lacks,
    is a diagnostic.
  - The record API writes a module entity's name as two path segments,
    `{module id}/{entity name}`, for example
    `/api/apps/{app}/entities/departments/Department/records`.
  - An application expression calls a module rule as `<module id>:<name>(…)`,
    for example `departments:IsManager(x)`, with no spaces around the `:`. `/`
    is already division and `.` is already a path step, so the `:` keeps every
    expression that parses today unchanged. The owner confirmed this syntax on
    issue #617 on 2026-10-18. A call to a rule of an undeclared module is
    `AXC0207`.
  - The data source endpoint writes a module data source's name as two path
    segments, `{module id}/{data source name}`, for example
    `/api/apps/{app}/data-sources/departments/Departments/rows`. This matches
    how the record API names module entities. Only that form reaches it.
- **A copy per application:**
  - Module entities, sequences and seeds belong to the application that uses
    the module. Applications never share stored data. Shared master data would
    need cross-application reads and grants, which this design leaves out.
  - The id of a module resource inside an application is derived from the
    application id and the module resource id, as a UUID v5. So two
    applications in one tenant get separate tables and separate counters.
  - A module upgrade that is not additive for its entities is a diagnostic.
    Modules ship no migrations, so an application also cannot stop using
    a module once its tables exist.
- **Texts:**
  - A module's text keys start with `<module id>.`. A module ships one text
    resource per locale.
  - Every locale of the application needs the module's texts for that locale.
    A missing one is a diagnostic.
- **Packages:** `module.json` keeps an optional `packages`, with the same
  shape as in `application.json`. No kind a module may hold can call a
  package operation yet, because only process steps can.
- **Sample:** `samples/modules/departments`, version `1.0.0`. It has the
  `Department` entity with code, name and manager, a data source over it, a rule
  that tells whether a user manages a department, seed departments, and `en`
  and `vi` texts. The build, dev, Compose and E2E scripts install it under
  `artifacts/modules`, as they do the sample package. The second sample
  application uses it. The purchase request sample keeps its own `Department`
  entity.

_Why:_ [Authoring and packaging](../domain/knowledge.md#authoring-and-packaging)
warns against hidden dependencies, so a module is declared, pinned and copied
into the release, and a missing name fails the compile. Reusing the ranges,
pinning and installation folder of
[D28](d28-extension-packages.md) keeps one way to version reusable parts.
Entity tables are named after the entity id, so a module used by two
applications in one tenant would share one table. Deriving the id per
application removes that risk and keeps a module's id stable across its
versions. The cost is that each application holds its own data, and a module
can only grow additively until modules can ship migrations.
