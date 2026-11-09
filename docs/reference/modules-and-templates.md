# Modules and application templates

This page describes the two ways an application starts from configuration that someone else wrote. A
module is a versioned folder of resources that an application declares and uses as its own, and an
application template is an application folder that Studio copies to create a new application. The
modules and the application that Axis ships are in [built-in modules](built-in-modules.md), how an
application changes a module is in [customization layers](customization-layers.md), and what a module
guarantees whatever an application changes is in [module contracts](module-contracts.md).

## Modules

A module is a versioned folder of resources that applications reuse. An application declares the
modules it uses in `application.json`, with a version range for each and a map from the module's role
slots to its own roles:

```json
{
  "id": "219102bc-864f-42ed-874a-c19eaee0d1ba",
  "kind": "application",
  "name": "PurchaseRequests",
  "formatVersion": 1,
  "modules": [{ "id": "organization", "version": "^1.0.0", "roles": { "admin": "department-manager", "member": "employee" } }]
}
```

The module's manifest is `module.json` at the root of its folder:

```json
{
  "kind": "module",
  "id": "organization",
  "version": "1.0.0",
  "label": "Organization",
  "roles": [
    { "name": "admin", "label": { "textKey": "organization.roles.admin" } },
    { "name": "member", "label": { "textKey": "organization.roles.member" } }
  ]
}
```

