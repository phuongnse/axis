# D63. Navigate actions and page parameters

Status: **Agreed**

## Context

A page must open another page and pass it values, such as a row of My tickets that opens the ticket's
detail page. The link should survive a reload and work when shared, and the compiler should check it
as it checks other references.

## Decision

- **Typed page parameters in the URL query.** A page declares `parameters`, each with a name, an
  entity field type and whether it is required. Their values live in the URL query under their names,
  as a data source's parameters do ([D18](d18-data-sources.md)), and a name the table's own query uses
  is reserved.
- **A name map binds them.** A table binds a page parameter to a data source parameter by name, not
  by an expression, and a bound parameter has no filter input.
- **Navigate actions.** A `navigate` action names a target page and gives each of its parameters an
  expression over the row or the record, which may follow a short reference path. It sits in a
  table's row and toolbar actions ([D59](d59-table-row-and-toolbar-actions.md)) and in a form's
  actions. To a form page, which declares no parameters, it may set `record` to open that record, and
  otherwise opens a new one.
- **One format.** A table opens and creates records only through navigate actions.
- **Checked when compiled.** The compiler checks each target page, parameter and value type, and
  indexes each target as a `navigate page` relation
  ([D141](d141-the-reference-index-and-what-uses-a-resource.md)). A required parameter missing from the
  URL, or malformed, shows the not-found page.
- **Policies shape the actions.** Page metadata leaves out an action whose target page, or a field on
  whose path, the user may not read, and the server still checks every request
  ([D12](d12-authorization-is-server-side-and-default-deny.md)).

The checks are in the [configuration pipeline](../reference/configuration.md), the binding in
[widget binding](../reference/data-sources.md#widget-binding), and the addresses and buttons in
[frontend](../reference/frontend.md).

## Consequences

- A row can open any page with any values, and the compiler checks the link.
- A page address works as a link and survives a reload.
- A reference path in an action is read on click, one record at a time.

## Alternatives

- **A table's link to one form page.** It cannot pass a value to any other page, and keeping it
  beside navigate actions would give Studio, the samples and the docs two formats.
- **An expression in the binding.** A name map keeps the binding a plain name.
