# D63. Navigate actions and page parameters

Status: **Agreed**

Navigate actions with typed page parameters replace the table's `formPage`
link. This settles the navigation point of
[D15](d15-presentation-model.md). Page parameters are built, with their name and
type checks and their `field target` edge in the reference index. The navigate actions are
checked by the compiler, with their `navigate page` edge, their `record` and the form page
rule. Page metadata lists them with their `parameters` and `record`, gives each page its
`parameters`, and leaves out the actions to pages the user cannot read. The compiler resolves
reference paths such as `department.name` in the parameters and `record` of an action on a table
over an entity, a form and a detail. Page metadata gives each action a `paths` map, and leaves out
an action whose path the user may not read. The SPA buttons are
built on table rows, the table toolbar and saved forms, and put the values in the URL query.
`formPage` is removed from the schema, the compiler, page metadata, the SPA and Studio, and the
samples, the template, the scenarios and the test fixtures use navigate actions. The
table binding is built: the compiler checks a table's name map, page metadata gives each table its
`parameters`, the SPA reads the URL values, and a page whose required parameter is missing or
malformed shows the not-found page. The owner
confirmed these points on issue #931 on 2026-10-21.

- **Page parameters:**
  - A page declares typed `parameters`. Each has a `name`, a `type` and
    `required`. The types are the entity field types. A `reference` parameter
    names its `target` entity.
  - Values live in the URL query under the parameter names, as the parameters
    of a data source do ([D18](d18-data-sources.md)).
  - A table binds a page parameter to a data source parameter with a name
    map on the widget. A required data source parameter is allowed once it is
    bound to a required page parameter. A bound parameter has no filter input.
  - A page parameter may not be named `page`, `pageSize` or `sort`, nor like
    an unbound filter of its table, because they share the URL query.
- **Navigate actions:**
  - A `navigate` action names a target `page` and gives each of its
    parameters an expression. It sits in the `rowActions` and `toolbarActions`
    of a table ([D59](d59-table-row-and-toolbar-actions.md)) and in the
    `actions` of a form.
  - On a table row, the expression sees the row's columns or the record's
    fields as bare names, plus `id`. On a form it sees the record. `id` is the
    id of the root record, typed as a reference to the root entity.
  - An expression may use a reference path of up to 3 hops, such as
    `department.name`, on a table over an entity, a form and a detail, but not
    in `visibleWhen` or on a table over a data source, and not from `id`. Page
    metadata maps each reference along a path to the record API of its target
    in `paths`, and leaves the action out when the user may not read a field or
    entity along the path.
- **Form pages:** a page whose widget is a form declares no parameters. A
  navigate action to it may set `record`, a reference to the form's entity.
  With `record` it opens `/{page}/{id}`. Without it, it opens `/{page}/new`.
- **Removing `formPage`:** `formPage` is removed. Every sample, the scenarios
  and the Studio journey comparison move to navigate actions in the same
  change, as the one-format rule of
  [D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)
  requires.
- **Compile checks:** each of these is a diagnostic:
  - an unknown target page,
  - an unknown parameter,
  - a missing required parameter,
  - an expression of the wrong type,
  - `record` on a page that is not a form page, or over another entity,
  - a duplicate or reserved page parameter name,
  - an unknown parameter type or `reference` target.
- **Missing values:** when a required parameter is missing from the URL or has
  the wrong form, the not-found page shows inside the site shell, as for a
  malformed record id.
- **Reference index:** each navigate target is a `navigate page` relation to
  the target page ([D39](d39-reference-index-and-where-a-resource-is-used.md)).
- **Authorization:** the page metadata leaves out a navigate action whose
  target page the user cannot read. The server still checks every request
  ([D12](d12-authorization-is-server-side-and-default-deny.md)).

_Why:_ `formPage` links a table to one kind of page and cannot pass a value to
any other page. A typed parameter and an expression per parameter let a row
open any page, and the compiler can check the link, as it checks other
references. Keeping values in the URL query makes a page address work as a
link and survive a reload. A name map keeps the binding a plain name, not an
expression. Reserved names keep the table's paging and sorting apart from page
parameters. A single format, with no `formPage` beside it, keeps Studio, the
samples and the docs simple.
