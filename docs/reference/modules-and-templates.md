# Modules and application templates

Detailed reference for reusable modules, which an application declares and builds on, and for
the application templates that Studio creates new applications from.
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

## Modules

This section follows
[D40](../decisions/d40-reusable-modules.md). The manifest,
installation, declaring, pinning, dependencies between modules, the diagnostics `AXC0200` to `AXC0214`,
`AXC0295` and `AXC0296`, the
role slots with their mapping, `AXC0292` to `AXC0294`, the connector slots with their
mapping, `AXC0297` to `AXC0303`, the maps of a depending module, with `AXC0306`, the module
policies that name role slots, `AXC0304` and `AXC0305`, and the task assignees that name role slots,
`AXC0307`, are built. A pinned module's files are stored in the release and
checked as JSON, for an allowed kind and against the schema of their kind. A
pinned module's entities, sequences, seeds, events, data sources, forms and policies join the application
model, each with an id derived from the application `id` and the name
`<module id>/<name>`. A field's `target` and `sequence`, a data source's and a
form's `entity` and a policy grant's `entity` can name them. A field's
`target` also covers data source parameters and endpoint inputs. A process
`entity`, a raise event step's `event` and an event trigger's `event` can name
a module entity or event. A table widget's `dataSource` and a policy grant's
`dataSource` can name a module data source. A site's navigation entry can name a module page. A pinned module's texts join the application's texts
of the same locale, so its labels resolve. A pinned module's rules are checked
with the rule checks, inside their module, and application expressions call them,
see [References](#modules). Activation provisions the module's entities as the
application's own tables, startup seeding applies the module's seeds, the record
API serves module entities, and the data source endpoint serves module data sources.
A module policy grants the role its slot maps to, so a person who holds that role in
the application reaches the module's entities and data sources, see
[module policies](#processes-pages-policies-and-dependencies).
A module is a
versioned folder of resources that applications reuse. An application declares
the modules it uses in `application.json`, with a version range for each:

```json
{
  "id": "219102bc-864f-42ed-874a-c19eaee0d1ba",
  "kind": "application",
  "name": "PurchaseRequests",
  "formatVersion": 1,
  "modules": [{ "id": "departments", "version": "^1.0.0", "roles": { "manager": "department-manager" } }]
}
```

The module's manifest is `module.json` at the root of its folder:

```json
{
  "kind": "module",
  "id": "departments",
  "version": "1.1.0",
  "label": "Departments",
  "roles": [{ "name": "manager", "label": { "textKey": "departments.roles.manager" } }]
}
```

`departments` is the sample module, `samples/modules/departments`. It holds a
`Department` entity with a code, a name and a manager, a `Departments` data source,
an `IsManager(manager, user)` rule and an `IsDepartmentCode(code)` rule, three seed
departments and `en` and `vi` texts. The `Department` entity has a validation that
calls `IsDepartmentCode`, so a department code must be in capital letters.
It has a `manager` role slot, labelled in `en` and `vi`, and a `ManagerAccess` module
policy for the slot. The policy lets the role read the departments whose `manager` is the
current user, and read the `Departments` data source. The leave request sample maps the
slot to its `department-manager` role, and so does the purchase request sample.
`department-budgets` is the second sample module, see
[Processes, pages, policies and dependencies](#processes-pages-policies-and-dependencies).
`scripts/build.sh`, `scripts/dev.sh` and the E2E run install them under
`artifacts/modules/departments/1.1.0/` and `artifacts/modules/department-budgets/1.0.0/`, and the
Compose images hold them in `/modules`. The server and the worker read them through `Modules:Path`.

- **Manifest.** `module.json` has `kind: module`, an `id` (lower-case letters,
  digits and `-`, starting with a letter), a SemVer `version`, a `label` and an
  optional `packages`, `roles`, `connectors` and `modules`. `packages` has the same shape as in `application.json`,
  see [Extension packages](extension-packages.md#declaring-packages-in-an-application).
  `roles` lists the role slots, see Role slots below. `connectors` lists the
  connector slots, see Connector slots below. `modules`
  lists the modules it depends on, with the `id` and `version` entries of an application's `modules`, see
  [dependencies](#processes-pages-policies-and-dependencies).
- **Kinds a module may hold.** `entity`, `dataSource`, `rule`, `sequence`,
  `form`, `page`, `process`, `event`, `text`, `seed`, `policy` and `trigger`
  of type `event`, `dataChange` or `schedule`. Roles are not allowed.
  _(planned for M24)_ `scenario` is allowed too. Roles stay out.
- **Installation.** Modules are installed per installation under
  `Modules:Path/<id>/<version>/`. The server, the worker and the scenario runner
  read them. The scenario runner gets a `--modules` option for the folder.
  Without the setting no module is installed. A setting that names a missing
  folder stops the host. A module version with a bad manifest, or with folder
  names that differ from its manifest, is left out and logged as a warning at
  start, and the other versions still load.
- **Declaring.** `modules` is optional. Each entry has an `id` and a `version`.
  A range has the same forms as for packages: `1.0.0`, `^1.0.0` or `~1.0.0`.
- **Pinning.** Compilation picks the highest installed version in each range.
  It does the same for every module a pinned module depends on, all the way
  down, and gives each module id one version, see
  [resolving the graph](#processes-pages-policies-and-dependencies).
  It copies that version's files into the release as `modules/<id>/…`, so the
  content hash covers them. The pinned version is the one in the copied
  `modules/<id>/module.json`. There is no extra release table. A release keeps
  working when the version is later removed from the installation, because the
  release holds the files. A newer installed version in range gives a new
  release at the next activation, as for packages.
- **References.** An application resource names a module resource as
  `<module id>/<name>`, for example `departments/Department`. A module resource
  names resources of its own module without a `/`, and resources of a module its
  `module.json` declares in `modules` as `<module id>/<name>`. The compiler
  builds both rules for a field's `target` and `sequence` and for a data
  source's and a form's `entity`, and for a data source parameter's `target`,
  except that a `sequence` names only a sequence of its own module. A
  module event's `entity` follows the same rule, and an application process
  `entity`, raise event step `event` and event trigger `event` can name module
  resources, and so can an application task's `form`, but only under a module
  that `application.json` declares. Any other module keeps the unknown-form
  code. An application trigger's `process`, a `startProcess` action's
  `process`, a process inspector's `process` and a sub-process step's `process`
  can name a process of a declared module. A table widget's `dataSource` and a
  policy grant's `dataSource` can name a module data source. A name under a
  module that `modules` does not declare is `AXC0207`, reported at `/entity` for
  a data source or a form, at `/widgets/{i}/dataSource` or
  `/grants/{i}/dataSource` for a widget or a grant, and at `/process`,
  `{actionPath}/process`, `/widgets/{i}/process` or `/steps/{i}/process` for a
  trigger, a `startProcess` action, a process inspector or a sub-process step.
  A step or trigger that names an event under an undeclared module keeps its
  unknown-event code. A module field whose `target` or `sequence` contains `/`,
  a module event, data source or form whose `entity` does, or a module data
  source parameter whose `target` does, is `AXC0208` unless the part before the
  first `/` is a module that module declares. A `sequence` with `/`, the
  module's own id and an undeclared module are all `AXC0208`. A reference under
  a declared module that could not be pinned is not reported again, and a
  module resource never gets `AXC0207`.
  An unknown name inside a declared module keeps its existing code, such as
  `AXC0012`, `AXC0042`, `AXC0066` or `AXC0074`. A policy grant `entity` can
  name a module entity. One that names an undeclared module keeps the policy's
  unknown-entity code, `AXC0092`. A module rule calls rules of its own
  module by plain name, such as `isActive(x)`, and rules of a module it declares
  as `<module id>:<rule>(…)`, such as `departments:IsCode(x)`. The rule checks, `AXC0055`,
  `AXC0056`, `AXC0057` and `AXC0065`, and the type check of its expression
  apply to it and are reported in the module file. A call to an application
  rule, or a qualified call to a module it does not declare or to its own id, is
  `AXC0050` at `/expression` of the module file, not `AXC0207`, because that code
  tells the user to fix `application.json`. The compiler checks module rules in
  dependency order, so a module in a dependency cycle, which is `AXC0296`, may
  also get `AXC0050` for a call back into the cycle. The compiler keeps each module rule as `<module id>/<name>`.
  An application expression calls it as `<module id>:<name>(…)`, such as
  `departments:IsManager(x)`, with no spaces around the `:`. A rule, a
  validation, a data source filter, a process condition and a policy filter can
  call it. The interpreter runs its body, and a filter's SQL inlines it with the
  module rules it calls. The rule name ignores letter case and the module id
  must match exactly. A module that `modules` does not declare is `AXC0207` at
  the expression's pointer. A rule the declared module lacks is `AXC0050`. A
  call chain that passes from an application rule into module rules counts
  toward the 8-deep limit, `AXC0065`. See
  [qualified rule calls](expressions.md#names-and-references). A module
  entity's validations and field defaults call rules of their own module by
  plain name and rules of a declared module by qualified name, as a module rule
  does. A call to an application rule, or a qualified call to an undeclared
  module or with the module's own id, is `AXC0050` at
  `/validations/{i}/expression` of the module file. The record API runs the
  module rule's body in each application that uses the module. These calls add
  no rule-call edge to the reference index, and the 8-deep limit counts the
  rule chain only. A name a module gives to a resource of a declared module does
  add an edge, so "where is this used" on a module entity lists the fields of
  the modules that depend on it. A module data source filter sees no rule of its
  own module. It calls rules of the modules its module declares by qualified
  name. Module entities come before the application's own entities
  in the model, in the order of the pins and then by path. The pins list the
  application's `modules` entries as written, and then each module they depend on in the
  order it is first reached. The record API
  writes a module entity's name as two path segments, `{module id}/{entity
  name}`, for example
  `/api/apps/{app}/entities/departments/Department/records`. See the
  [record API routes](record-api.md#routes). The data source endpoint writes a
  module data source's name the same way, `{module id}/{data source name}`, for
  example `/api/apps/{app}/data-sources/departments/Departments/rows`. See
  [the data source endpoint](data-sources.md#endpoint).
- **A copy per application.** Entities, sequences, seeds, events, data sources and forms of a module belong
  to the application that uses it. The id of each module resource inside an
  application is a UUID v5 of the application `id` and the id written in the
  module file, so two applications in one tenant get separate tables and
  counters, and the id does not change with the module version. The compiler
  builds the derived id into the model. A diagnostic in a module file keeps the
  id written in that file. See
  [the physical names in storage](storage.md). A module upgrade must be
  additive for its entities: `AXC0213`. Activation compares each module copy in
  the release with the copy in the application's active release. It matches
  entities by the id written in the module file and fields by name, ignoring
  letter case. Dropping an entity, dropping a field and changing a field's
  `type` are refused. Other field properties are not compared there. Removing a
  declared module from an application whose active release uses it counts as
  dropping each of its entities. Nothing is applied when activation is
  refused. Modules ship no migrations, and a `migration` cannot name a
  module entity: `AXC0214`. So an application cannot stop using a module once
  its tables exist.
- **Texts.** A module's text keys start with `<module id>.`, or `AXC0209` is
  reported. A module ships one `text` resource per locale. Its texts join the
  application's texts of the same locale, ignoring letter case, so labels in
  module resources resolve. Each locale of the application needs the module's
  text for that locale, or `AXC0210` is reported. This covers every module the
  application uses, declared or only depended on. A module text in a locale the
  application does not have is ignored, without a diagnostic. `AXC0026` and
  `AXC0027` check each module's texts against each other, apart from the
  application's texts. A module key joins the `AXC0028` check only in the
  locales the application has. An application text key may not start with the
  `<id>.` prefix of a module the application uses, declared or only depended
  on, or `AXC0211` is reported.
- **Seeds.** The startup seeding step applies a module's seeds for each
  application that uses the module, into that application's table. Each
  application keeps the record ids written in the module file, so its own
  seeds can reference a module seed record by that id.
- **Diagnostics.** A diagnostic in a module file has `file` set to
  `modules/<id>/<relative path>` and `resourceId` set to the id declared in the
  file.

| Built code | Meaning |
| --- | --- |
| `AXC0200` | `module.json` is missing, unreadable, not JSON or fails its schema, or another file of the module version cannot be read. Reported at host start, at the failing pointer. A connector slot name, a slot operation name or a slot field name that repeats an earlier one in its list, ignoring letter case, is reported at its `name`. A copy of a module in a stored release without a valid `modules/<id>/module.json` is reported the same way, when the release is rebuilt. |
| `AXC0201` | The folder names differ from the manifest `id` or `version`. Reported at host start, at `/id` or `/version`. In a stored release, the folder `modules/<id>/` differing from the manifest `id` is reported the same way. |
| `AXC0202` | A module file has a kind that a module may not hold, or no kind. Reported at compile, at `/kind` of `modules/<id>/<relative path>`. |
| `AXC0203` | A `modules` entry names a module with no installed version. Reported in `application.json` at `/modules/{i}/id`, or in `modules/<id>/module.json` for an entry of a module's own `modules`. |
| `AXC0204` | A `modules` entry has a version range in no valid form. Reported at `/modules/{i}/version`, in `application.json` or in `modules/<id>/module.json`. |
| `AXC0205` | No installed version meets the one range that asks for the module. The message lists the installed versions. Reported at `/modules/{i}/version`, in `application.json` or in `modules/<id>/module.json`. Several ranges that no version meets together are `AXC0295`. |
| `AXC0206` | A module id appears twice in `modules`. Reported at `/modules/{i}/id` of each later entry, in `application.json` or in `modules/<id>/module.json`. |
| `AXC0207` | A field's `target` or `sequence`, a data source's or form's `entity`, a widget's or policy grant's `dataSource`, a site navigation entry's `page`, a trigger's, `startProcess` action's, process inspector's or sub-process step's `process`, an endpoint's `entity` or `process`, or a qualified rule call `<module id>:<name>(…)` names a module that `modules` does not declare. Reported at `/fields/{i}/target`, `/fields/{i}/sequence`, `/entity`, `/widgets/{i}/dataSource`, `/grants/{i}/dataSource`, `/navigation/{i}/page`, `/process`, `{actionPath}/process`, `/widgets/{i}/process`, `/steps/{i}/process` or the expression's pointer. A module that only another module depends on is not declared by the application, so a reference to it is reported too. A reference under a declared module that could not be pinned is not reported again. |
| `AXC0208` | A field's `target` or `sequence` in a module entity, the `entity` of a module event, data source, form or process, the `target` of a module data source parameter, a module process step's `form`, `event`, `process` or `entity`, a module trigger's `process`, `event` or `entity`, or a reference in a module page (a widget's `entity`, `form`, `dataSource` or `process`, the `page` or `process` of an action, or a page parameter's `target`), contains `/` and does not name a module that module declares. A `sequence`, a step `form`, a step `process` and a trigger `process` with `/` always give it. Reported at that property in `modules/<id>/…`. |
| `AXC0209` | A module text key lacks the `<id>.` prefix. Reported at `/texts/{key}` of the module text, and at `/roles/{i}/label/textKey` of `modules/<id>/module.json` for a role slot label, and at `/connectors/{i}/label/textKey` for a connector slot label. |
| `AXC0210` | An application locale has no text from a pinned module. The message names the locale. Reported in `application.json` at `/modules/{i}` for a declared module, and at `/modules` for a module that only another module depends on. |
| `AXC0211` | An application text key starts with the `<id>.` prefix of a module the application uses, declared or only depended on. Reported at `/texts/{key}`. |
| `AXC0212` | An application file sits under `modules/`, which releases reserve. Reported at that file, with an empty path. |
| `AXC0213` | The pinned version drops a module entity, drops one of its fields or changes a field's `type`, compared with the active release. The message names the module and both versions, and `none` when the module is removed. Reported at activation, at `/fields/{i}/type` of the module entity file in the release for a type change and at `/fields` for a dropped field. A dropped entity, or a removed module, is reported at the entity file of the active release, with an empty path. |
| `AXC0214` | A `migration`'s `entity` names a module entity, that is, it contains `/`. Reported at compile, at `/entity`. |
| `AXC0292` | A role slot of the pinned module version has no key in the entry's `roles`. The message names the slot, the module and the version. Reported in `application.json` at `/modules/{i}/roles`, or at `/modules/{i}` when the entry has no `roles`. An entry of a `module.json` is checked the same way for the role slots of the module it names, and reported in `modules/<id>/module.json` of the module that holds it. |
| `AXC0293` | A role slot is mapped to a name that is not a role the application declares. Reported at `/modules/{i}/roles/{slot}`, with `~` written `~0` and `/` written `~1`. A role whose file failed its own schema is not reported again. In an entry of a `module.json` the name must be a role slot of the module that holds the entry, matched exactly, and the report is in that `module.json`. |
| `AXC0294` | A key in the entry's `roles` is not a role slot of the pinned module version. The message lists the slots, or says there are none. Reported at `/modules/{i}/roles/{key}`, escaped the same way. It is reported the same way in `modules/<id>/module.json` for an entry of a module. |
| `AXC0295` | No installed version of a module meets every range that asks for it, and two or more ranges ask. The message names the application or each module that asks, with its range, and lists the installed versions. Reported at `/modules/{i}/version` of the application's own entry when the application asks, and otherwise of the first asking module's `modules/<id>/module.json`. See [resolving the graph](#processes-pages-policies-and-dependencies). |
| `AXC0296` | Modules depend on each other in a cycle, and a module that depends on itself is a cycle. The message names the cycle. Reported once for each cycle, at `/modules/{i}/id` of the entry that closes it, in `modules/<id>/module.json`. |
| `AXC0297` | A connector slot of the pinned module version has no key in the entry's `connectors`. The message names the slot, the module and the version. Reported in `application.json` at `/modules/{i}/connectors`, or at `/modules/{i}` when the entry has no `connectors`. An entry of a `module.json` is checked the same way for the connector slots of the module it names, and reported in `modules/<id>/module.json` of the module that holds it. |
| `AXC0298` | A connector slot is mapped to a name that is not a connector the application declares, ignoring letter case. Reported at `/modules/{i}/connectors/{slot}`, with `~` written `~0` and `/` written `~1`. A connector whose file failed its own schema is not reported again. In an entry of a `module.json` the name must be a connector slot of the module that holds the entry, matched exactly, and the report is in that `module.json`. |
| `AXC0299` | A key in the entry's `connectors` is not a connector slot of the pinned module version. The message lists the slots, or says there are none. Reported at `/modules/{i}/connectors/{key}`, escaped the same way. It is reported the same way in `modules/<id>/module.json` for an entry of a module. |
| `AXC0300` | The connector a slot is mapped to has no operation with the name of an operation of the slot, ignoring letter case. The message names the slot, the connector and the operation. Reported at `/modules/{i}/connectors/{slot}`. In an entry of a `module.json` the slot of the depending module takes the place of the connector, and the report is in that `module.json`. |
| `AXC0301` | An input of a slot operation is not an input of the connector's operation with the same type. The message names the slot, the operation and the field, and both types when the field exists. Reported at `/modules/{i}/connectors/{slot}`. In an entry of a `module.json` the slot of the depending module takes the place of the connector, and the report is in that `module.json`. |
| `AXC0302` | An input of the connector's operation is required, and the slot operation has no input with that name. The message names the slot, the operation and the field. Reported at `/modules/{i}/connectors/{slot}`. In an entry of a `module.json` it is checked against the connector that the chain of slots reaches, and the report is in that `module.json`. |
| `AXC0303` | An output of a slot operation is not an output of the connector's operation with the same type. The message names the slot, the operation and the field, and both types when the field exists. Reported at `/modules/{i}/connectors/{slot}`. In an entry of a `module.json` the slot of the depending module takes the place of the connector, and the report is in that `module.json`. |
| `AXC0304` | A role in a module policy's `roles` is not a role slot of its module. The message lists the slots, or says there are none. Reported at `/roles/{i}` of the module file. |
| `AXC0305` | A grant in a module policy is on something other than an entity or data source of its own module: a `process` or `endpoint` grant, a name with `/` such as `departments/Department`, or the plain name of an application entity or data source. Reported at `/grants/{i}/entity`, `/grants/{i}/dataSource`, `/grants/{i}/process` or `/grants/{i}/endpoint` of the module file. |
| `AXC0306` | Two mappings of the same slot of a module give it different roles or different connectors: the application's own entry and the chain through a depending module, or the chains through two depending modules. The message names the slot, the module and both mappings. The application's entry stays, and otherwise the first depending module in pin order. Reported at `/modules/{i}/roles/{slot}` or `/modules/{i}/connectors/{slot}` of the later entry, in `modules/<id>/module.json`. See [mapping slots](#processes-pages-policies-and-dependencies). |
| `AXC0307` | A module task's `assignee.role`, or an escalation's `reassign.role`, is not a role slot of its module. A role of an application gives this too. The message lists the slots, or says there are none. Reported at `/steps/{i}/assignee/role` or `/steps/{i}/escalations/{j}/reassign/role` of the module file. |

### Processes, pages, policies and dependencies

_(planned for M20)_ A module can ship a working feature with its screens,
permissions, triggers and outside calls, and build on other modules, as
[D89](../decisions/d89-modules-that-hold-processes-pages-and-policies-and-depend-on-other-modules.md)
and [D103](../decisions/d103-modules-hold-triggers-scenarios-and-connector-slots.md) agree. It replaces the two limits above: the kinds a module may hold, and no
dependencies. Dependencies (the `modules` list with the slot maps), resolving the graph, one
copy each, what the application names, and what a module names and calls of the modules it declares are built, and so are module pages. The `department-budgets` sample module and its use by the purchase request sample are built too. The rest of this section is planned.

```json
{
  "kind": "module",
  "id": "department-budgets",
  "version": "1.0.0",
  "label": "Department budgets",
  "roles": [{ "name": "approver", "label": { "textKey": "department-budgets.roles.approver" } }],
  "modules": [{ "id": "departments", "version": "^1.1.0", "roles": { "manager": "approver" } }]
}
```

`department-budgets` is the sample module, `samples/modules/department-budgets`, that the
purchase request sample declares, as
[D94](../decisions/d94-purchase-requests-prove-the-extensibility-milestone.md)
agrees. It holds a `Budget` entity with a name, a `departments/Department` reference, a year, an
amount and a status, a `BudgetReview` form, a `Budgets` data source and page, a `BudgetApproval`
process whose `review` task goes to `approver`, an `ApproverAccess` policy and `en` and `vi` texts.
An application that declares it maps the slot to one of its own roles:

```json
"modules": [
  { "id": "departments", "version": "^1.1.0", "roles": { "manager": "department-manager" } },
  { "id": "department-budgets", "version": "^1.0.0", "roles": { "approver": "department-manager" } }
]
```

- **Kinds.** A module may also hold `process` _(built)_, `trigger` of type `event`,
  `dataChange` or `schedule` _(built)_ and _(planned for M24)_ `scenario`, as
  [D103](../decisions/d103-modules-hold-triggers-scenarios-and-connector-slots.md) agrees. It still holds no `role`, `site`, `identityProvider`,
  `connector`, `endpoint` or `webhook`.
- **Role slots.** `roles` in `module.json` lists the module's role slots, each
  with a `name`, written like a role name, and a `label`. Slot names are unique
  in the module, ignoring letter case, or the version fails at host start with
  `AXC0200` at `/roles/{i}/name`. A label's text key starts with `<module id>.`,
  or `AXC0209` is reported at `/roles/{i}/label/textKey` of `module.json`.
  Declaring slots is built. A module policy names a slot, and anything else is
  `AXC0304`. The assignees of module tasks name a slot too, in a task step's
  `assignee.role` and an escalation's `reassign.role`. Naming anything else, an
  application role included, is `AXC0307`. A module task can still name a user with `assignee.user`.
- **Mapping slots.** Each `modules` entry of an application has `roles`, which
  maps every slot of that module to a role the application declares. A slot
  left unmapped is `AXC0292`, a slot mapped to an undeclared role is `AXC0293`
  and a key that is no slot of the pinned version is `AXC0294`. Slot and role
  names are matched exactly. The compiled model keeps the map of each pinned
  module. After the mapping, a user who holds the role in the application
  holds the slot, for the module policies. A module that the application
  reaches only as a dependency gets its map through the chain, see
  [dependencies](#processes-pages-policies-and-dependencies). A module task
  that names a slot goes to the people who hold the mapped role in that application.
- **Module policies.** _(built)_ A module policy is a `policy` file in the module.
  In an application it is named `<module id>/<name>`, and its id is derived from
  the application `id` as for other module resources. Its `roles` name role
  slots of its own module, matched exactly, and anything else, a role included,
  is `AXC0304`. Each slot becomes the role the application maps it to, so the
  compiled policy names that role and a person who holds the role in the
  application gets the grants there, and in no other application. A slot the
  application does not map names nobody. A grant names an entity or data source of
  its own module by its plain name. A name with `/`, including a module it
  declares, a `process` or `endpoint` grant, and the plain name of an application
  entity or data source are `AXC0305`. An unknown plain name keeps `AXC0092`. A grant's `filter` calls
  only rules of the modules its module declares, by qualified call, as a module
  data source filter does. Default deny still holds: without the mapped role a
  person gets `403` on the module entity and data source. A module that the
  application reaches only as a dependency gets its slot map through the chain, so its policies
  grant the role the chain reaches. _(planned for M20)_ A module policy grants on the processes and
  endpoints of its own module.
- **Pages.** _(built)_ A module page is a `page` file in the module. In an application it is
  named `<module id>/<name>`, and its id is derived from the application `id` as for other module
  resources. Its widgets name resources of its own module by their plain names: the `entity`,
  `form`, `dataSource` and `process` of a widget, the `page` of a `navigate` action in the widget's
  `actions`, `rowActions` and `toolbarActions` or in a kpi's `navigate`, and the `target` of a page
  parameter. The compiler rewrites each to the qualified name. A name with `/`, a module the
  module declares included, is `AXC0208` at that property of the module file. A `process` names
  a process of the same module. A name the module lacks, such as a data source of the
  application, gets the usual unknown-name diagnostic in the module file, and not in the
  application's files. Text keys in the page resolve against the merged texts, as in module forms.
  A module holds no site, so the application's site navigation lists a module page by that name,
  such as `{ "page": "departments/Departments" }`, for a module that `application.json` declares.
  A module that the application does not declare is `AXC0207` at `/navigation/{i}/page`. An
  application page cannot navigate to a module page yet, because the `page` of its `navigate`
  action is a plain name. The page metadata API writes the name as two path segments, as the record
  API writes a module entity: `GET /api/sites/{site path}/pages/{module id}/{name}`. In the browser a
  module page opens at `/{site}/{module id}/{name}`, and its form pages one segment deeper, at
  `/{site}/{module id}/{name}/new` and `/{site}/{module id}/{name}/{id}`. A second address segment
  that is no record id names a module page. A module page named `New` cannot be opened, because
  `/{site}/{module id}/new` is the address of a new record on the application page named like the
  module. Who may read the records is up to the grants of the application's policies.
- **Processes.** _(built for compilation and running)_ A module process is a `process` file in the module. In
  an application it is named `<module id>/<name>`, and its id is derived from the application `id`
  as for other module resources. It works on an entity of its own module, and its steps name
  resources of its own module by plain name: the `entity` of the process and of a `createRecord`
  step, the `form` of a task, the `event` of a step and the `process` of a sub-process step. The
  compiler rewrites each to the qualified name. A name with `/` is `AXC0208` in the module file, but
  an `entity` or an `event` may name a module that the module declares. A task `form` and a
  sub-process `process` never name another module. The process sees no application form, process,
  page, role, connector or rule, and naming one gives the usual unknown-name code in the module
  file. Its expressions see the rules of its own module by plain name. A package operation step
  calls an operation of a package in the module's `packages`, which `module.json` lists like an
  application's. The step is checked against the pinned version, and a package the module does not
  list gives `AXC0150` in the module file. The pins follow the
  [pinning rule](extension-packages.md#declaring-packages-in-an-application) of the release. A task
  assignee names a role slot of the module, which the compiled task replaces with the role the
  application maps it to, see Role slots above. The `callConnector` steps that name
  a connector slot of the module, see Connector slots below. The application starts it from a
  trigger, a `startProcess` action, a sub-process step or an endpoint, by the name
  `<module id>/<name>`. It is pinned with the rest of the release. The worker runs a module process
  instance on the application's own copy of its module entity when a trigger or a sub-process step
  starts it or an endpoint creates the module record and starts it, and a waiting instance finishes
  on the release it was started on, even after a newer module version is activated. Starting one
  through the start endpoint is not proven yet. A module task assigned to a slot is listed in the task
  inbox of the people who hold the mapped role, and only they can decide it.
- **Triggers.** A module trigger starts a process of its own module. It
  reacts to an event or an entity of its own module, or of a module it
  declares. A trigger of type `event` or `dataChange`, delete triggers included,
  is built. Its `process` names a process of its own module by plain name, and a
  name with `/` is `AXC0208`. An unknown name, such as an application process,
  is `AXC0129`. Its `event` or `entity` names its own module's resource by plain
  name, or a declared module's resource as `<module id>/<name>`, and anything
  else is `AXC0208`. An unknown name keeps `AXC0130` or `AXC0143`. Each is
  reported in the module file. In each application the trigger is named
  `<module id>/<name>` and has an id derived from the application, like the
  module's other resources. A create, update or delete of the application's
  copy of the module's entity, or an event raised in that application, starts
  the module's process in that application only, and its receipts and dead
  letters belong to that application. A trigger of type `schedule` is built
  too. It has no `timeZone`, and writing one is `AXC0004` at `/timeZone`. It
  runs in the time zone of each application that uses the module, so two
  applications fire the same schedule at their own local times. Its
  `dataSource` names a data source of its own module by plain name, or is left
  out for a scheduled job. An application data source is unknown, `AXC0162`
  in the module file. Each application has its own schedule state and its own
  `trigger.schedule` work item, keyed by the trigger's id for that application.
  Occurrences, catch-up after downtime and the fan-out to records stay inside
  that application, so one application's occurrence never fires or catches up
  another's. When an application changes its time zone, its next activation
  recomputes the next occurrence of its module schedules, as it does for a
  changed `timeZone` of an application schedule.
- **Connector slots.** `connectors` in `module.json` lists the module's
  connector slots. Each has a `name`, a `label` and `operations`, each with a
  `name` and typed `input` and `output` fields in the shape of a
  [connector operation](integration-resources.md#connector-resource). Each
  `modules` entry of an application has `connectors`, which maps every slot to
  a connector the application declares, as
  `"connectors": { "orders": "PurchasingSystem" }`. The connector must have
  each operation of the slot by name, every input of the slot with the same
  type, no required input the slot lacks, and every output of the slot with
  the same type. Declaring and mapping connector slots, with these checks, is built.
  Slot names are unique in the module, and so are the operation names of a
  slot and the field names of an input or output, all ignoring letter case, or
  the version fails at host start with `AXC0200`. A label is a `textKey` that
  starts with `<module id>.`, or `AXC0209` is reported at
  `/connectors/{i}/label/textKey`. An unmapped slot is `AXC0297`, at
  `/modules/{i}/connectors` of `application.json`, or at `/modules/{i}` when the
  entry has no `connectors`. A slot mapped to an undeclared connector is
  `AXC0298` and a key that is no slot is `AXC0299`, both at
  `/modules/{i}/connectors/{slot}`. A missing operation is `AXC0300`, an input
  of another type or one the operation lacks is `AXC0301`, a required operation
  input the slot lacks is `AXC0302` and an output of another type or one the
  operation lacks is `AXC0303`, each at the same pointer and with a message
  that names the slot, the operation and the field. Slot keys are matched
  exactly. Connector, operation and field names are matched ignoring letter
  case, and a field's `required` is not compared for the slot's own fields. The
  compiled model keeps the map from slot to the connector's declared name.
  The application's connector keeps its base URL, auth, bindings, timeout,
  idempotency header and lookup. A depending module maps the connector slots
  of a module it depends on to its own slots, see
  [dependencies](#processes-pages-policies-and-dependencies). A module process's
  `callConnector` step is built: its `connector` names a slot of the module,
  matched exactly, and one of the slot's operations, ignoring letter case, as in
  `"connector": { "name": "orders", "operation": "createOrder" }`. Its `input`
  and `output` are checked against the slot operation's fields, not against
  any one application's connector, with the codes `AXC0104` to `AXC0108` of
  an application step, reported in the module's process file. A name that is
  no slot of the module, which includes an application connector's name, is
  `AXC0102` at `/steps/{i}/connector/name` of the module file. An operation
  the slot lacks is `AXC0103` at `/steps/{i}/connector/operation`. The inputs a
  step must give are those the slot marks `required`. The step adds no
  reference, since a slot is no resource. In each application the compiled
  step names the connector the slot maps to and its operation, so the call
  keeps that connector's base URL, auth, bindings, timeout, idempotency
  header and lookup, and the worker runs it like an application step. Two
  applications that map one slot to different connectors call different
  systems. A module never names a binding.
- **Scenarios** _(planned for M24)_. A module scenario gives its users role
  slots instead of roles. When it runs for an application, each slot becomes
  the role it maps to, and the scenario runs against the application as
  customized.
- **Dependencies.** _(built)_ `module.json` has an optional `modules`, with the `id` and
  `version` entries of an application's `modules`. A bad entry is `AXC0203`,
  `AXC0204` or `AXC0206`, reported in the `module.json` of the module that
  holds it. A module names resources and calls rules only of the modules it
  declares, as `<module id>/<name>` and `<module id>:<rule>(…)`, see
  [references](#modules). Its entry for a module it depends on maps that module's
  slots to its own slots, with `roles` and `connectors` as an application's entry
  has. Each key is a slot of the module it depends on and each value is a slot of
  the module that holds the entry, all matched exactly. Following the chain gives
  each slot of the dependency the application role or connector that the slot of
  the depending module reaches, and the compiled model keeps that map for the
  pin. A slot the entry leaves unmapped is `AXC0292` or `AXC0297`, a value that
  is no slot of the depending module is `AXC0293` or `AXC0298`, and a key that is
  no slot of the dependency is `AXC0294` or `AXC0299`, each reported in the
  `module.json` that holds the entry, at `/modules/{i}/roles` or
  `/modules/{i}/connectors`, and at the key for the last two. A connector slot is
  checked against the slot it maps to as it is against a connector: a missing
  operation is `AXC0300`, and an input or output of another type, or one the
  slot lacks, is `AXC0301` or `AXC0303`, with a message that names the slot, the
  operation and the field. A required input is `AXC0302`, checked against the
  connector that the chain reaches, because a slot's own `required` is not
  compared. A slot that the chain leaves without a role or connector, because
  the application leaves a slot of the depending module unmapped, gets nothing
  and no report beyond that of the break. When the application declares a
  module and also reaches it through a depending module, or two depending
  modules map the same slot, the mappings must give the same role or connector.
  A difference is `AXC0306`, which names both mappings and is reported at the
  later entry. The application's entry stays in the model, and otherwise the
  first depending module in pin order.
- **Resolving the graph.** _(built)_ The application resolves every module it declares
  and every module they declare, flat. Each module id gets one version: the
  highest installed version that meets every range that asks for it.
  When one range asks and no installed version meets it, the diagnostic is
  `AXC0205`. When two or more ranges ask and no version meets them all, it is
  `AXC0295`, which names the application or each module that asks, with its
  range. A cycle between modules, and a module that depends on itself, is
  `AXC0296`, which names the cycle. The resolution picks again until nothing
  changes, without backtracking. A graph whose picks never settle is reported as a
  conflict.
- **One copy each.** _(built)_ Each module gets one copy per application, with ids
  derived from the application. A module that two others depend on is pinned
  and copied once. Rebuilding a stored release resolves the same graph from the
  module copies it holds.
- **What the application names.** _(built)_ The application names resources only of the
  modules it declares itself, not of the modules they depend on. A name under
  a module that is only a dependency is `AXC0207`, as for an undeclared module,
  in the places `AXC0207` lists. A process, event, trigger, seed, migration or policy
  `entity` still resolves to a dependency's resources. A name inside a
  dependency-only module's own resources that does not resolve keeps its
  unknown-name code.
- **Studio.** The policy matrix lists a module policy's grants in the row of the
  role its slot maps to, read-only and tagged with the module, without an Open policy
  link. Studio shows a module's processes, policies and pages read-only _(built)_, as it shows
  module entities: a process as its graph, a policy as a table of its grants and a page
  drawn on the page canvas. Studio never edits a module.
- **Upgrades.** A module upgrade must still be additive for its entities
  (`AXC0213`). Adding a process, page or policy is additive, and a version that adds a process
  file is accepted.

## Application templates

This section follows
[D51](../decisions/d51-creating-applications-and-resources-from-studio.md).
The manifest, the one-site rule, installation, listing, the sample and the copy are built.
An application template is an application folder with a `template.json`
manifest. Studio creates a new application by copying an installed template, see
[creating an application](authoring.md#creating-an-application).

The manifest is `template.json` at the root of the folder:

```json
{
  "kind": "applicationTemplate",
  "id": "request-approval",
  "version": "1.0.0",
  "title": { "en": "Request approval", "vi": "Phê duyệt yêu cầu" },
  "description": {
    "en": "A request form, a list and a two-step approval.",
    "vi": "Một biểu mẫu yêu cầu, một danh sách và phê duyệt hai bước."
  }
}
```

- **Manifest.** `template.json` has `kind: applicationTemplate`, an `id`
  (lower-case letters, digits and `-`, starting with a letter), a SemVer
  `version`, a `title` and a `description`. The title and the description each
  have an `en` and a `vi` text.
- **One site.** A template holds exactly one site. The site path of a copy
  follows the new name, and that rule is only clear for a single site.
- **Installation.** Templates are installed per installation under
  `Templates:Path/<id>/<version>/`. The environment variable is
  `Templates__Path`, and relative paths are resolved against the server content
  root. The server reads them. Without the setting no
  template is installed. A setting that names a missing folder stops the
  server. A template version with a bad manifest, with folder names that differ
  from its manifest, or without exactly one site is left out and logged as a
  warning at start, and the other versions still load. The warning names the
  version's folder, as `request-approval/1.0.0`, and has no `AXC` code.
- **Listing.** `GET /api/studio/templates` lists the newest version of each
  template, with the diagnostics of compiling it in memory. A template that does
  not compile is still listed.
- **Copy**. A copy takes every `*.json` file of the template except
  `template.json`, in the same folders.
  - The top-level `id` of each resource becomes a new UUID. Seed record ids stay,
    because they are row ids in the new application's own tables and cannot
    clash.
  - The manifest gets the new `name`, and the site's `path` becomes the
    kebab-case name.
  - References are by name, so they still resolve.
  - The copy has the template's diagnostics. `template.json` is not copied,
    because template metadata does not belong in an application.
- **Sample.** `samples/templates/request-approval` is the sample template. It
  holds one entity with a sequence number, a form page, a table page, a two-step
  approval process, two roles and their policies, an `accounts` identity provider, texts in `en` and `vi`, and one
  test scenario. `scripts/build.sh` installs it into `artifacts/templates`. The
  production images hold no template, and the Compose image holds the samples in
  `/templates`.

### Blank application

A blank application is the smallest folder that compiles with no error. A site
needs at least one navigation entry that names a page, so a blank application
also holds one `Home` page with a `taskInbox` widget. That page names no entity,
form or data source. For the name `PurchaseRequests`, it holds:

- `application.json`: a new UUID `id`, `kind: application`, the `name` and
  `formatVersion: 1`.
- `sites/<kebab-name>.json`: the site, with the `name` of the application, the
  `path` set to the kebab-case name, the title key `site.title`, the locales
  `{ "default": "en", "fallback": "en", "available": ["en"] }` and one navigation
  entry to `Home` with the label key `nav.home`.
- `identity-providers/accounts.json`: a new UUID `id`, `kind: identityProvider`,
  `name: accounts`, `formatVersion: 1` and `type: accounts`. An application with a
  site needs at least one identity provider, and the site allows it because it
  has no `signIn`.
- `pages/home.json`: the page `Home` with the title key `pages.home.title` and
  `widgets` of one `taskInbox`.
- `texts/en.json`: the `en` text resource, with three keys: `site.title`, which
  equals the application `name`, `pages.home.title` and `nav.home`.

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

A `name` whose kebab-case form is longer than 60 characters, or is a reserved
site path (`api`, `health`, `assets`, `studio`, `account`, `users`), is refused with `400`, so a
blank application cannot fail with `AXC0024` or a schema error.