`organization` is a [built-in module](built-in-modules.md), and `department-budgets` is the
[sample module](#the-department-budgets-sample). `scripts/build.sh`, `scripts/dev.sh` and the E2E run
install them under `artifacts/modules/organization/1.0.1/` and `artifacts/modules/department-budgets/1.2.0/`,
the Compose images hold them in `/modules`, and the server and the worker read them through
`Modules:Path`.

### Manifest

`module.json` has `kind: module`, an `id` of lower-case letters, digits and `-` that starts with a
letter, a SemVer `version` and a `label`. Four properties are optional:

| Property     | What it holds                                                                                                                                                |
| ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `packages`   | The extension packages that the module's process steps call, in the shape of [an application's](extension-packages.md#declaring-packages-in-an-application) |
| `roles`      | The [role slots](#role-slots)                                                                                                                                |
| `connectors` | The [connector slots](#connector-slots)                                                                                                                      |
| `modules`    | The modules it depends on, see [dependencies](#dependencies)                                                                                                 |

A module holds files of the kinds `entity`, `dataSource`, `rule`, `sequence`, `form`, `page`,
`process`, `event`, `text`, `seed`, `policy`, `contract`, `scenario` and `trigger`, a trigger being of
type `event`, `dataChange` or `schedule`. A file of any other kind, such as `role`, `site`,
`identityProvider`, `connector`, `endpoint`, `webhook`, `migration` or `layer`, is `AXC0202`. A module
reaches the roles and connectors of an application only through its slots.

### Installation

Modules are installed per installation under `Modules:Path/<id>/<version>/`. The server, the worker and
the scenario runner read them, and the scenario runner takes the folder with its `--modules` option.
Without the setting no module is installed, and a setting that names a missing folder stops the host.
A module version with a bad manifest, or with folder names that differ from its manifest, is left out
and logged as a warning at start, and the other versions still load.

### Declaring and pinning

An application's `modules` is optional. Each entry has an `id` and a `version` range, in the forms
that packages take: `1.0.0`, `^1.0.0` or `~1.0.0`. It also maps the module's role and connector slots,
and may [relax](module-contracts.md#relax-entries) points of its contract.

- **Pinning.** Compilation picks the highest installed version in each range. It does the same for
  every module that a pinned module depends on, all the way down, and gives each module id one version,
  see [resolving the graph](#dependencies).
- **The release holds the files.** Compilation copies the pinned version's files into the release as
  `modules/<id>/…`, so the content hash covers them, and the pinned version is the one in the copied
  `modules/<id>/module.json`. There is no other release table. A release keeps working when its version
  is removed from the installation later, because the release holds the files.
- **A newer version.** A newer installed version in range gives a new release at the next activation,
  as for packages. A [customization layer](customization-layers.md#versions-until-m29) that names a key
  the newer version dropped fails that compile, and the release in use stays.
- **A reserved folder.** An application file under `modules/` is `AXC0212`, because releases reserve
  that folder.

### Names across modules

A resource names a resource of another module as `<module id>/<name>`, such as
`organization/Department`, and a rule of another module as `<module id>:<name>(…)`, with no spaces
around the `:`. These rules say which module a name may reach and which code a wrong name gets.

- **The application** names resources only of the modules that `application.json` declares, in a
  field's `target` and `sequence`, the `target` of a data source parameter and of an endpoint input, a
  data source's and a form's `entity`, a policy grant's `entity` and `dataSource`, a table widget's
  `dataSource`, a process `entity`, a raise event step's `event`, an event trigger's `event`, a task's
  `form`, the `process` of a trigger, a `startProcess` action, a process inspector and a sub-process
  step, an endpoint's `entity` and `process`, and a site navigation entry's `page`.
- **An undeclared module** in one of these names is `AXC0207`, at the property that holds it, with
  these exceptions: an event under an undeclared module keeps the unknown-event code, a task `form` the
  unknown-form code, and a policy grant `entity` `AXC0092`. A name under a declared module that could not
  be pinned is not reported again. An unknown name inside a declared module keeps its usual code, such
  as `AXC0012`, `AXC0042`, `AXC0066` or `AXC0074`.
- **A module** names resources of its own module by their plain names, and resources of a module that
  its `module.json` declares as `<module id>/<name>`. This holds for a field's `target`, a data source
  parameter's `target`, and the `entity` of a data source, a form and an event. A field's `sequence`
  names only a sequence of its own module. A name with `/` whose first part is not a module that the
  module declares, the module's own id included, is `AXC0208`, and so is any `sequence` with `/`. A
  module resource never gets `AXC0207`. What a module's pages, processes, triggers and scenarios name is
  under [processes, pages, policies and dependencies](#processes-pages-policies-and-dependencies).
- **An application calls a module rule** as `organization:IsDelegating(from, until)`, from a rule, a
  validation, a data source filter, a process condition or a policy filter. The rule name ignores
  letter case and the module id matches exactly. A module that `modules` does not declare is `AXC0207`
  at the expression's pointer, and a rule that the declared module lacks is `AXC0050`. The interpreter
  runs the rule's body, and a filter's SQL inlines it with the module rules it calls. A call chain from
  an application rule into module rules counts toward the 8-deep limit, `AXC0065`. See
  [qualified rule calls](expressions.md#names-and-references).
- **A module calls rules.** A module rule, and a module entity's validations and field defaults, call
  rules of their own module by plain name, such as `isActive(x)`, and rules of a declared module by
  qualified name. A call to an application rule, or a qualified call to an undeclared module or with
  the module's own id, is `AXC0050` in the module file, at `/expression` or
  `/validations/{i}/expression`, and not `AXC0207`, because that code tells the author to fix
  `application.json`. The rule checks `AXC0055`, `AXC0056`, `AXC0057` and `AXC0065` and the type check
  apply to a module rule, in the module file. The compiler checks module rules in dependency order, so
  a module in a dependency cycle, `AXC0296`, may also get `AXC0050` for a call back into the cycle. A
  module data source filter sees no rule of its own module, and calls rules of the modules that its
  module declares by qualified name.
- **The reference index.** A rule call adds no edge to the reference index, and the 8-deep limit
  counts the rule chain only. A name that a module gives to a resource of a declared module does add an
  edge, so "where is this used" on a module entity lists the fields of the modules that depend on it.

### A copy per application

Each application that uses a module gets its own copy of the module's resources, so two applications
in one tenant never share module data.

- **Names and ids.** In the application, each module resource is named `<module id>/<name>`. Its id is
  a UUID v5 of the application `id` and the id written in the module file, so two applications get
  separate tables and counters, and the id does not change with the module version. The compiler builds
  the derived id into the model, and a diagnostic in a module file keeps the id written in that file.
  See [the physical names in storage](storage.md). The compiler keeps each module rule as
  `<module id>/<name>`, and the record API runs its body in each application that uses the module.
- **Order.** Module entities come before the application's own entities in the model, in the order of
  the pins and then by path. The pins list the application's `modules` entries as written, then each
  module they depend on in the order it is first reached.
- **Tables and routes.** Activation provisions the module's entities as the application's own tables.
  The record API writes a module entity's name as two path segments, as in
  `/api/apps/{app}/entities/organization/Department/records`, see
  [the record API routes](record-api.md#routes), and the data source endpoint does the same, as in
  `/api/apps/{app}/data-sources/organization/Departments/rows`, see
  [the data source endpoint](data-sources.md#endpoint).
- **Seeds.** The startup seeding step applies a module's seeds for each application that uses the
  module, into that application's table. Each application keeps the record ids written in the module
  file, so its own seeds can reference a module seed record by that id.

### Upgrades and migrations

A new module version must keep the tables that applications already hold, so a module upgrade is
additive for its entities. How an application's layer follows a newer version is in
[customization layers](customization-layers.md#versions-until-m29).

- **Additive.** Activation compares each module copy in the release with the copy in the application's
  active release. It matches entities by the id written in the module file and fields by name, ignoring
  letter case. Dropping an entity, dropping a field and changing a field's `type` are refused with
  `AXC0213`, and nothing is applied. Other field properties are not compared. Adding a process, page or
  policy is additive.
- **Removing a module.** Removing a declared module from an application whose active release uses it
  counts as dropping each of its entities, so an application cannot stop using a module once its tables
  exist.
- **Migrations.** A module ships no migrations. An application `migration` may name an entity of a
  module that `application.json` declares, as `<module id>/<entity>`, and then drops or converts a
  column of the application's copy. A migration that names an entity of a module the application does
  not declare, or that drops a module entity, is `AXC0214`.

### Upgrades with module migrations

_(planned for M29)_

A module that renames or removes an element says so in a migration of its own, so an upgrade can carry
each application's data and customization layer across the change.

- **Module migrations.** A module that renames or removes an element declares it in a migration, with
  the change kinds of an application [migration](migrations.md#migrations), such as `renameField`,
  `dropField` and `convert`. Building a module compares it with its previous version, and an element
  that is gone without such a migration fails the build.
- **Applying them.** Activation runs a module's migrations on each application's copy. A rename
  rewrites the references of the application's [layer](customization-layers.md), and a removed element
  that the layer uses is a diagnostic.
- **Three-way merge.** An upgrade merges the old version, the new one and the layer, property by
  property within each element. A property that the new version did not touch applies as the layer
  wrote it, and a property that both changed is a conflict. An expression is one property, with no merge
  inside it. Studio shows each conflict, and the author resolves it before the next publish.
- **Contract changes.** A change to a module's [contract](module-contracts.md) is a new major version.
- **Extension points.** A module may mark rule slots and process slots as extension points. A change
  made there never conflicts on an upgrade within the same major version. A change elsewhere is allowed
  and may need a merge.
- **Eject.** An application may copy a module into its own resources, as a
  [template copy](#application-templates) does. The copy gets no upgrades, and no module contract holds
  on it.

### Texts

A module ships one `text` resource for each locale, and its keys start with `<module id>.`, or
`AXC0209` is reported. Its texts join the application's texts of the same locale, ignoring letter case,
so labels in module resources resolve.

- Each locale of the application needs the text of every module the application uses, declared or only
  depended on, or `AXC0210` is reported. A module text in a locale the application does not have is
  ignored, without a diagnostic.
- `AXC0026` and `AXC0027` check each module's texts against each other, apart from the application's
  texts. A module key joins the `AXC0028` check only in the locales the application has.
- An application text key may not start with the `<id>.` prefix of a module the application uses,
  declared or only depended on, or `AXC0211` is reported.

### Diagnostics

A diagnostic in a module file has `file` set to `modules/<id>/<relative path>` and `resourceId` set to
the id declared in the file. The codes of modules are `AXC0200` to `AXC0214`, `AXC0292` to `AXC0307`
and `AXC0335`, and [diagnostic codes](diagnostic-codes.md) says what each means and where it is
reported.

### Processes, pages, policies and dependencies

A module can ship a working feature with its screens, permissions, triggers and outside calls, and
build on other modules. This section says how a module reaches the roles and connectors of the
application that uses it, and what its processes, pages, policies, triggers and scenarios may name.

```json
{
  "kind": "module",
  "id": "department-budgets",
  "version": "1.2.0",
  "label": "Department budgets",
  "roles": [
    { "name": "approver", "label": { "textKey": "department-budgets.roles.approver" } },
    { "name": "administrator", "label": { "textKey": "department-budgets.roles.administrator" } }
  ],
  "modules": [{ "id": "organization", "version": "^1.0.0", "roles": { "admin": "administrator", "member": "approver" } }]
}
```

#### The department budgets sample

`department-budgets`, in `samples/modules/department-budgets`, is the sample module that the purchase
request sample declares. It holds:

- a `Budget` entity with a name, an `organization/Department` reference, a year, an amount, a
  `requester` creator field and a required `status` that defaults to `draft`, and a `BudgetDecision`
  entity with the budget, the amount and `decidedBy`;
- a `BudgetReview` form, a `Budgets` data source and page, and a `BudgetForm` page. The `Budgets` page
  has a `New` toolbar action that opens `BudgetForm`, and a `Submit for approval` row action that starts
  `BudgetApproval` for the budget;
- a `BudgetApproval` process whose `review` task goes to the `approver` slot, an `ApproverAccess`
  policy, and `en` and `vi` texts;
- a `BudgetContract` on `Budget`, whose lifecycle has `draft` as initial and deletable, and `approve`
  and `reject` from `draft`. Both have the guard `accountableUser() != null and accountableUser() != requester`,
  so the creator of a budget cannot decide it and a second manager must. The core validation
  `amountFrozenOnceDecided` refuses an amount change once the budget is decided, the effect
  `decisionRecorded` creates a `BudgetDecision` when a budget is approved, and the `approver` ceiling is
  `read`, `create` and `update`;
- the scenario `ApproverCreatesBudget` and the
  [contract scenarios](module-contracts.md#contract-scenarios) `RequesterCannotDecideOwnBudget`,
  `ApprovalRecordsDecision` and `DecidedAmountIsFrozen`.

Its second role slot, `administrator`, grants nothing on budgets. It passes the `admin` slot of
`organization` on to a role of the application, because the two `organization` slots must reach two
different roles. An application that declares it maps both modules' slots to roles of its own:

```json
"modules": [
  { "id": "organization", "version": "^1.0.0", "roles": { "admin": "operator", "member": "department-manager" } },
  { "id": "department-budgets", "version": "^1.2.0", "roles": { "approver": "department-manager", "administrator": "operator" } }
]
```

#### Role slots

A role slot is a role that a module's policies, tasks and scenarios name, and that each application
maps to a role of its own.

- **Declaring.** `roles` in `module.json` lists the slots, each with a `name`, written like a role
  name, and a `label`. Slot names are unique in the module, ignoring letter case, or the version fails
  at host start with `AXC0200` at `/roles/{i}/name`. A label's text key starts with `<module id>.`, or
  `AXC0209` is reported at `/roles/{i}/label/textKey`.
- **Mapping.** Each `modules` entry of an application has `roles`, which maps every slot of that
  module to a role the application declares. A slot left unmapped is `AXC0292`, a slot mapped to an
  undeclared role is `AXC0293`, and a key that is no slot of the pinned version is `AXC0294`. Slot and
  role names are matched exactly. The compiled model keeps the map of each pinned module, and a module
  that the application reaches only as a dependency gets its map through the chain, see
  [dependencies](#dependencies).
- **Holding a slot.** A user who holds the mapped role in the application holds the slot there. A
  module task that names a slot goes to the people who hold the mapped role in that application.
- **What names a slot.** A module policy's `roles`, a module task step's `assignee.role` and an
  escalation's `reassign.role` name slots of their own module. Anything else, an application role
  included, is `AXC0304` in a policy and `AXC0307` in a task. A module task can still name a user with
  `assignee.user`.

#### Connector slots

A connector slot is an outside call that a module's processes make, which each application maps to a
connector of its own.

- **Declaring.** `connectors` in `module.json` lists the slots. Each has a `name`, a `label` and
  `operations`, each with a `name` and typed `input` and `output` fields in the shape of a
  [connector operation](integration-resources.md#connector-resource). Slot names, the operation names
  of a slot and the field names of an input or output are unique, ignoring letter case, or the version
  fails at host start with `AXC0200`. A label is a `textKey` that starts with `<module id>.`, or
  `AXC0209` is reported at `/connectors/{i}/label/textKey`.
- **Mapping.** Each `modules` entry of an application has `connectors`, which maps every slot to a
  connector the application declares, as `"connectors": { "orders": "PurchasingSystem" }`. The
  compiled model keeps the map from slot to the connector's declared name, and the application's
  connector keeps its base URL, auth, bindings, timeout, idempotency header and lookup.
- **Checks of the mapping.** The connector must have each operation of the slot, every input and
  output of the slot with the same type, and no required input that the slot lacks. Slot keys are
  matched exactly, and connector, operation and field names ignoring letter case. A field's `required`
  is not compared for the slot's own fields. The codes, each with a message that names the slot, the
  operation and the field:

  | Problem                                                  | Code      | Reported at                                                                  |
  | -------------------------------------------------------- | --------- | ---------------------------------------------------------------------------- |
  | A slot is not mapped                                     | `AXC0297` | `/modules/{i}/connectors`, or `/modules/{i}` when the entry has none         |
  | A slot is mapped to an undeclared connector              | `AXC0298` | `/modules/{i}/connectors/{slot}`                                             |
  | A key is no slot of the module                           | `AXC0299` | `/modules/{i}/connectors/{slot}`                                             |
  | The connector lacks an operation of the slot             | `AXC0300` | `/modules/{i}/connectors/{slot}`                                             |
  | An input is of another type, or the operation lacks it   | `AXC0301` | `/modules/{i}/connectors/{slot}`                                             |
  | A required input of the operation is missing in the slot | `AXC0302` | `/modules/{i}/connectors/{slot}`                                             |
  | An output is of another type, or the operation lacks it  | `AXC0303` | `/modules/{i}/connectors/{slot}`                                             |

- **Calling a slot.** A module process's `callConnector` step names a slot of its module, matched
  exactly, and one of the slot's operations, ignoring letter case, as in
  `"connector": { "name": "orders", "operation": "createOrder" }`. Its `input` and `output` are checked
  against the slot operation's fields, not against any one application's connector, with the codes
  `AXC0104` to `AXC0108` of an application step, in the module's process file. A name that is no slot of
  the module, an application connector's name included, is `AXC0102` at `/steps/{i}/connector/name`,
  and an operation the slot lacks is `AXC0103` at `/steps/{i}/connector/operation`. The inputs a step
  must give are those the slot marks `required`. The step adds no reference, because a slot is no
  resource.
- **In each application** the compiled step names the connector that the slot maps to and its
  operation, so the call keeps that connector's settings, and the worker runs it like an application
  step. Two applications that map one slot to different connectors call different systems. A module
  never names a binding.

#### Module policies

A module policy is a `policy` file in the module, which gives the module's role slots what the
module's features need, so an application that only maps the slots works.

- **Names.** In an application it is named `<module id>/<name>`, and its id is derived from the
  application `id` as for other module resources.
- **Roles.** Its `roles` name role slots of its own module, matched exactly, and anything else, a role
  included, is `AXC0304`. Each slot becomes the role that the application maps it to, so the compiled
  policy names that role, and a person who holds the role gets the grants in that application and in
  no other. A slot that the application does not map names nobody. A module that the application
  reaches only as a dependency gets its slot map through the chain, so its policies grant the role that
  the chain reaches.
- **Grants.** A grant names an entity, data source or process of its own module by its plain name. A
  name with `/`, a declared module's included, an `endpoint` grant and the plain name of an application
  entity, data source or process are `AXC0305`. An unknown plain name keeps `AXC0092`. A grant's
  `filter` calls only rules of the modules that its module declares, by qualified call, as a module
  data source filter does.
- **Default deny** still holds. Without the mapped role a person gets `403` on the module's entities
  and data sources, and on starting or operating its processes, which a `process` grant allows.

#### Module pages

A module page is a `page` file in the module, which ships a screen with the feature.

- **Names.** In an application it is named `<module id>/<name>`, and its id is derived from the
  application `id` as for other module resources.
- **What it names.** Its widgets name resources of its own module by their plain names: the `entity`,
  `form`, `dataSource` and `process` of a widget, the `page` of a `navigate` action in the widget's
  `actions`, `rowActions` and `toolbarActions` or in a KPI's `navigate`, and the `target` of a page
  parameter. The compiler rewrites each to the qualified name. A name with `/`, a declared module's
  included, is `AXC0208` at that property of the module file. A name that the module lacks, such as a
  data source of the application, gets the usual unknown-name code in the module file. Text keys in the
  page resolve against the merged texts, as in module forms.
- **In a site.** A module holds no site, so the application's site navigation lists a module page by
  its name, such as `{ "page": "organization/Departments" }`, for a module that `application.json`
  declares. Another module is `AXC0207` at `/navigation/{i}/page`. An application page's `navigate`
  action names a page by its plain name, so it leads only to the application's own pages.
- **Addresses.** The page metadata API writes the name as two path segments, as in
  `GET /api/sites/{site path}/pages/{module id}/{name}`. In the browser a module page opens at
  `/{site}/{module id}/{name}`, and its form pages one segment deeper, at
  `/{site}/{module id}/{name}/new` and `/{site}/{module id}/{name}/{id}`. A second address segment that
  is no record id names a module page, so a module page named `New` cannot be opened: `/{site}/{module id}/new`
  is the address of a new record on the application page named like the module.
- **Access.** Who may read the records is up to the grants of the application's policies.

#### Module processes

A module process is a `process` file in the module, which works on an entity of its own module.

- **Names.** In an application it is named `<module id>/<name>`, and its id is derived from the
  application `id` as for other module resources.
- **What it names.** Its steps name resources of its own module by their plain names: the `entity` of
  the process and of a `createRecord` step, the `form` of a task, the `event` of a step and the
  `process` of a sub-process step. The compiler rewrites each to the qualified name. A name with `/` is
  `AXC0208` in the module file, except that an `entity` or an `event` may name a module that the module
  declares. A task `form` and a sub-process `process` never name another module.
- **What it sees.** The process sees no application form, process, page, role, connector or rule, and
  naming one gives the usual unknown-name code in the module file. Its expressions see the rules of its
  own module by plain name.
- **Packages.** A package operation step calls an operation of a package in the module's `packages`,
  checked against the pinned version. A package that the module does not list is `AXC0150` in the
  module file. The pins follow the
  [pinning rule](extension-packages.md#declaring-packages-in-an-application) of the release.
- **Assignees and calls.** A task assignee names a [role slot](#role-slots), which the compiled task
  replaces with the role the application maps it to, and a `callConnector` step names a
  [connector slot](#connector-slots).
- **Starting it.** The application starts it from a trigger, a `startProcess` action, a sub-process
  step or an endpoint, by the name `<module id>/<name>`, and the process start endpoint takes it at
  `POST /api/apps/{app}/processes/{module}/{process}/instances`.
- **Running it.** The worker runs a module process instance on the application's own copy of its
  module entity, and a waiting instance finishes on the release it was started on, even after a newer
  module version is activated. A module task assigned to a slot is listed in the task inbox of the
  people who hold the mapped role, and only they can decide it.

#### Module triggers

A module trigger starts a process of its own module when something happens in the application that
uses the module, and only there.

- **What it names.** Its `process` names a process of its own module by plain name. A name with `/` is
  `AXC0208`, and an unknown name, such as an application process, is `AXC0129`. Its `event` or
  `entity` names a resource of its own module by plain name, or of a declared module as
  `<module id>/<name>`, and anything else is `AXC0208`. An unknown name keeps `AXC0130` or `AXC0143`.
  Each is reported in the module file.
- **Each application.** In each application the trigger is named `<module id>/<name>` and has an id
  derived from the application. A create, update or delete of the application's copy of the module's
  entity, or an event raised in that application, starts the module's process in that application
  only, and its receipts and dead letters belong to that application.
- **Schedules.** A trigger of type `schedule` has no `timeZone`, and writing one is `AXC0004` at
  `/timeZone`. It runs in the time zone of each application that uses the module, so two applications
  fire the same schedule at their own local times. Its `dataSource` names a data source of its own
  module by plain name, or is left out for a scheduled job, and an application data source is unknown,
  `AXC0162`, in the module file. Each application has its own schedule state and its own
  `trigger.schedule` work item, keyed by the trigger's id for that application, so occurrences,
  catch-up after downtime and the fan-out to records stay inside that application. When an application
  changes its time zone, its next activation recomputes the next occurrence of its module schedules, as
  it does for an application schedule.

#### Module scenarios

A module scenario tests the module's features against each application that pins it.

- **What it names.** Its users, and its assignee and recipient roles, give role slots instead of roles,
  and a role that is not a slot is `AXC0335` in the module file. Its steps name entities and processes
  of its own module by plain name. A step's `entity` may also name an entity of a declared module, as
  `<module id>/<name>`, so a scenario can create the records another module holds, such as
  `organization/Employee`. A process names only the module's own. Any other name with `/` is `AXC0208`,
  and an unknown entity or process has its usual code. A connector stub names a connector slot, and a
  package stub names a package that the module lists.
- **Running.** It runs for each application that pins the module, with each slot replaced by the role
  that the application maps it to, against the application as compiled. A module's scenarios run after
  the application's own, in pin order and then by file path, in Studio and with `Axis.Scenarios`. A
  result is named `<module id>/<scenario name>`. The scenario files stay out of the release and its
  content hash.
- **Contract scenarios.** A module marks some scenarios as
  [contract scenarios](module-contracts.md#contract-scenarios), which run on every publish.

#### Dependencies

A module may build on other modules. The application resolves the whole graph flat, and each module
reaches the roles and connectors of the application through the module that depends on it.

- **Declaring.** `module.json` has an optional `modules`, with the `id` and `version` entries of an
  application's `modules`. A bad entry is `AXC0203`, `AXC0204` or `AXC0206`, in the `module.json` of
  the module that holds it. A module names resources and calls rules only of the modules it declares,
  see [names across modules](#names-across-modules).
- **Mapping slots.** A module's entry for a module it depends on maps that module's slots to its own,
  with `roles` and `connectors` as an application's entry has. Each key is a slot of the dependency and
  each value a slot of the module that holds the entry, all matched exactly. Following the chain gives
  each slot of the dependency the application role or connector that the depending module's slot
  reaches, and the compiled model keeps that map for the pin.
- **Checks of the mapping.** An unmapped slot is `AXC0292` or `AXC0297`, a value that is no slot of the
  depending module is `AXC0293` or `AXC0298`, and a key that is no slot of the dependency is `AXC0294`
  or `AXC0299`, each in the `module.json` that holds the entry. A connector slot is checked against the
  slot it maps to as it is against a connector: a missing operation is `AXC0300`, and an input or
  output of another type, or one the slot lacks, is `AXC0301` or `AXC0303`. A required input is
  `AXC0302`, checked against the connector that the chain reaches, because a slot's own `required` is
  not compared. A slot that the chain leaves without a role or connector, because the application
  leaves a slot of the depending module unmapped, gets nothing and no report beyond that of the break.
- **One mapping for each slot.** When the application declares a module and also reaches it through a
  depending module, or two depending modules map the same slot, the mappings must give the same role or
  connector. A difference is `AXC0306`, which names both mappings and is reported at the later entry.
  The model keeps the application's entry, or else the first depending module's in pin order.
- **Resolving the graph.** The application resolves every module it declares and every module they
  declare, flat. Each module id gets one version: the highest installed version that meets every range
  that asks for it. When one range asks and no installed version meets it, the code is `AXC0205`. When
  two or more ranges ask and no version meets them all, it is `AXC0295`, which names the application or
  each module that asks, with its range. A cycle between modules, a module that depends on itself
  included, is `AXC0296`, which names the cycle. The resolution picks again until nothing changes,
  without backtracking, and a graph whose picks never settle is reported as a conflict.
- **One copy each.** Each module gets one copy for each application, with ids derived from the
  application, so a module that two others depend on is pinned and copied once. Rebuilding a stored
  release resolves the same graph from the module copies it holds.
- **What the application names.** The application names resources only of the modules it declares
  itself. A name under a module that is only a dependency is `AXC0207`, as for an undeclared module. A
  process, event, trigger, seed, migration or policy `entity` still resolves to a dependency's
  resources, and a name inside a dependency's own resources that does not resolve keeps its
  unknown-name code.

#### In Studio

Studio lists the resources of every module the application uses, by `<module id>/<name>`, and opens
each in the editor of its kind with the application's layer applied, so an author's edit is saved as a
customization layer and Studio never edits the module's own files, see
[customization layers in Studio](customization-layers.md#in-studio). The
[policy matrix](studio.md#policy-matrix) lists a module policy's grants in the row of the role that its
slot maps to, read-only and tagged with the module, without an Open policy link.

## Application templates

An application template is an application folder with a `template.json` manifest, from which Studio
creates a new application by copying it, see
[creating an application](authoring-applications.md#creating-an-application). The manifest is `template.json` at the
root of the folder:

```json
{
  "kind": "applicationTemplate",
  "id": "approvals",
  "version": "1.0.0",
  "builtIn": true,
  "title": {
    "en": "Approvals",
    "vi": "Phê duyệt"
  },
  "description": {
    "en": "Departments, employees, out-of-office periods and approval requests, from the built-in organization and approvals modules.",
    "vi": "Phòng ban, nhân viên, thời gian vắng mặt và yêu cầu phê duyệt, từ các mô-đun tích hợp organization và approvals."
  }
}
```

- **Manifest.** `template.json` has `kind: applicationTemplate`, an `id` of lower-case letters, digits
  and `-` that starts with a letter, a SemVer `version`, a `title` and a `description`, each with an
  `en` and a `vi` text, and an optional boolean `builtIn`. `"builtIn": true` marks a
  [built-in application](built-in-modules.md#where-they-live), which Studio lists first. Any other value
  of `builtIn` leaves the template out.
- **One site.** A template holds exactly one site, because the site path of a copy follows the new
  name, and that rule is only clear for a single site.
- **Installation.** Templates are installed per installation under `Templates:Path/<id>/<version>/`,
  set with the environment variable `Templates__Path`, and a relative path is resolved against the
  server content root. The server reads them, and in a cell the publisher reads them for the create
  job. Without the setting no template is installed, and a setting that names a missing folder stops
  the server. A template version with a bad manifest, with folder names that differ from its manifest,
  or without exactly one site is left out and logged as a warning at start, which names the version's
  folder, such as `approvals/1.0.0`, and has no `AXC` code. The other versions still load.
- **Listing.** `GET /api/studio/templates` lists the newest version of each template, with the
  diagnostics of compiling it in memory, so a template that does not compile is still listed. Built-in
  templates come first, then the others, each group sorted by id.
- **Copy.** A copy takes every `*.json` file of the template except `template.json`, in the same
  folders. `template.json` stays out, because template metadata does not belong in an application.
  - The top-level `id` of each resource becomes a new UUID. Seed record ids stay, because they are row
    ids in the new application's own tables and cannot clash.
  - The manifest gets the new `name`, and the site's `path` becomes the kebab-case name.
  - References are by name, so they still resolve.
  - The copy has the template's diagnostics.
- **The templates there are.** The only templates are the built-in applications, which
  `scripts/build.sh` installs into `artifacts/templates`, and the Compose image holds in `/templates`.
  How an installation gets them is in [built-in modules](built-in-modules.md#where-they-live).

### Blank application

A blank application is the smallest folder that compiles with no error. A site needs at least one
navigation entry that names a page, so a blank application also holds one `Home` page with a
`taskInbox` widget, which names no entity, form or data source. For the name `PurchaseRequests`, it
holds:

- `application.json`: a new UUID `id`, `kind: application`, the `name` and `formatVersion: 1`.
- `sites/<kebab-name>.json`: the site, with the `name` of the application, the `path` set to the
  kebab-case name, the title key `site.title`, the locales
  `{ "default": "en", "fallback": "en", "available": ["en"] }` and one navigation entry to `Home` with
  the label key `nav.home`.
- `identity-providers/accounts.json`: a new UUID `id`, `kind: identityProvider`, `name: accounts`,
  `formatVersion: 1` and `type: accounts`. An application with a site needs at least one identity
  provider, and the site allows it because it has no `signIn`.
- `pages/home.json`: the page `Home` with the title key `pages.home.title` and `widgets` of one
  `taskInbox`.
- `texts/en.json`: the `en` text resource, with three keys: `site.title`, which equals the application
  `name`, `pages.home.title` and `nav.home`.

Every resource gets a new UUID `id`. The site JSON is:

```json
{
  "id": "0f6c1c1e-3f43-4a52-8f27-0b2b6a6a3d11",
  "kind": "site",
  "name": "PurchaseRequests",
  "formatVersion": 1,
  "path": "purchase-requests",
  "title": { "textKey": "site.title" },
  "locales": { "default": "en", "fallback": "en", "available": ["en"] },
  "navigation": [{ "page": "Home", "label": { "textKey": "nav.home" } }]
}
```

A `name` whose kebab-case form is longer than 60 characters, or is a reserved site path (`api`,
`health`, `assets`, `studio`, `account`, `users` or `licences`), is refused with `400`, so a blank
application cannot fail with `AXC0024` or a schema error.
