# Resource files

Detailed reference for the shape of resource files: the common file shape, the JSON Schema
keywords, entity field types and constraints, entity logic and sequences. How the pipeline loads
and checks them is in [configuration.md](configuration.md).
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

## Resource file shape

```json
{
  "id": "4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1a01",
  "kind": "entity",
  "name": "PurchaseRequest",
  "formatVersion": 1,
  "label": { "textKey": "purchaseRequest.label" },
  "displayField": "title",
  "fields": [
    { "name": "title", "type": "text", "required": true, "maxLength": 200 },
    { "name": "department", "type": "reference", "target": "Department", "required": true },
    { "name": "total", "type": "decimal", "precision": 18, "scale": 2 }
  ]
}
```

- `id` is permanent.
- `name` is the stable technical name used in references and storage. It
  starts with a letter, contains only ASCII letters and digits, and is at most
  60 characters long (`AXC0004`). The same rule applies to field names.
- Labels always come from text resources.
- A field may have `help`, such as
  `{ "name": "title", "type": "text", "help": { "textKey": "purchaseRequest.title.help" } }`:
  a text resource like `label`, on any type, which a form shows between the
  label and the input _(planned)_. It is one or two short lines. Its key joins
  the `AXC0028` check. Studio's [entity designer](entity-designer.md) edits it.
  Page metadata gives it as `helpKey`. See the [form widget](form-widget.md).
- `displayField` names the required `text` field that gives a record its
  name, for example in lookups. An entity that is the target of a reference
  must have one.

A `text` resource holds the texts of one locale, as a map from text key to
text:

```json
{
  "id": "8c3a6b4d-5e6f-4a7b-8c9d-0e1f2a3b4c04",
  "kind": "text",
  "name": "TextsEn",
  "formatVersion": 1,
  "locale": "en",
  "texts": {
    "purchaseRequest.label": "Purchase request"
  }
}
```

- `locale` is a language tag such as `en`, `vi` or `pt-BR`: two or three
  letters, then any number of `-` parts of two to eight letters or digits
  (`AXC0004`). Locales are compared ignoring letter case.
- Each locale has one `text` resource, and every locale has the same keys.
- Texts are never shared across applications.

