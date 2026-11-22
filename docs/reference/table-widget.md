# Table widget

This page describes the table widget, which lists the records or data source rows on a page, with its columns, search, filters, paging and actions. The rest of the frontend is in [frontend](frontend.md).

## Table widget

A `table` widget lists the records of its entity through the record API, or the rows of its data source. It draws on
the platform's one shared table, `AxisTable`, whose look is in [tables](actions-and-controls.md#tables).

### Columns

There is one column per field, in declaration order. The header is the field's label, or the field name when it has no
label. A child collection and a `references` field have no column.

- **Identifying column.** The column that names a row is the entity's `displayField` when the table shows it, and
  otherwise the first column. For a data source it is the first column. It comes first. With a `navigate` row action it
  is the link that opens the row, see [opening a row](#opening-a-row).
- **`priority`**. A `table` widget's `priorities` gives a column `high` or `low` by name, as `{ "amount": "low" }`,
  for what moves under its row first when [room runs out](actions-and-controls.md#tables).
- **Layout.** Text, reference and file columns are free text, which shares the width left and wraps. Every other
  column, an enum too, fits its content and does not wrap, see [tables](actions-and-controls.md#tables). Every cell
  and the actions column line up at the top, so a row with wrapped text reads along its first line. Number columns sit
  on the right in tabular figures, see [shown values](frontend.md#shown-values).

### Toolbar

The toolbar always shows, also when the table has no search and no toolbar action. What changes the view sits on the
left: the search box, the Filter button and, on a data source table, its filter inputs. On the right
is a Refresh icon button, named by the platform text `shell.table.refresh` ("Refresh", in `vi` "Làm mới"), which loads
the current page of rows again. Then come the secondary actions, Import, Export and the toolbar actions after the first,
and last the first shown toolbar action, as the table's main action, see [buttons](actions-and-controls.md#buttons).
Toolbar actions sit above the table.

### Search

A table whose rows can be searched shows a search box above the rows, on the left, with the toolbar buttons on the
right.

- **When.** An entity table has a search box when the entity's display field is among the fields the user may read,
  that is in the widget's `entity.fields`. A data source table has one when the data source's metadata has
  `"search": true`.
- **Applying.** The box applies as [tables](actions-and-controls.md#tables) says, so typing does not request rows
  for every key. The text lives in the URL as `search`, a new search adds a history entry, and paging and sorting
  keep it.
- **The text.** The text is sent to the rows request as typed, without trimming. An empty text means no search, so
  clearing the box removes `search` from the URL. The box takes at most 200 characters, the limit of the rows routes.
- **No search.** On a table that cannot search, a `search` in the URL is ignored and removed without a history entry,
  as an invalid `sort` is, and is not sent. `search` is a reserved page parameter name, because the table owns it in
  the URL.

### Filter

A table with a column the builder offers has a Filter button in the toolbar, after the search box, which opens the
builder of [tables](actions-and-controls.md#tables).

- **Columns.** The builder offers the text, enum, integer, decimal, money and date columns. It leaves out boolean,
  reference, file and date-time columns, as the record API refuses the first three and the builder's date picker gives
  no time for the fourth. An enum column offers its values by their labels. The builder cannot tell which fields are
  `sensitive`, so it may offer one, and the record API then answers `400`. A measure column of a grouped data source
  has no filter, so the builder leaves it out. A bucketed group column is a date.
- **Operators.** Each column takes the operators the record API takes for its type, see
  [record API paging and sorting](record-api.md#paging-and-sorting).
- **Sending.** The filter lives in the URL as `filter`. The rows request and the export send it as `filter`, to the
  record API of an entity or to the rows and export routes of a data source. The data source routes take it as
  [query parameters](data-sources.md#query-parameters) describes.

### Paging and sorting

Paging, sorting and search live in the URL, so reload, sharing and the back button keep them.

- **URL state.** They are the record API's own `page`, `pageSize`, `sort` (`-` and the field name for descending),
  `search` and `filter`. The widget writes them after every other parameter, always in the order
  `search`, `filter`, `page`, `pageSize`, `sort`, and leaves out defaults: no search, no filter, page 1, 20 a page or
  the data source's page size, and no sort. It changes only its own parameters and, for a data source, its
  [parameter values](#data-source-binding). Any other parameter, such as `x=1`, stays as it is.
- **Changes.** A new sort or page size starts again at page 1, because the old page number means nothing under a new
  order or size. Only the pagination control moves between pages. Each change adds a history entry.
- **Invalid values.** A `page` that is not a positive integer, a `pageSize` other than the offered sizes, and a `sort`
  that names no field, a reference field or a child collection field are ignored. The default is requested instead,
  and the URL is rewritten without them and without a history entry. Once the total is known, a `page` past the last
  page (at least 1) becomes the last page in the same way, so the URL and the pagination agree. With no records the
  last page is 1, so `page` is removed.
- **Sorting.** Every column but a reference and a file is sortable, because the record API sorts a reference by the
  stored id, which does not match the label people see, and cannot sort a file. Repeated clicks on a header sort
  ascending, then descending, then not at all.

### Address prefix

_(planned for M42)_

A table may set `prefix`, such as `"prefix": "orders"`, so that a page can hold several tables. The table's
`search`, `filter`, `page`, `pageSize` and `sort`, and its unbound data source parameters, then live in the URL
as `orders.search`, `orders.filter`, `orders.page`, `orders.pageSize`, `orders.sort` and `orders.{parameter}`.
They keep the order and the rules of [paging and sorting](#paging-and-sorting). A table without a `prefix` keeps
the plain keys, and a page has at most one table, calendar, board or cards widget without a `prefix`, see
[calendar, board, cards and tree widgets](calendar-board-card-tree-widgets.md).

A page parameter never takes a prefix. So a table bound to a page parameter reads its value from the URL under
the plain name, and every table bound to it follows the same value. A customer page with its orders and tickets
looks like this:

```json
{
  "widgets": [
    { "type": "detail", "name": "customer", "entity": "Customer", "record": "customer" },
    { "type": "table", "name": "orders", "prefix": "orders", "dataSource": "CustomerOrders", "parameters": { "customer": "customer" } },
    { "type": "table", "name": "tickets", "prefix": "tickets", "dataSource": "CustomerTickets", "parameters": { "customer": "customer" } }
  ]
}
```

The table's key in the SPA takes the prefix too, see [the table key](frontend.md#table-views-and-columns).

### Values

Values are formatted for display and never changed. The rules of [shown values](frontend.md#shown-values) apply.

| Field type           | Shown as                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| -------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `date-time`          | date and time in the UI locale and the browser's time zone, to the minute on the locale's clock, with no seconds. The fraction is dropped                                                                                                                                                                                                                                                                                                          |
| `date`               | the date in the UI locale, without a time-zone shift                                                                                                                                                                                                                                                                                                                                                                                               |
| `integer`, `decimal` | the number in the locale's digit grouping, with every digit the API wrote, right-aligned in tabular figures                                                                                                                                                                                                                                                                                                                                        |
| `money`              | the amount with its currency in the UI locale, such as `$1,250.50` in `en` and `1.250.000 ₫` in `vi`, right-aligned. It shows the field's `scale` in decimal places, or more when the API wrote more, so it never rounds. The currency is `currency`, or the value of `currencyField` in the same row, or of `currencyColumn` in a data source row. Without a code of three letters it shows the number grouped like a decimal, without a currency |
| `boolean`            | a localized yes or no                                                                                                                                                                                                                                                                                                                                                                                                                              |
| `reference`          | the label from `labels`                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `text`, `enum`       | the value. An enum with `valueLabels` shows the label                                                                                                                                                                                                                                                                                                                                                                                              |
| `file`               | the file's name as a download link to `downloadPath` and the file's id. A data source row holds a file as its bare id, so a data source column shows the id and no link                                                                                                                                                                                                                                                                            |
| `null`               | a muted italic "—". A group row of a data source stays blank                                                                                                                                                                                                                                                                                                                                                                                       |

- **Reference cells** _(planned)_. A `reference` cell is a link to its record page, see
  [opening things](actions-and-controls.md#opening-things). In the identifying column the link stays the row's link,
  as [opening a row](#opening-a-row) says, because a link cannot hold a link.

### Number source text

The SPA parses record responses with the source text of each JSON number, so `values` holds integers and decimals as
strings, because a plain parse would show `1250.50` as `1250.5` and lose digits beyond double precision. `version`,
`page`, `pageSize` and `totalCount` are record metadata, not field values, so they are turned back into numbers. A
browser without `JSON.parse` source text access (older than Chromium 114, Firefox 135 or Safari 18.4) falls back to
the parsed number, so `1250.50` shows as `1250.5` there. The web unit tests need Node 22 or later for the same reason.
The API never coerces a string to a number, so the [form widget](form-widget.md) sends numbers back as JSON numbers.

### Data source binding

A table may name a `dataSource` instead of an `entity`, as [widget binding](data-sources.md#widget-binding) says. Page
metadata then gives the widget `"entity": null` and a `dataSource` object, and an entity widget has
`"dataSource": null`.

```json
"dataSource": {
  "name": "OpenRequests",
  "rowsPath": "/api/apps/PurchasingApp/data-sources/OpenRequests/rows",
  "entity": "PurchaseRequest",
  "parameters": [
    {
      "name": "statusFilter", "type": "enum", "required": false,
      "labelKey": "requests.statusFilter",
      "values": ["draft", "submitted", "approved", "rejected"],
      "valueLabelKeys": { "draft": "request.status.draft", "submitted": "request.status.submitted", "approved": "request.status.approved", "rejected": "request.status.rejected" },
      "target": null
    }
  ],
  "pageSize": 20,
  "search": false,
  "columns": [
    {
      "name": "status", "type": "enum", "labelKey": "request.status",
      "values": ["draft", "submitted", "approved", "rejected"],
      "valueLabelKeys": { "draft": "request.status.draft", "submitted": "request.status.submitted", "approved": "request.status.approved", "rejected": "request.status.rejected" },
      "target": null
    }
  ]
}
```

- **Parameters.** A parameter's `labelKey` is the text key of its label, or `null` when it has none. `values` and
  `target` are `null` when the type has none. `valueLabelKeys` maps each enum value to the text key of its label, and
  is `null` without `valueLabels`. The SPA resolves the keys with the site texts, while rows keep the raw value.
- **Columns.** There is one column per projected field, in order. For a grouped data source, the columns are the group
  fields in `groupBy` order, then the measures. The header is the label of the field the path ends at, else the
  projected name. A measure has no `labelKey`, so its header is its name. A `count` is an `integer` column, a `sum` is
  a `decimal` column, and a `min` or `max` has its field's type. A column over a labelled enum field has the same
  `valueLabelKeys` as the field, and `null` otherwise. Sorting, values and labels follow the column's type, as for an
  entity table. A measure column has `"measure": true`, and every other column leaves the property out.
- **Page size.** The data source's `pageSize` is the table's default page size. It is offered with 10, 20, 50 and 100
  when it is not one of them, and the URL leaves it out.
- **Search.** `search` is `true` when the data source declares `"search": true`, and the table then shows the
  [search box](#search). Otherwise it shows none.
- **Filter inputs.** A filter bar above the rows has one input per parameter, in declaration order, labelled with the
  parameter's label, or its name when `labelKey` is `null`. Each input is the kind the [form widget](form-widget.md)
  uses for that type, with two exceptions. A `boolean` is a Yes/No choice that can be cleared, because an optional
  parameter has three states: true, false and not given. A `reference` has a choose button that opens the lookup, and
  a clear button once it has a value. The SPA does not check the values: the server does.
- **Bound parameters.** A data source parameter that the widget's `parameters` binds to a page parameter has no filter
  input and is not written to the URL by the table. The table sends the page parameter's value from the URL under the
  data source parameter's name, and leaves the page parameter in the URL as it is. A page parameter that is not given
  is not sent.
- **Applying a value.** A `text`, `integer` or `decimal` value applies on Enter or when the input loses focus, so
  typing does not request rows and add a history entry for every key. A date, a choice, a Yes/No and a picked record
  apply at once. A cleared value is removed.
- **URL state.** The parameter values live in the URL under the parameter names, and the table sends them to
  `rowsPath`. The widget writes them after every other parameter and before `search`, `page`, `pageSize` and `sort`,
  in declaration order. A new value starts again at page 1 and adds a history entry, so reload, sharing and the back
  button keep the filter. An empty value is left out, and a repeated parameter keeps its first value. Both are
  rewritten without a history entry, as for paging and sorting.
- **Reference label.** The URL holds only the record id. After a reload the table loads that record of the target
  entity to show its display field. When the record is missing or cannot be loaded, the input shows the id.
- **Rejected values.** When the server rejects a parameter value, such as a hand-edited `?statusFilter=bogus`, its
  message shows under that input and the table shows no rows. The load error alert is for other failures.
- **Rows.** The table requests its rows from `rowsPath`. A row carries the id of its root record, so a row action with
  `record: id` opens its form as for an entity table. A group row of a grouped data source has `id: null` and no row
  actions.

### Opening a row

A table has no create button set apart from its actions. It opens a record through a `rowActions` navigate with
`record: id`, and creates one through a `toolbarActions` navigate without `record`, which needs
[`create`](frontend.md#access). Both pass the table's address, with its paging and sorting, to the form in history
state.

- **The identifying cell is the link.** The first `navigate` row action opens a row from its identifying column, which
  is a link, and has no button of its own. The link has a real `href` when the address is known at once, so Ctrl+click
  opens it in a new tab. A reference path in the action, which is read on click, gives no `href`.
- **Plain text.** An action whose `visibleWhen` hides it leaves the cell as plain text, as does a group row or an
  action the SPA cannot build an address for.
- **An empty cell** shows the action's label as the link text, so the row can still be opened.
- **A file column.** When the identifying column is a `file` column, its download link stays and no action is the
  link, so every `navigate` action stays a row action, because a link cannot hold a link.

### Row and toolbar actions

Page metadata lists the `navigate` actions in `rowActions` and `toolbarActions`, without those to pages the user cannot
read, each with its `parameters`, `record`, `opensForm` and `paths`. It lists a `startProcess` in `rowActions` only
with a `start` grant on its process. A `navigate` action may sit in either list, and `startProcess` and `delete` are
row actions only. A row of a data source acts on its root record, and a group row has no actions.

- **In the row.** Row actions are buttons in the row, unless the row folds, see below. A `delete` is a trash icon in
  `dangerText`, named by its label, and any other action a small text button.
- **Folded.** With three or more configured actions, a row with two or more of them folds at any width. At narrow
  width a row with two or more folds too. They fold into one "…" icon button named by the platform text
  `shell.table.rowActions`, which opens a menu of text entries, a delete entry in `dangerText`. The count is of the
  actions the widget configures besides the open link. A row that `visibleWhen` leaves with one action shows it as a
  button, so the rows of one table can differ.

### Navigate

A `navigate` action is a button that opens another page and sets its parameters. A My tickets row opens a TicketDetail
page and sets its `ticket` parameter:

```json
{
  "type": "table",
  "name": "tickets",
  "entity": "Ticket",
  "rowActions": [
    {
      "type": "navigate",
      "name": "open",
      "page": "TicketDetail",
      "label": { "textKey": "tickets.open" },
      "parameters": { "ticket": "id" }
    }
  ]
}
```

The target page declares `"parameters": [{ "name": "ticket", "type": "reference", "target": "Ticket", "required": true }]`,
and the click goes to `/{site}/ticketdetail?ticket=<id>`.

- **Addresses.** With `record`, the button opens `/{site}/{page}/{id}`. Without it, an action to a form page, one with
  `opensForm`, opens `/{site}/{page}/new`. The table's address in history state lets a form return to it after save
  or cancel.
- **Values.** The SPA has no full expression engine. It works out a bare name (`id`, a column or a field, matched
  ignoring case) and a literal (text in single quotes, a number, `true`, `false` or `null`), and a button whose
  expression uses anything else is hidden. The one exception is [`visibleWhen`](#visiblewhen), which works out a
  larger part of the language. A toolbar has no row, so only its literals resolve. A parameter whose value is `null`
  is left out of the query, and a button whose `record` is `null` is hidden. The target page reads its parameters from
  the URL and binds them.
- **Reference paths.** A reference path such as `customer.parent.name` is resolved on click: the SPA reads each record
  along it through the record API, using `paths`. A `null` reference gives `null`. A `403`, a `404`, a missing field or
  a failed read opens no page and shows `shell.navigate.failed`. A button whose first name is not in scope or lacks a
  `paths` entry is hidden.

### visibleWhen

A row action may have a `visibleWhen` condition, a boolean expression over the row, such as one that shows Delete only
on drafts. The compiler checks it against the row's names, as it checks a `navigate` parameter. It only shapes the UI,
and the server decides, as [authorization](security.md#authorization) says.

- **The part the SPA works out.** Names, literals, `==`, `!=`, `is null`, `is not null`, `in (…)` with literal items,
  `and`, `or`, `not` and parentheses. Keywords ignore letter case.
- **Showing.** The button shows only when the result is `true`, and hides when it is `false` or `null`. It also hides
  when the condition uses syntax outside that part, such as `<`, `+`, a path or a function call, or reads a name the row
  does not hold. A field the user may not read is not in the row, so it counts as a missing name. A missing name hides
  the button even where `and` or `or` could stop early.
- **Nulls.** Comparison is null safe: `null == null` is `true` and `null in (…)` is `false`. In `and`, `or` and `not`,
  `null` means unknown, so `not null` and `true and null` are `null` and hide the button, while `false and null` is
  `false`.
- **Numbers.** A number literal compares as a number with a value, so `5` equals a stored `5.00`. Two names compare by
  their text.
- **The author stays inside it.** The compiler does not refuse a well-typed condition the SPA cannot work out, such as
  `amount > 100`.

### Delete

A row has a delete button only when the widget's `access.actions` holds `delete`. A click opens a confirmation dialog,
and on confirm the SPA sends `DELETE {recordsPath}/{id}` with `X-Axis-Csrf: 1`, with `recordsPath` from the action's
metadata. The answers:

| Answer    | What the table does                                                                                                                                 |
| --------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| `204`     | shows "The record was deleted." and loads the current page of rows again. A page past the last page becomes the last page, as for an invalid `page` |
| `403`     | shows "You may not delete this record.", such as when the record filter denies the row                                                              |
| `404`     | shows "This record no longer exists."                                                                                                               |
| `409`     | shows "This record is in use and cannot be deleted.", for a record that another record references and for a record held by a running process        |
| Any other | shows the generic platform error                                                                                                                    |

On a failure the row stays. The success and the failure messages show above the table.

### Import

A table over an entity has an Import button only when `access.actions` holds `import`. It is a secondary action, before
the main action. The page designer's canvas draws it and opens nothing.

1. A click opens a dialog with one file picker for a `.csv` or `.xlsx` file, a Check button and a Close button. Another
   extension is refused in the dialog and sends nothing.
2. Check sends `POST {entity}/imports` with the file as the raw body and `X-Axis-Csrf: 1`. The imports path is the
   entity's `recordsPath` with its last `/records` swapped for `/imports`. The `Content-Type` comes from the file
   extension, `text/csv` or the `xlsx` type, because browsers often report a CSV differently.
3. The dialog reads the import at once and then every second, with no time limit, until it is `checked` or `failed`.
   Closing the dialog stops the checking.
4. The dialog lists each error as the row and the field's label, for `/rows/{n}/values/{field}`, as a row error for
   `/rows/{n}`, as a column error for `/columns/{n}`, and as a file error for any other key. A field the table lacks
   shows its name.
5. Create records shows only when the import is `checked`. It sends `POST {entity}/imports/{id}/commit` and checks every second
   until the import is `committed` or `failed`. On `committed` the dialog closes, a success message shows above the
   table and the current page of rows loads again. A `failed` commit lists its errors in the dialog.

Picking another file starts a new import. A `403`, `413` or `415` on the upload each shows its own message, and any
other failure shows the generic one. The progress and the errors sit in an `aria-live` region, and the picker and every
button take the keyboard.

### Export

A table has an Export dropdown button only when `access.actions` holds `export`. The page designer's canvas draws it and
exports nothing.

- **The choice.** The dropdown offers CSV and Excel, and it opens and selects with the keyboard.
- **The request.** A choice sends `GET` to the export route: the entity's `recordsPath` with `/export` added, or the
  data source's `rowsPath` with its last `/rows` swapped for `/export`. The address holds the table's data source
  parameter values, bound and filter, its `search`, its `filter` and its `sort`, as the rows
  request has them, and `format` as `csv` or `xlsx`. It holds no `page` or `pageSize`, so the file has every matching
  row.
- **The file.** The SPA saves the file under the name in the response's `Content-Disposition`, through a temporary
  download link. A success shows no message. When the response carries `Axis-Export-Truncated: true`, a notice above
  the table says the file stops at the row limit.
- **Failures.** A `403`, `409` or `503` each shows its own message above the table, and any other failure, a network
  error included, shows the generic one.

### Start process

A row `startProcess` follows the rules of the [form widget](form-widget.md)'s start process action, with the row as the
subject and the same `Idempotency-Key` and `409` text, without a save first. On `201` it loads the current page of rows
again, with no success message. A failure shows above the table.

### Bulk actions

_(planned for M42)_

A table with `bulkActions` shows a checkbox on each row and an "N selected" bar above the rows, as
[tables](actions-and-controls.md#tables) says. The bar holds the bulk actions. The table's metadata lists a bulk action
only when the person may run it:

- A `startProcess` needs the `start` grant on its process.
- A `setFields` needs `access.updateFields` to hold each of its fields.
- A `delete` needs `access.actions` to hold `delete`.

A `delete` asks for confirmation first. A `setFields` opens a dialog with one input for each of its fields. The
inputs are drawn as the [form widget](form-widget.md) draws them. The same values go to every selected row.

The SPA makes one call to the [bulk route](record-api.md#bulk-actions), with the selected rows' ids and, for an
update, their versions. It then shows a result for each row, clears the selection and loads the current page of rows
again. Each row that failed is listed, as [tables](actions-and-controls.md#tables) says.

The reasons are in [D159](../decisions/d159-pages-show-related-lists-boards-and-calendars-forms-take-context-and-drafts-and-each-site-has-its-brand.md).

### Inline edit

_(planned for M42)_

An entity table with `"inlineEdit": true` lets a person edit a cell in place. A cell is editable only when its field is
in `access.updateFields` and the field is not computed, a sequence, a `creator` or a matched reference. A data source
table has no inline edit.

A click or Enter opens the cell's input, drawn as the [form widget](form-widget.md) draws the field's type. A cell saves
on Enter or when it loses focus, with `PATCH {recordsPath}/{id}`, the row's `version` and `X-Axis-Csrf: 1`. Escape
restores the value.

The answers:

- `200` gives the row its new values and its new version.
- `400` shows the message at the cell and keeps the old value.
- `403`, `404` and `409` each show their own message above the table and restore the value.
- Any other failure shows the generic platform error and restores the value.

The reasons are in [D159](../decisions/d159-pages-show-related-lists-boards-and-calendars-forms-take-context-and-drafts-and-each-site-has-its-brand.md).

### Column chooser

_(planned for M42)_

A `table` widget has the Columns control of [tables](actions-and-controls.md#tables). It keeps each person's choice of
columns under the table's key, see [table views and columns](frontend.md#table-views-and-columns). The identifying
column stays first and always shows. The `priorities` still decide what moves under a row when room runs out.

The reasons are in [D159](../decisions/d159-pages-show-related-lists-boards-and-calendars-forms-take-context-and-drafts-and-each-site-has-its-brand.md).

### States

Loading, empty and error states use the shared table and alert with platform texts. While the next page loads, the
current rows stay under the loading overlay.
