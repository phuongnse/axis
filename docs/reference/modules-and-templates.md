# Modules and application templates

Detailed reference for reusable modules, which an application declares and builds on, and for
the application templates that Studio creates new applications from.
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

## Modules

This section follows
[D40](../decisions/d40-reusable-modules.md). The manifest,
installation, declaring, pinning and the diagnostics `AXC0200` to `AXC0214`
are built. A pinned module's files are stored in the release and
checked as JSON, for an allowed kind and against the schema of their kind. A
pinned module's entities, sequences, seeds, events, data sources and forms join the application
model, each with an id derived from the application `id` and the name
`<module id>/<name>`. A field's `target` and `sequence`, a data source's and a
form's `entity` and a policy grant's `entity` can name them. A field's
`target` also covers data source parameters and endpoint inputs. A process
`entity`, a raise event step's `event` and an event trigger's `event` can name
a module entity or event. A table widget's `dataSource` and a policy grant's
`dataSource` can name a module data source. A pinned module's texts join the application's texts
of the same locale, so its labels resolve. A pinned module's rules are checked
with the rule checks, inside their module, and application expressions call them,
see [References](#modules). Activation provisions the module's entities as the
application's own tables, startup seeding applies the module's seeds, the record
API serves module entities, and the data source endpoint serves module data sources.
A module is a
versioned folder of resources that applications reuse. An application declares
the modules it uses in `application.json`, with a version range for each:

```json
{
  "id": "219102bc-864f-42ed-874a-c19eaee0d1ba",
  "kind": "application",
  "name": "PurchaseRequests",
  "formatVersion": 1,
  "modules": [{ "id": "departments", "version": "^1.0.0" }]
}
```

The module's manifest is `module.json` at the root of its folder:

```json
{
  "kind": "module",
  "id": "departments",
  "version": "1.0.0",
  "label": "Departments"
}
```

`departments` is the sample module, `samples/modules/departments`. It holds a
`Department` entity with a code, a name and a manager, a `Departments` data source,
an `IsManager(manager, user)` rule and an `IsDepartmentCode(code)` rule, three seed
departments and `en` and `vi` texts. The `Department` entity has a validation that
calls `IsDepartmentCode`, so a department code must be in capital letters.
`scripts/build.sh`, `scripts/dev.sh` and the E2E run install it under
`artifacts/modules/departments/1.0.0/`, and the Compose images hold it in
`/modules`. The server and the worker read it through `Modules:Path`.

- **Manifest.** `module.json` has `kind: module`, an `id` (lower-case letters,
  digits and `-`, starting with a letter), a SemVer `version`, a `label` and an
  optional `packages`. `packages` has the same shape as in `application.json`,
  see [Extension packages](extension-packages.md#declaring-packages-in-an-application). A module has no dependencies
  on other modules. _(planned for M20)_ It gets `roles` and `modules`, see
  [Processes, pages, policies and dependencies](#processes-pages-policies-and-dependencies).
- **Kinds a module may hold.** `entity`, `dataSource`, `rule`, `sequence`,
  `form`, `event`, `text` and `seed`. Processes, pages, roles and policies are
  not allowed. _(planned for M20)_ `process`, `page` and `policy` are allowed
  too. Roles stay out.
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
  It copies that version's files into the release as `modules/<id>/…`, so the
  content hash covers them. The pinned version is the one in the copied
  `modules/<id>/module.json`. There is no extra release table. A release keeps
  working when the version is later removed from the installation, because the
  release holds the files. A newer installed version in range gives a new
  release at the next activation, as for packages.
- **References.** An application resource names a module resource as
  `<module id>/<name>`, for example `departments/Department`. A module resource
  names only resources of its own module, written without a `/`. The compiler
  builds both rules for a field's `target` and `sequence` and for a data
  source's and a form's `entity`, and for a data source parameter's `target`. A
  module event's `entity` follows the same rule, and an application process
  `entity`, raise event step `event` and event trigger `event` can name module
  resources. A table widget's `dataSource` and a policy grant's `dataSource` can
  name a module data source. A name under a module that `modules` does not
  declare is `AXC0207`, reported at `/entity` for a data source or a form, and at
  `/widgets/{i}/dataSource` or `/grants/{i}/dataSource` for a widget or a grant.
  A step or trigger that names an event under an undeclared module keeps its
  unknown-event code. A module field whose `target` or `sequence` contains `/`,
  a module event, data source or form whose `entity` does, or a module data
  source parameter whose `target` does, is `AXC0208`.
  An unknown name inside a declared module keeps its existing code, such as
  `AXC0012`, `AXC0042`, `AXC0066` or `AXC0074`. A policy grant `entity` can
  name a module entity. One that names an undeclared module keeps the policy's
  unknown-entity code, `AXC0092`. A module rule calls only rules of its own
  module, by plain name, such as `isActive(x)`. The rule checks, `AXC0055`,
  `AXC0056`, `AXC0057` and `AXC0065`, and the type check of its expression
  apply to it and are reported in the module file. A call to an application
  rule, or to a rule of another module, is `AXC0050` at `/expression` of the
  module file. The compiler keeps each module rule as `<module id>/<name>`.
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
  entity's validations call rules of their own module by plain name, as a
  module rule does. A call to an application rule, or a qualified call, even
  with the module's own id, is `AXC0050` at `/validations/{i}/expression` of the
  module file. The record API runs the module rule's body in each application
  that declares the module. These calls add no rule-call edge to the reference
  index, and the 8-deep limit counts the rule chain only. A module data source
  filter sees no rules. Module entities come before the application's own entities
  in the model, in the order of `modules` and then by path. The record API
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
  text for that locale, or `AXC0210` is reported. A module text in a locale the
  application does not have is ignored, without a diagnostic. `AXC0026` and
  `AXC0027` check each module's texts against each other, apart from the
  application's texts. A module key joins the `AXC0028` check only in the
  locales the application has. An application text key may not start with the
  `<id>.` prefix of a declared module, or `AXC0211` is reported.
- **Seeds.** The startup seeding step applies a module's seeds for each
  application that uses the module, into that application's table. Each
  application keeps the record ids written in the module file, so its own
  seeds can reference a module seed record by that id.
- **Diagnostics.** A diagnostic in a module file has `file` set to
  `modules/<id>/<relative path>` and `resourceId` set to the id declared in the
  file.

| Built code | Meaning |
| --- | --- |
| `AXC0200` | `module.json` is missing, unreadable, not JSON or fails its schema, or another file of the module version cannot be read. Reported at host start, at the failing pointer. A copy of a module in a stored release without a valid `modules/<id>/module.json` is reported the same way, when the release is rebuilt. |
| `AXC0201` | The folder names differ from the manifest `id` or `version`. Reported at host start, at `/id` or `/version`. In a stored release, the folder `modules/<id>/` differing from the manifest `id` is reported the same way. |
| `AXC0202` | A module file has a kind that a module may not hold, or no kind. Reported at compile, at `/kind` of `modules/<id>/<relative path>`. |
| `AXC0203` | A `modules` entry names a module with no installed version. Reported in `application.json` at `/modules/{i}/id`. |
| `AXC0204` | A `modules` entry has a version range in no valid form. Reported at `/modules/{i}/version`. |
| `AXC0205` | No installed version meets the range. The message lists the installed versions. Reported at `/modules/{i}/version`. |
| `AXC0206` | A module id appears twice in `modules`. Reported at `/modules/{i}/id` of each later entry. |
| `AXC0207` | A field's `target` or `sequence`, a data source's or form's `entity`, a widget's or policy grant's `dataSource`, or a qualified rule call `<module id>:<name>(…)` names a module that `modules` does not declare. Reported at `/fields/{i}/target`, `/fields/{i}/sequence`, `/entity`, `/widgets/{i}/dataSource`, `/grants/{i}/dataSource` or the expression's pointer. A reference under a declared module that could not be pinned is not reported again. |
| `AXC0208` | A field's `target` or `sequence` in a module entity, the `entity` of a module event, data source or form, or the `target` of a module data source parameter, contains `/`. Reported at that property in `modules/<id>/…`. |
| `AXC0209` | A module text key lacks the `<id>.` prefix. Reported at `/texts/{key}` of the module text. |
| `AXC0210` | An application locale has no text from a declared module. The message names the locale. Reported in `application.json` at `/modules/{i}`. |
| `AXC0211` | An application text key starts with the `<id>.` prefix of a declared module. Reported at `/texts/{key}`. |
| `AXC0212` | An application file sits under `modules/`, which releases reserve. Reported at that file, with an empty path. |
| `AXC0213` | The pinned version drops a module entity, drops one of its fields or changes a field's `type`, compared with the active release. The message names the module and both versions, and `none` when the module is removed. Reported at activation, at `/fields/{i}/type` of the module entity file in the release for a type change and at `/fields` for a dropped field. A dropped entity, or a removed module, is reported at the entity file of the active release, with an empty path. |
| `AXC0214` | A `migration`'s `entity` names a module entity, that is, it contains `/`. Reported at compile, at `/entity`. |

### Processes, pages, policies and dependencies

_(planned for M20)_ A module can ship a working feature with its screens and
permissions, and build on other modules, as
[D89](../decisions/d89-modules-that-hold-processes-pages-and-policies-and-depend-on-other-modules.md)
agrees. It replaces the two limits above: the kinds a module may hold, and no
dependencies.

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

`department-budgets` is the sample module that the purchase request sample
declares, as
[D94](../decisions/d94-purchase-requests-prove-the-extensibility-milestone.md)
agrees. An application that declares it maps the slot to one of its own roles:

```json
"modules": [
  { "id": "departments", "version": "^1.1.0", "roles": { "manager": "Manager" } },
  { "id": "department-budgets", "version": "^1.0.0", "roles": { "approver": "Manager" } }
]
```

- **Kinds.** A module may also hold `process`, `page` and `policy`. It still
  holds no `role`, `site`, `connector`, `endpoint`, `trigger` or `webhook`.
- **Role slots.** `roles` in `module.json` lists the module's role slots, each
  with a `name` and a `label`. Module policies and the assignees of module
  tasks name a slot. Naming anything else is a new diagnostic.
- **Mapping slots.** Each `modules` entry of an application has `roles`, which
  maps every slot of that module to a role the application declares. A slot
  left unmapped, or mapped to an undeclared role, is a new diagnostic. After
  the mapping, a user who holds the role holds the slot.
- **Module policies.** A module policy grants only on resources of its own
  module.
- **Pages.** A module page is named `<module id>/<name>`. Its widgets name
  resources of its own module. A module holds no site, so the application's
  site navigation lists a module page by that name, such as
  `{ "page": "departments/Departments" }`.
- **Processes.** A module process works on an entity of its own module. Its
  steps name resources of its own module, and call operations of the module's
  `packages`. It has no `callConnector` step. The application starts it from
  a trigger, a `startProcess` action or a sub-process step, by the name
  `<module id>/<name>`. It is pinned with the rest of the release.
- **Dependencies.** `module.json` gets `modules`, with the shape of an
  application's `modules` entries. A module names resources and calls rules
  only of the modules it declares, as `<module id>/<name>` and
  `<module id>:<rule>(…)`. Its entry for a module it depends on maps that
  module's slots to its own slots.
- **Resolving the graph.** The application resolves every module it declares
  and every module they declare, flat. Each module id gets one version: the
  highest installed version that meets every range that asks for it. No such
  version is a new diagnostic that names every module that asks. A cycle
  between modules is a new diagnostic.
- **One copy each.** Each module gets one copy per application, with ids
  derived from the application. A module that two others depend on is copied
  once.
- **What the application names.** The application names resources only of the
  modules it declares itself, not of the modules they depend on.
- **Studio.** Studio shows a module's processes, pages and policies read-only,
  as it shows module entities, and never edits a module.
- **Upgrades.** A module upgrade must still be additive for its entities
  (`AXC0213`). Adding a process, page or policy is additive.

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
site path (`api`, `health`, `assets`, `studio`, `account`), is refused with `400`, so a
blank application cannot fail with `AXC0024` or a schema error.