A `site` is an entry point of the application. A `page` is a route in a
site, and its widgets are its content. A site's `signIn` lists the identity
providers it allows, see
[sites and sign-in](identity-provider.md#sites-and-sign-in):

```json
{
  "id": "c5e8d2a1-7b3f-4c9e-9d1a-2f3b4c5d6e06",
  "kind": "site",
  "name": "Purchasing",
  "formatVersion": 1,
  "path": "purchasing",
  "title": { "textKey": "purchasing.title" },
  "locales": { "default": "en", "fallback": "en", "available": ["en", "vi"] },
  "signIn": ["accounts"],
  "navigation": [{ "page": "PurchaseRequests", "label": { "textKey": "purchasing.nav.requests" } }]
}
```

```json
{
  "id": "d6f9e3b2-8c4a-4d0f-8e2b-3a4c5d6e7f07",
  "kind": "page",
  "name": "PurchaseRequests",
  "formatVersion": 1,
  "title": { "textKey": "pages.requests.title" },
  "widgets": [
    {
      "type": "table",
      "entity": "PurchaseRequest",
      "rowActions": [{ "type": "navigate", "page": "PurchaseRequestForm", "label": { "textKey": "actions.open" }, "record": "id" }],
      "toolbarActions": [{ "type": "navigate", "page": "PurchaseRequestForm", "label": { "textKey": "actions.new" } }]
    }
  ]
}
```

- A page holds one or more widgets. A leaf widget `type` is `table`, `form`,
  `taskInbox`, `processInspector`, `kpi` or `detail`. A `form` names a `form` resource, which lays out the fields of
  one entity in sections (see [form-widget.md](form-widget.md)), or an `entity`, as
  shorthand for all its fields in one section. A `table` names an `entity`, as
  shorthand for all records of that entity, or a `dataSource`. A `kpi` names a
  `dataSource` with one total row, one of its `measure`s and a `label`, and
  may add a `comparison` with a second `measure` and a `label`, and a `navigate`
  action, see
  [detail-chart-kpi-widgets.md](detail-chart-kpi-widgets.md). A `detail` names
  an `entity` or a `form`, as a form does, and a `record`, the required
  `reference` page parameter that picks the record it shows. It may list
  `actions`. A `chart` names a `dataSource` grouped by one field, its `kind`
  (`bar`, `line` or `pie`), the `category` that is that field, one or more
  `measures` with a `label` each, and a `title`, see
  [detail-chart-kpi-widgets.md](detail-chart-kpi-widgets.md).
- A `table` widget opens and creates records through `navigate` actions, see
  below. It has no link to a form page of its own.
- A `form` or `detail` widget may list `actions`. Each is
  `{ "type": "startProcess", "process": "PurchaseRequestApproval", "label": { "textKey": "purchaseRequest.submit" } }`,
  a button that starts the process for the form's record. See
  [frontend.md](frontend.md).
- A `table` widget may list `rowActions` and `toolbarActions`. For now each
  entry is a `navigate` action or, in `rowActions`, a `startProcess` or `delete`
  action. The compiler checks them and the SPA shows each as a button. The first
  `navigate` row action is the link of the identifying column instead
  _(planned)_, see [frontend](frontend.md). A `delete`
  asks for confirmation, then deletes the row's record. An entry has a `type`, such as
  `{ "type": "startProcess", "process": "PurchaseRequestApproval", "label": { "textKey": "purchaseRequest.submit" } }` or
  `{ "type": "delete", "label": { "textKey": "purchaseRequest.delete" } }`.
  `navigate` may sit in either list. `startProcess` and `delete` sit in
  `rowActions` only, and the row is their subject. A `navigate`,
  `startProcess` or `delete` entry in `rowActions` may have `visibleWhen`, a boolean
  expression over the row, such as `"visibleWhen": "status == 'open'"`. The
  schema refuses it in `toolbarActions`, in a form's `actions` and on a kpi's
  `navigate`. It refuses a `delete` with a `process`, `page`, `parameters` or
  `record`, and a `delete` in `toolbarActions` or a form's `actions`. See [frontend.md](frontend.md) and
  [D59](../decisions/d59-table-row-and-toolbar-actions.md).
- A `table` widget may name a `dataSource` instead of an `entity`, such as
  `{ "type": "table", "dataSource": "PurchaseRequestList" }`.
  Its columns are the data source's projected fields. See
  [widget binding](data-sources.md#widget-binding).
- A page may declare typed `parameters`. Each has a `name`, a `type` (text,
  integer, decimal, boolean, date, date-time, enum or reference) and
  `required`, which defaults to false. An `enum` parameter also lists its
  `values` and a `reference` parameter names its `target` entity. The type is
  checked by the same rules as an entity field, so a bad type, missing
  `values` or `target`, or an unknown `target` gives `AXC0004`, `AXC0014`,
  `AXC0012`, `AXC0040` or `AXC0030`. A name that an earlier parameter already
  uses, or that is `page`, `pageSize`, `sort` or `search`, ignoring letter case, is
  `AXC0231`. The compiled page lists its parameters in file order. A value
  lives in the URL query under the parameter name, and a table can bind it to a
  data source parameter. See
  [D63](../decisions/d63-navigate-actions-and-page-parameters.md).

  ```json
  {
    "id": "a1c2e3f4-5b6d-4789-8abc-0d1e2f3a4b5c",
    "kind": "page",
    "name": "TicketDetail",
    "formatVersion": 1,
    "title": { "textKey": "pages.ticketDetail.title" },
    "parameters": [{ "name": "ticket", "type": "reference", "target": "Ticket", "required": true }],
    "widgets": []
  }
  ```

- A `navigate` action opens another page. The compiler checks its shape, and
  the SPA shows its button. It sits in a
  table's `rowActions` or `toolbarActions` ([D59](../decisions/d59-table-row-and-toolbar-actions.md)),
  or in a form's `actions`:
  `{ "type": "navigate", "page": "TicketDetail", "label": { "textKey": "tickets.open" }, "parameters": { "ticket": "id" } }`.
  Each value is an expression, see
  [expressions](expressions.md#names-and-references). In `toolbarActions` the
  expressions see no names, because a toolbar has no row. The checks are in
  [configuration](configuration.md). A page whose widget is a form
  declares no parameters, otherwise it is `AXC0236`. An action to it may set
  `record`, a reference to the form's entity, instead of `parameters`, and the
  compiler checks it (`AXC0235`). The button opens `/{page}/{id}` with
  `record`, and `/{page}/new` without it on a form page. A table on the
  target page reads the parameter values through its `parameters` map.

A page may hold a tree of widgets. The container
widgets `columns`, `tabs` and `section` hold other widgets, see
[D61](../decisions/d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md):

```json
{
  "id": "d6f9e3b2-8c4a-4d0f-8e2b-3a4c5d6e7f09",
  "kind": "page",
  "name": "PurchaseRequestsOverview",
  "formatVersion": 1,
  "title": { "textKey": "pages.overview.title" },
  "widgets": [
    {
      "type": "columns",
      "columns": [
        {
          "width": 8,
          "widgets": [
            { "type": "table", "entity": "PurchaseRequest" }
          ]
        },
        {
          "width": 4,
          "widgets": [
            {
              "type": "tabs",
              "tabs": [
                {
                  "label": { "textKey": "pages.overview.tasks" },
                  "widgets": [
                    {
                      "type": "section",
                      "title": { "textKey": "pages.overview.tasksTitle" },
                      "collapsed": true,
                      "widgets": [{ "type": "taskInbox" }]
                    }
                  ]
                }
              ]
            }
          ]
        }
      ]
    }
  ]
}
```

- A `columns` widget lists `columns`. Each has a `width`, a whole number from
  1 to 12, and its `widgets`. The widths of one `columns` widget add up to 12.
- A `tabs` widget lists `tabs`. Each has a `label` and its `widgets`.
- A `section` widget has a `title`, its `widgets` and an optional `collapsed`,
  which defaults to `false`.
- A label and a title are `{ "textKey": ... }`, like a page title.
- Containers nest to at most 4 levels. The page's own `widgets` list is
  level 1.
- A page holds at most one `table` of any kind, and at most one `form`,
  `taskInbox` or `processInspector`, so the URL state stays unambiguous.
  Every `widgets`, `columns` and `tabs` list has at least one entry.
- The compiler checks every widget at any depth. A problem is reported at the
  widget's own JSON pointer, such as `/widgets/0/columns/1/widgets/0/entity`.
- A widget, an action, a column or a tab may have a `name`. The name is optional,
  and no layer reads it yet.
- A widget's `name` is unique on the whole page, at any depth, ignoring letter case.
  A repeat is `AXC0361`, reported at the later widget's `name`.
- An action, column or tab `name` is unique in its own list, ignoring letter case.
  A repeat is `AXC0361`, reported at the later element's `name`. The actions of a
  `form` or `detail` widget, a table's `rowActions` and `toolbarActions`, a
  `columns` widget's columns and a `tabs` widget's tabs each have their own list,
  so a row action and a toolbar action may share a name.
- A kpi's `navigate` action has no `name`. The schema refuses one there.
- `formatVersion` stays 1. Today's pages stay valid.

A `seed` holds records with fixed ids for one entity:

```json
{
  "id": "e7a0f4c3-9d5b-4e1f-8f3c-4b5d6e7f8a08",
  "kind": "seed",
  "name": "Departments",
  "formatVersion": 1,
  "entity": "Department",
  "sync": true,
  "records": [
    { "id": "0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b", "values": { "name": "Finance" } }
  ]
}
```

- Each record's `id` is the record id, fixed so that the startup step
  inserts the record once. It is unique across every seed file of the
  application (`AXC0034`).
- `sync` is optional and defaults to `false`. When `true`, the startup step
  keeps existing records in step with the file. It writes only the fields
  the seed declares and never deletes a record (see
  [Startup activation](host-settings.md#startup-activation)).
- `values` has the shape of the `values` of a record API create body (see
  [Request bodies and values](record-api.md#request-bodies-and-values)).
  A seed cannot set a `file` field, in a record or in a child row (`AXC0259`).
  A record of an entity whose [module contract](module-contracts.md#where-the-contract-is-checked)
  has a lifecycle must give an initial state when it gives a state as text (`AXC0338`). A synced
  seed that changes a stored state must follow a transition.
- Seed files are inserted in path order, so a seed whose records reference
  another seed's records sorts after it.

A `rule` resource holds a named expression with typed parameters and a
result type. A validation, a data source filter or a process condition can
call it (see
[Expression language](expressions.md#names-and-references)). Calls from
computed fields come later:

```json
{
  "id": "8e16aa07-44d2-435b-8260-557ea9f3c1b2",
  "kind": "rule",
  "name": "IsPositive",
  "formatVersion": 1,
  "parameters": [{ "name": "value", "type": "integer" }],
  "resultType": "boolean",
  "expression": "value > 0"
}
```

- `parameters` is a list of `name` and `type`, and may be empty. Parameter
  and result types are `text`, `integer`, `decimal`, `boolean`, `date` and
  `date-time`. Any other type, `enum` included, is `AXC0004`. An `enum`
  parameter would need its own `values` to be compared with an enum field,
  so it comes later. A parameter is never a record.
- Parameter names are unique within the rule, ignoring letter case
  (`AXC0056`). A rule name may not be a built-in function name (`AXC0057`).
- A rule is called by name, ignoring letter case, such as `IsPositive(quantity)`.
  An integer argument fits a `decimal` parameter, and `null` fits any
  parameter.
- The `expression` must give the `resultType`, and sees only its parameters.
  It may call other rules, but not in a cycle (`AXC0055`) or more than 8 deep
  (`AXC0065`).
- The server runs the rule's expression when it evaluates the calling
  validation. Every argument is evaluated, even when one is `null`, and the
  rule's steps count against the caller's
  [step budget](expressions.md#cost-bounds).
- A data source filter's SQL inlines the rule's expression in place of the
  call, instead of running it. The expression must then be in the
  [SQL subset](expressions.md#sql-subset).

A `migration` resource changes one entity's storage before additive provisioning.
The resource, its compile checks and the once-per-tenant apply of `renameField`,
`dropField`, `dropEntity`, `fill` and `convert` at activation are built. See
[D30](../decisions/d30-schema-migrations.md) and
[migrations](migrations.md#migrations) for activation and old-release work.

```json
{
  "id": "7b3096e8-c14a-48f2-9a56-30bbd774e102",
  "kind": "migration",
  "name": "RenameDecisionComments",
  "formatVersion": 1,
  "entity": "PurchaseRequest",
  "changes": [
    { "type": "renameField", "from": "decisionComments", "to": "reviewComments" },
    { "type": "fill", "field": "priority", "value": "'normal'" }
  ]
}
```

The entity file in the same release renames `decisionComments` to
`reviewComments` and adds this required field:

```json
{ "name": "priority", "type": "enum", "values": ["low", "normal", "high"], "required": true }
```

- `id` is permanent. `kind` is `migration`, `name` names the migration and
  `formatVersion` is `1`. `entity` names the entity it changes. It cannot name a
  module entity such as `departments/Department`, which is `AXC0214`.
- `changes` is ordered. Each change has a `type` and the properties below.

  | Type | Properties | Effect |
  | --- | --- | --- |
  | `renameField` | `from`, `to` | Rename the column, keeping its values. |
  | `dropField` | `field` | Drop the column and its values. |
  | `dropEntity` | None | Drop the entity table, its child tables and provisioning records. |
  | `fill` | `field`, `value` | Fill every null with an expression over the record. |
  | `convert` | `field`, `value` | Convert stored values to the field's new type, or map removed enum values to kept ones. |

- Each migration has its own file. Files run in ordinal order of their
  relative path, such as `migrations/0001-rename-decision-comments.json`.
  Changes within a file run in list order.
- Migration files are part of the release and its content hash. Applied
  files must stay unchanged in later releases. Migrations run forward only.
- A `fill` or `convert` value must translate to the
  [SQL subset](expressions.md#sql-subset). An expression outside that subset
  is a compile diagnostic. The value is an expression, never raw SQL.
- A release holds only the entity as it is after its migrations. The compiler
  undoes the changes of all migrations of one entity, last change first, to
  rebuild the entity as it was before them. It then replays them forward in
  path order, so what an earlier migration leaves behind is what a later one
  starts from. A `from` or `field` that the entity lacks at that point is
  `AXC0171`, and a `to` that is already a field is `AXC0172`. A `fill` or
  `convert` `field` that the entity lacks in the release is `AXC0173`. A child
  collection is not a field for these checks. A migration of an entity that
  no loaded entity names is `AXC0170`, unless a migration of that name drops
  the entity. Changes after a `dropEntity` are not checked against fields.
  These checks only find contradictions inside the release. A name that never
  existed in the stored table is found when migrations are applied.
- A `fill` or `convert` value sees the record's own fields only. It cannot
  call rules, follow reference paths or use aggregates. `now()` is allowed and
  `currentUser()` is not.
- A value is type-checked against its field's type, and the problem is
  reported at `/changes/{i}/value` with its usual code, such as `AXC0048`. It
  is not type-checked when the type at that point is unknown: a later change
  drops or converts the field, the value comes after a `dropEntity`, or a
  `convert` reads its own field. The stored type is known only when the
  migration is applied. Every value is still parsed and checked against the
  SQL subset, so `upper(title)` is `AXC0053` in a `fill` and in a `convert`.
  A value that is not type-checked can name an unknown field or call
  `currentUser()` without a compile diagnostic.

## Schema keywords

Two custom keywords in the resource schemas tell [Studio](studio.md)
where to show a picker and where to show the expression editor, as
[D52](../decisions/d52-reference-pickers-label-texts-and-the-expression-editor.md)
agrees. The schemas stay the single source of what the form shows.

- **Kind form.** `"x-axis-reference": "<kind>"` marks a property that holds
  the name of a resource. The kind is one of `entity`, `dataSource`, `process`,
  `role`, `rule`, `connector`, `event`, `sequence`, `form`, `page` or `text`.
  The kind `text` marks a `textKey`.
- **Part form.** `"x-axis-reference": { "part": "<part>", "of": "<pointer>" }`
  marks a property that holds a part of another resource.
  - `part` is `field`, `step`, `parameter`, `column`, `outcome` or `operation`.
    A `parameter` or `column` part belongs to a data source, an `operation`
    part to a connector, and a `step` part to a process.
  - A mark on a map's `propertyNames` marks the map's keys. The data source
    `parameters` of a schedule trigger and of a table widget are such maps,
    because a parameter is named only as a key.
  - `of` is a JSON Pointer from the resource root, or a Relative JSON Pointer
    `N/<pointer>`, to the property that names the resource the part belongs
    to. A relative pointer goes up `N` segments from the marked property. For
    `outcome`, `of` names the task step. The picker gathers that step's outcomes
    from every process, because a `completeTask` step names a step and never a
    process.
- **Examples.** A form field's `field` is `{ "part": "field", "of": "/entity" }`,
  because the form's `entity` names the entity whose fields it lists. A grant's
  `fields` item is `{ "part": "field", "of": "2/entity" }`, because the entity
  sits two levels above the item. A process step's `next` is
  `{ "part": "step", "of": "/name" }`, because the process's `name` names the
  process whose steps it lists. A KPI's `measure` is
  `{ "part": "column", "of": "1/dataSource" }`, because the widget's
  `dataSource` sits one level above it and the picker lists that data source's
  columns. The keys of a schedule trigger's `parameters` map are
  `{ "part": "parameter", "of": "/dataSource" }` on `propertyNames`.
- *(Planned.)* **Set keys.** The keys of a process `updateRecord` step's `set`
  map are fields of the process's `/entity` in the same way.
- **Exempt relations.** Text entries, packages, modules, rule calls and field
  paths name no single property, so they have no mark. Stub packages and
  package operations name a package, and a grant's `endpoint` has no kind, so
  they have none either. Two entity marks, a data source parameter's `target`
  and an endpoint input's `target`, have no edge in the reference index yet.
  They offer entities, and the schema test checks them apart from the index.
- A test keeps the marks and the [reference index](configuration.md#reference-index) in step:
  each indexed relation has a mark or is listed as exempt.
- **Expressions.** `"x-axis-expression": true` marks a string property that
  holds an [expression](expressions.md). The form then shows the expression
  editor there.
- **Annotations only.** JsonSchema.Net ignores unknown keywords when it
  validates, so neither keyword changes what a resource may hold, and no
  resource file changes. The [reference index](configuration.md#reference-index) stays the
  list of reference paths.

## Entity field types and constraints

A field property is allowed only on the types that have an entry for it below.
Properties are optional unless marked *needed*. A property on another type is
`AXC0013`; a missing *needed* property is `AXC0014`.
Value ranges follow what PostgreSQL accepts, so an invalid value fails at
compile time rather than when the table is created; a value outside the range
is `AXC0013`.

| Type | `required` | `unique` | `maxLength` | `minLength` | `min`, `max` | `precision` | `scale` | `target` | `values` | `expression` | `sequence` | `creator` | `sensitive` | `exactLookup` |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `text` | yes | yes | 1..10485760 | 1..`maxLength` | | | | | | yes | yes | yes | yes | yes |
| `integer` | yes | yes | | | yes | | | | | yes | | | yes | yes |
| `decimal` | yes | yes | | | yes | 1..1000 | 0..`precision`, only with `precision` | | | yes | | | yes | yes |
| `boolean` | yes | yes | | | | | | | | yes | | | yes | yes |
| `date` | yes | yes | | | yes | | | | | yes | | | yes | yes |
| `date-time` | yes | yes | | | yes | | | | | yes | | | yes | yes |
| `enum` | yes | yes | | | | | | | needed | yes | | | yes | yes |
| `reference` | yes | yes | | | | | | needed | | | | | | |
| `child-collection` | | | | | | | | needed | | | | | | |
| `money` | yes | yes | | | yes | 1..1000, default 18 | 0..`precision`, default from the currency | | | yes | | | yes | yes |
- `required` and `unique` default to `false`.
- `maxLength` is capped at 10485760, the largest `varchar` length.
- `min` and `max` are literals of the field's type, written as the record API
  reads a value. An `integer` limit is a JSON integer in the signed 64-bit
  range. A `decimal` or `money` limit is a JSON number that fits a .NET
  `decimal`, at most 28 digits. A `date` limit is a string `yyyy-MM-dd`. A `date-time` limit is an
  RFC 3339 string with an offset, compared as an instant in UTC. Either can be
  left out. A limit of another JSON type, an invalid literal or `max` below
  `min` is `AXC0013`, at `/fields/{i}/min` or `/fields/{i}/max`. `min` equal to
  `max` is allowed.
- `minLength` is 1 or more, and at most `maxLength` when that is set, else
  `AXC0013` at `/fields/{i}/minLength`.
- `min`, `max` and `minLength` cannot be combined with `expression`, `sequence`
  or `creator`, because the server sets the value and a request never carries
  it. Each is `AXC0013` at the property.
- The server checks the limits when it reads a request, and the database has no
  `CHECK` constraint for them. So tightening a limit needs no migration, and a
  stored row outside the new limit keeps its values and still reads back. See
  [request bodies and values](record-api.md#request-bodies-and-values).
- `values` is a non-empty list of distinct strings; the JSON Schema checks
  this (`AXC0004`).
- `target` names an entity in the same application, ignoring letter case.
- `money` is an amount stored as `numeric(precision,scale)`. It takes exactly
  one of `currency`, a fixed ISO 4217 code written in upper case such as
  `"VND"`, or `currencyField`, which names an `enum` or `text` field of the
  same entity, ignoring letter case. `precision` defaults to 18. `scale`
  defaults to the currency's minor units (`VND` 0, `USD` 2, `KWD` 3), or to 2
  with `currencyField`. `scale` can be set without `precision`, but cannot
  exceed it. The compiled field holds the resolved values. The record API
  reads and writes it as a `decimal` with the same `precision` and `scale`,
  and its expression type is `decimal`. A field with neither property, with
  both, with a code that is not an ISO 4217 code with minor units, or with a
  `currencyField` that is not a `text` or `enum` field of the entity is
  `AXC0243`, at `/fields/{i}` when the currency is missing and at
  `/fields/{i}/currency` or `/fields/{i}/currencyField` otherwise. `currency`
  or `currencyField` on another type is `AXC0013`. A data source measure may
  `sum`, `min` or `max` a money field, and must then group by its
  `currencyField` (`AXC0248`), see [data sources](data-sources.md#aggregates).
- `child-collection` owns the rows of the entity that `target` names. It
  allows no other type-specific property. It has no `required`, because a
  missing collection means no rows, and no `unique`. Setting either, even to
  `false`, is `AXC0013`.
- `expression` makes the field a computed field. `required` on a computed
  field is `AXC0013` at `/fields/{i}/required`, and an `expression` on a
  `reference`, `references` or `child-collection` field is `AXC0013` at
  `/fields/{i}/expression`. See [Entity logic](#entity-logic).
- `sequence` names a [sequence](#sequences), ignoring letter case. It cannot
  be combined with `required: true` or `expression`, and a child entity's
  field cannot have it. Each is `AXC0013` at `/fields/{i}/sequence`.
- `creator` is allowed only on a `text` field. It cannot be combined with
  `required: true`, `expression` or `sequence`, and a child entity's field
  cannot have it. Each is `AXC0013` at `/fields/{i}/creator`. See
  [Record creator](roles-and-policies.md#record-creator).
- `sensitive` is allowed on `text`, `integer`, `decimal`, `boolean`, `date`, `date-time`, `enum` and `money`, and defaults to
  `false`. `true` on a `reference`, `references` or `file` field is `AXC0308` at `/fields/{i}/sensitive`, on the `displayField`
  at `/displayField`, on `child-collection` `AXC0013`. `exactLookup` needs `sensitive` and a name of at most 54 characters,
  else [`AXC0309`](diagnostic-codes.md). See [Sensitive fields](roles-and-policies.md#sensitive-fields), [exact lookup](data-encryption.md#exact-lookup).

### Types and properties added in M15

These were added in M15.
They follow
[D76](../decisions/d76-file-money-and-enum-label-field-types.md),
[D77](../decisions/d77-indexes-defaults-and-ranges.md),
[D78](../decisions/d78-references-from-child-rows-and-many-to-many.md) and
[D80](../decisions/d80-search-more-aggregates-and-query-timeouts.md). Each
new check is a new compile diagnostic.

- **`file`.** One file, with optional `maxSize` and `accept`. It takes
  `required`, and no `unique`, `default` or `expression`. See
  [file fields](file-fields.md). The compiler reports a `maxSize` below 1 or
  above the file size limit (`AXC0257`), an `accept` entry that is not
  `type/subtype` or `type/*` (`AXC0258`), and `unique` on the field
  (`AXC0259`). `maxSize` or `accept` on another type, and `expression` on a
  `file`, are `AXC0013`. In an expression a file allows only `is null` and
  `is not null`.
- **`references`.** A set of records of `target`, at most 1,000. It takes
  `required`, and no `unique`, `default` or `expression`. Its
  expression type is `list<Target>`. The compile side is built. `unique`,
  `expression` and `default` are `AXC0013`, and a missing `target` is
  `AXC0014`. A `target` that is a child
  entity is `AXC0040`, and one without a `displayField` is `AXC0030`. A
  `references` field on a child entity is `AXC0041`. It has no column, so a
  data source cannot project it (`AXC0043`), an endpoint cannot map it
  (`AXC0121`) and a process `updateRecord` cannot set it. The record API
  writes it as an array of record ids and reads it with its labels, see
  [reading values](record-api.md#reading-values). Its links live in a junction
  table, see [storage](storage.md#schema-planning).
- **`valueLabels`.** On an `enum` field, a map from every value to a
  `{ "textKey" }` label: `"valueLabels": { "low": { "textKey":
  "priority.low" } }`. It covers every value, and its keys join `AXC0028`. An
  enum parameter of a data source takes it too. A value that has no label, or a
  label for a value that `values` does not have, is `AXC0242`, at
  `/fields/{i}/valueLabels` or `/fields/{i}/valueLabels/{value}`. Values are
  compared exactly, by letter case too. On a field of another type it is
  `AXC0013`. The compile checks are built, and pages show the labels.
- **`default`.** An expression with no record in scope: literals, rule calls
  and `now()`. Its type must fit the field. It is not allowed with
  `expression`, `sequence` or `creator`, or on `reference`, `file`,
  `references` and `child-collection` fields. The compiler checks it now. A
  forbidden combination is `AXC0013` at `/fields/{i}/default`, and an
  expression problem reports its own code at the same path. The compiled field
  carries the checked default. A create applies it when the body
  leaves the field out, see
  [create, update and delete](record-api.md#create-update-and-delete). That
  holds for the record API, the record command, inbound endpoints and seed
  records. A required field with a default may be left out of a create, and
  `now()` is the start time of the write transaction. An update never applies
  a default. The [form state endpoint](record-api.md#form-state) returns the
  defaults of a new record, and the form widget shows them on a new form. A
  default on a field of a child entity does nothing for its rows yet.
- **`searchable`.** On a `text` field. `searchable` on any other type is
  `AXC0013` at `/fields/{i}/searchable`, even when it is `false`. The compile
  check and the indexes are built: a searchable field and the display field
  get a trigram index, see [schema planning](storage.md#schema-planning).
  `search` reads every searchable field the user may read, and the display
  field, which is always searched. See
  [paging and sorting](record-api.md#paging-and-sorting).

### Matched references

_(planned for M26)_ A `reference` field may have `match`, which finds its record by a unique field
instead of being written, as
[D124](../decisions/d124-the-organization-and-approvals-modules.md) agrees:

```json
{
  "name": "requesterEmployee",
  "type": "reference",
  "target": "organization/Employee",
  "match": { "field": "user", "from": "requester" }
}
```

- **Shape.** `match.field` names a `unique` field of the target entity. `match.from` names a field
  of this entity with the same type. Neither may be `sensitive`, unless the target field has
  `exactLookup`. A bad name or type is a diagnostic at `/fields/{i}/match`.
- **Value.** On every create and update, after defaults and creator fields and before computed
  fields and validations, the write sets the field to the target record whose `field` equals the
  value of `from`, or to `null` when `from` is empty or no record matches. The lookup reads as the
  system, so it needs no grant. A later change to the target does not change the stored reference
  until this record is written again.
- **Not writable.** Clients, process `set`, imports and seeds cannot write it. A value for it is a
  `400` at its pointer with "Cannot be set.", as for a creator field.
- **Allowed fields.** `match` cannot be combined with `required`, `expression`, `default` or
  `sequence`, and a child entity's field cannot have it.

### Indexes

`indexes` is built. On the entity, it is a list of
`{ "name": ..., "fields": [ ... ] }` with an optional `"unique": true`. See
[D77](../decisions/d77-indexes-defaults-and-ranges.md) and
[schema planning](storage.md#schema-planning).

- **`name`.** Required, so a [customization layer](customization-layers.md#list-keys)
  can find the index. It is a name like a field's, and it is unique in the
  entity's indexes, ignoring letter case. A missing `name` breaks the JSON
  Schema (`AXC0004`). It is not the PostgreSQL index name, which the platform
  builds from the table and the columns.
- **`fields`.** One to 32 distinct field names of the entity, matched
  ignoring letter case. They are the index columns, in order. An empty list,
  a repeated name and more than 32 names break the JSON Schema (`AXC0004`).
- **Field types.** An index holds a field that has a column. A
  `child-collection`, `references` or `file` field is `AXC0276`. A `references`
  field has no column, because a junction table holds its links.
- **Compile diagnostics.** Each is reported in the entity file:
  - `AXC0343`: an index has the name of an earlier one, ignoring letter case,
    at `/indexes/{j}/name`.
  - `AXC0274`: an index lists the same fields in the same order as an earlier
    one, at `/indexes/{j}/fields`.
  - `AXC0275`: a name in `fields` is no field of the entity, at
    `/indexes/{j}/fields/{k}`.
  - `AXC0276`: a field of a type an index cannot hold, at the same path.
- **Every reference.** Every `reference` field and every child table's
  `owner_id` is indexed without a declaration.

## Entity logic

This section adds validations, computed fields and child collections to an
entity (D17). Child collection fields, their child tables, validations and
computed fields are built, including
[aggregates](expressions.md#aggregates) over child rows.
Expressions use the syntax of the [expression language](expressions.md), and
diagnostic codes come with the issues that build each check. A validation
can call a named [rule](#resource-file-shape), such as `IsPositive(quantity)`
below. Both examples below compile, given that rule. A module entity's
validation calls the rules of its own module, see [modules](modules-and-templates.md#modules).

```json
{
  "id": "0ec02f4a-bf31-4409-8eb9-bfb9c39b6756",
  "kind": "entity",
  "name": "PurchaseRequest",
  "formatVersion": 1,
  "label": { "textKey": "purchaseRequest.label" },
  "displayField": "title",
  "fields": [
    { "name": "title", "type": "text", "required": true, "maxLength": 200 },
    { "name": "lineItems", "type": "child-collection", "target": "LineItem" },
    {
      "name": "total",
      "type": "decimal",
      "precision": 18,
      "scale": 2,
      "expression": "sum(lineItems, amount)"
    }
  ],
  "validations": [
    {
      "name": "needsLineItem",
      "expression": "count(lineItems) >= 1",
      "message": { "textKey": "purchaseRequest.needsLineItem" },
      "field": "lineItems"
    }
  ]
}
```

```json
{
  "id": "b6c15e88-82e5-43e7-a589-c48ba8a56bc3",
  "kind": "entity",
  "name": "LineItem",
  "formatVersion": 1,
  "label": { "textKey": "lineItem.label" },
  "fields": [
    { "name": "description", "type": "text", "maxLength": 200 },
    { "name": "quantity", "type": "integer" },
    { "name": "unitPrice", "type": "decimal", "precision": 18, "scale": 2 },
    {
      "name": "amount",
      "type": "decimal",
      "precision": 18,
      "scale": 2,
      "expression": "quantity * unitPrice"
    }
  ],
  "validations": [
    {
      "name": "quantityPositive",
      "expression": "IsPositive(quantity)",
      "message": { "textKey": "lineItem.quantityPositive" },
      "field": "quantity"
    }
  ]
}
```

- **Validations.** `validations` is a list on the entity. Each entry has:
  - `name`, which is required, so a
    [customization layer](customization-layers.md#list-keys) can find the
    validation. It is a name like a field's, and it is unique in the entity's
    validations, ignoring letter case (`AXC0343`, at `/validations/{i}/name`).
    A missing `name` breaks the JSON Schema (`AXC0004`).
  - `expression`, which must be boolean.
  - `message`, a label that is checked like other labels (`AXC0028`). Its text
    key is the message of a failure.
  - `field`, which names a field of the same entity.

  The expression reads the entity's own fields by name, and can call named
  rules. It can also aggregate over the entity's child collections, such as
  `count(lineItems) >= 1`. An aggregate reads the rows as they will be
  stored, so `count(lineItems) >= 1` refuses a create without rows and an
  update that removes the last row. The server runs the
  validations on create and update, on the record as it will be stored and
  after computed fields are calculated. A validation fails when its condition
  is `false` or `null`, or stops with a run-time error. The validations of a
  child entity run on each row the body sends. See
  [the record API](record-api.md#child-rows-computed-fields-and-validations).
- **Computed fields.** A field with an `expression` is a computed field.
  - The expression type must match the field's type, under the
    [result type rule](expressions.md#types).
  - The type must be a scalar type. A `reference` or a `child-collection`
    cannot be computed.
  - `required` is not allowed. `unique`, `maxLength`, `precision`, `scale` and
    `values` keep their usual meaning, because they describe the column that
    stores the value.
  - It reads only the record's own fields and its child rows, never a
    reference path. It reads the entity's own fields that are not computed.
    Naming a computed field, itself included, is `AXC0046`.
  - It cannot call `now()`, `today()` or `toDate`, because its stored value
    would go stale. A call, also inside an aggregate's item expression, is
    `AXC0086`.
  - It reads child rows only through an
    [aggregate](expressions.md#aggregates), such as `sum(lineItems, amount)`.
    Inside the item expression, names are the child row's fields, computed
    ones included, and nothing else.
  - The rows of a child collection are computed before the owner, so the
    owner's aggregates read the rows' computed values. An update that leaves
    a collection out aggregates its stored rows.
  - Clients cannot write it. A body that sets it, even to `null`, is a `400`
    at its pointer with the message "Cannot be set.". A run-time error is a
    `400` at its pointer with "Could not be computed.". A value that does not
    fit the column gets the message a body value would get. See
    [the record API](record-api.md#child-rows-computed-fields-and-validations)
    and
    [Child tables and computed columns](child-tables-and-computed-columns.md#child-tables-and-computed-columns).
- **Child collections.** The field type `child-collection` needs a `target`
  that names the child entity. Its rows are read and written only through the
  owner record, as
  [the record API](record-api.md#child-rows-computed-fields-and-validations)
  describes.
- **Limits on a child entity.** A child entity:
  - is owned by exactly one `child-collection` field (`AXC0039`);
  - is never the `target` of a `reference` or `references` field (`AXC0040`);
  - is never the `entity` of a data source (`AXC0064`) or of a process
    (`AXC0068`);
  - has no record routes of its own;
  - has no `child-collection` or `references` fields (`AXC0041`). It may have
    `reference` fields, as real line items point to a product, see
    [D78](../decisions/d78-references-from-child-rows-and-many-to-many.md). A
    reference from a child row gets a foreign key, and its labels follow the
    rows, see
    [child rows](record-api.md#child-rows-computed-fields-and-validations).
    Its index comes with the platform indexes on every reference column.

## Sequences

A `sequence` resource hands out business numbers, such as `PR-2026-00042`
(D21). The compiler checks sequences and the fields that name them. The
record create fills a field that names a sequence with the next number.

```json
{
  "id": "5d2f8a61-3c4b-4e7d-9a0f-6b1c2d3e4f09",
  "kind": "sequence",
  "name": "PurchaseRequestNumber",
  "formatVersion": 1,
  "format": "PR-{yyyy}-{n:5}"
}
```

- **Tokens.** `{yyyy}` is the four-digit year of the create's transaction
  time in the [application time zone](#application-time-zone), which is `UTC`
  when the application names none. `{n}` is the number, and
  `{n:k}` is the number zero-padded to `k` digits, with `k` from 1 to 18.
  Every other character is literal.
- **One number.** The format holds exactly one `{n}` or `{n:k}` token. It
  may hold `{yyyy}`, even more than once, and no other braces. Any other
  format is `AXC0004` at `/format`.
- **Yearly restart.** The counter restarts each year in the
  [application time zone](#application-time-zone), but only when the format
  holds `{yyyy}`. Without a year, a restart would hand out the same number
  twice. In `Asia/Ho_Chi_Minh`, a record created at 18:00 UTC on 31 December
  gets the first number of the next year.

An entity `text` field names the sequence in its `sequence` property:

```json
{
  "id": "0ec02f4a-bf31-4409-8eb9-bfb9c39b6756",
  "kind": "entity",
  "name": "PurchaseRequest",
  "formatVersion": 1,
  "label": { "textKey": "purchaseRequest.label" },
  "displayField": "title",
  "fields": [
    { "name": "number", "type": "text", "maxLength": 20, "unique": true, "sequence": "PurchaseRequestNumber" },
    { "name": "title", "type": "text", "required": true, "maxLength": 200 }
  ]
}
```

- `sequence` names a loaded `sequence` resource, ignoring letter case.
- The field gets its number on create, in the create's transaction, and
  clients cannot write it. See
  [the record API](record-api.md#create-update-and-delete) and
  [storage](storage.md#audit-records-and-sequence-counters).
- A field with `sequence` cannot be `required` or have an `expression`,
  because the server always assigns its value. A child entity's field cannot
  have a `sequence` either. Each of these is `AXC0013`.

## Application time zone

The `application` manifest may name the application's time zone, see [D75](../decisions/d75-the-application-time-zone.md):

```json
{
  "id": "6ec71db1-5b21-43d9-95d2-dcc45055eb0e",
  "kind": "application",
  "name": "ServiceDesk",
  "formatVersion": 1,
  "timeZone": "Asia/Ho_Chi_Minh"
}
```

- `timeZone` is optional. It is an IANA time zone id. Without it, the time
  zone is `UTC`, so an application that names none behaves as before. An id
  is known when .NET finds it as an IANA id written with its exact letter
  case, as for a schedule trigger. An unknown id is `AXC0277` at
  `/timeZone`. Studio's application form offers `timeZone`.
- It is part of the release. A change applies to work that runs on the new
  release.
- **What uses it.** [`today()` and `toDate`](expressions.md#functions), a
  sequence's [`{yyyy}`](#sequences) and its yearly restart. The dates in
  [notifications](notifications.md#placeholders) and the
  date buckets of data sources use it too.
- **What does not.** `now()` stays an instant. A schedule trigger keeps its own
  `timeZone`. The SPA shows a `date-time` in the browser's time zone, as
  before. Calendar durations such as `P1M` stay `AXC0084`.

## File storage

The `application` manifest may name the deployment binding that says where the bytes of its files go, see
[D76](../decisions/d76-file-money-and-enum-label-field-types.md):

```json
{
  "id": "6ec71db1-5b21-43d9-95d2-dcc45055eb0e",
  "kind": "application",
  "name": "ServiceDesk",
  "formatVersion": 1,
  "fileStorage": { "binding": "Files" }
}
```

- `fileStorage` is optional. It is an object with one `binding`, a binding name. Any other shape is a schema error.
  Without it, the bytes go to the tenant database and activation needs no binding.
- The binding must be a `storage` binding of the tenant. Activation reports a missing binding, or one of another
  kind, as `AXC0100` at `/fileStorage/binding` of `application.json`. On a preview tenant a missing binding is a
  warning.
- In M15 the binding's only value is `database`, so the bytes stay in `axis.file_contents`. See
  [deployment bindings](integration-resources.md#deployment-bindings).
