# Frontend

Detailed reference for the frontend: the SPA, its rendering from metadata and
its widgets. The conventions of [architecture.md](../architecture.md) apply:
sections marked *(planned for Mx)* are not built yet, and Dn refers to
[decisions.md](../decisions.md).

- **One SPA for all applications.** The SPA renders from metadata served by
  the server: site navigation, pages and their widgets, data source schemas
  *(planned for M2)* and text resources. Publishing an application never
  rebuilds the frontend.
- **Shared look and feel.** A page is a route in a site, and its widgets are
  its content. Each widget type, such as `table` and `form`, has one shared
  component, and layout comes from container widgets rather than per-page
  templates. Those components and a single token-based theme with light and
  dark modes live in `web/src/platform/`. Feature code assembles them and
  adds no styling of its own.
- **How pages and widgets grow.** M1 has one widget per page and binds it to
  an entity. Container widgets such as tabs, sections and columns will hold
  other widgets. Data sources are agreed in
  [D18](../decisions.md#d18-data-sources--agreed) and described in
  [data-sources.md](data-sources.md). Navigate actions and shared `form`
  resources are expected to replace the other M1 shortcuts; see
  [D15](../decisions.md#d15-presentation-model--agreed), where those parts are
  still **Proposed**.
- **Text.** All UI strings come from text resources.
- **Sites and texts.** The server describes sites to the SPA through
  read-only endpoints. Like every other `/api` path, they need a known tenant
  host.
- **Platform site.** The built-in platform site in `Axis.Presentation` serves
  the shell through two endpoints, `GET /api/site` and
  `GET /api/texts/{locale}`. The home page also uses `GET /api/sites` (see
  Application sites below) to list the tenant's sites.
  - `GET /api/site` returns the site name, its title key, the locales
    (default, fallback and available) and the navigation items. Navigation
    labels are text keys, never literal text.
  - `GET /api/texts/{locale}` returns a flat map from text key to text for
    one locale. A locale without texts gets a 404 problem response.
  - The platform site offers English (`en`, default and fallback) and
    Vietnamese (`vi`). The platform site stays: application sites never
    replace or merge into it. The SPA reaches them through the site endpoints
    below. A test checks that every locale has exactly the same keys as
    English.
- **Application sites.** The sites of the tenant's active applications are
  found by path, not by application name, because the path is what users see
  and one application can have several sites. Four endpoints describe them.

  | Method and path | Response |
  | --- | --- |
  | `GET /api/sites` | `{ "sites": [ { "path", "titleKey", "titles" } ] }`, every site of every active application, in path order |
  | `GET /api/sites/{path}` | `{ "path", "titleKey", "locales": { "default", "fallback", "available" }, "navigation": [ { "page", "labelKey" } ] }` |
  | `GET /api/sites/{path}/texts/{locale}` | `{ "locale", "texts" }`, the texts of the site's application for one locale, in the shape of the platform texts |
  | `GET /api/sites/{path}/pages/{page}` | the page metadata shown below |

  A page response looks like this:

  ```json
  {
    "name": "Items",
    "titleKey": "items.title",
    "widgets": [
      {
        "type": "table",
        "formPage": "ItemForm",
        "entity": {
          "name": "Item",
          "labelKey": "item.label",
          "displayField": "name",
          "recordsPath": "/api/apps/RecordsApp/entities/Item/records",
          "fields": [
            {
              "name": "name", "type": "text", "labelKey": null,
              "required": true, "unique": false, "maxLength": 100,
              "precision": null, "scale": null, "values": null, "target": null
            },
            {
              "name": "department", "type": "reference", "labelKey": "item.department",
              "required": false, "unique": false, "maxLength": null,
              "precision": null, "scale": null, "values": null,
              "target": {
                "entity": "Department",
                "displayField": "name",
                "recordsPath": "/api/apps/RecordsApp/entities/Department/records"
              }
            }
          ]
        }
      }
    ]
  }
  ```

  - `titles` in the site list has one entry per available locale of the site,
    from the locale as the site declares it to the title text. The home page
    can show each site without one more request per site.
  - `type` of a widget is `table` or `form`. `formPage` is the page holding
    the form for a table's records, and `null` otherwise.
  - `entity` is the widget's entity: its name, its label key, its display
    field, the path of its record API in `recordsPath`, and its fields in
    declaration order.
  - Each field has its name, its `type` as written in entity files (such as
    `date-time`), its label key, `required` and `unique`, `maxLength` for
    text, `precision` and `scale` for decimal, `values` for enum, and
    `target` for reference. A property the field's type does not have, or a
    label the file leaves out, is `null`; it is never left out.
  - `target` names the referenced entity, its display field and the path of
    its record API, so the SPA never builds a record URL itself.
  - Names in responses (page, form page, entity, and the application in
    `recordsPath`) are the model's declared names, not the letter case of the
    request.
  - The texts endpoint serves any locale the site's application has texts
    for, and the page endpoint any page of the site's application, not only
    pages in the site's navigation, because form pages opened from a table
    are not in navigation.
  - `{path}`, `{locale}` and `{page}` match ignoring letter case. A path
    outside the pattern (an ASCII letter, then ASCII letters, digits or
    hyphens, at most 60 characters, matched ignoring letter case) is a 404
    without a query. A reserved path such as `api` is a 404 because no site
    can hold it.
  - An unknown site, page or locale is a 404 problem with a fixed title. A
    site active only in another tenant does not exist for the request.
  - The endpoints have no authorization yet, like the record API.
- **Routing.** The SPA routes on the client with `react-router` v7. The
  server answers every other path with the SPA, so each address below
  answers 200.

  | Address | Shows |
  | --- | --- |
  | `/` | the platform home page: the server status and the tenant's sites as links, each titled in the current locale, or in the site's first locale when it lacks the current one |
  | `/{site}` | the site's first navigation entry, by redirect |
  | `/{site}/{page}` | the page inside the site shell, with the page title. A table page also takes `?page=&pageSize=&sort=`. A table bound to a data source also takes its parameter values *(planned for M2)* |
  | `/{site}/{page}/new` | the form of a form page, to create a record |
  | `/{site}/{page}/{id}` | the form of a form page, to edit the record with that id |

  - `{site}` is the site path. `{page}` is the page name in lower case, as
    navigation links write it. The server matches both ignoring letter case.
  - An unknown site shows the not-found page inside the platform shell. An
    unknown page shows it inside the site shell.
    An address with more than three segments, such as `/e2e/notes/42/edit`,
    matches no route and shows the not-found page inside the platform shell.
  - A form page without `new` or an id, `new` or an id on a table page, an
    id that is not in the hyphenated 8-4-4-4-12 hex form, and an id the
    entity has no record for show the not-found page inside the site shell.
    An id in the wrong form is not requested.
  - Inside a site, the shell shows the site's title, navigation and locales.
    The theme toggle and the locale switch work as they do on the platform
    site.
- **Table widget.** A page whose widget is a `table` lists the records of its
  entity through the record API, in the shared Ant Design table.
  - **Columns.** There is one column per field, in declaration order. The
    header is the field's label, or the field name when it has no label.
  - **URL state.** Paging and sorting live in the URL as the record API's own
    `page`, `pageSize` and `sort` (`-` and the field name for descending), so
    reload, sharing and the back button keep them. The widget writes them
    after every other parameter, always in the order `page`, `pageSize`,
    `sort`, and leaves out defaults (page 1, 20 a page, no sort). It changes
    only its own three parameters: any other parameter, such as `x=1`, stays
    as it is.
  - **Data source binding** *(planned for M2)*. A table may name a
    `dataSource` instead of an `entity`. See
    [data-sources.md](data-sources.md#widget-binding). Page metadata then
    gives the widget `"entity": null` and a `dataSource` object. An entity
    widget has `"dataSource": null`.

    ```json
    "dataSource": {
      "name": "OpenRequests",
      "rowsPath": "/api/apps/PurchasingApp/data-sources/OpenRequests/rows",
      "entity": "PurchaseRequest",
      "parameters": [
        {
          "name": "statusFilter", "type": "enum", "required": false,
          "labelKey": "requests.statusFilter",
          "values": ["draft", "submitted", "approved", "rejected"], "target": null
        }
      ],
      "pageSize": 20,
      "columns": [
        {
          "name": "status", "type": "enum", "labelKey": "request.status",
          "values": ["draft", "submitted", "approved", "rejected"], "target": null
        }
      ]
    }
    ```

    - **Parameters.** A parameter's `labelKey` is the text key of its label, or
      `null` when it has none. `values` and `target` are `null` when the type
      has none.
    - **Columns.** There is one column per projected field, in order. The
      header is the label of the field the path ends at, else the projected
      name. A measure's header is its name.
    - **Filter inputs.** There is one input per parameter, of the kind the form
      widget uses for that type. It shows the parameter's label, or its name
      when `labelKey` is `null`.
    - **URL state.** The parameter values live in the URL under the parameter
      names. The widget writes them before `page`, `pageSize` and `sort`.
    - **Rows.** The table requests its rows from `rowsPath`.
  - **Changes.** A new sort or page size starts again at page 1, because the
    old page number means nothing under a new order or size. Only the
    pagination control moves between pages. Each change adds a history entry.
  - **Invalid values.** A `page` that is not a positive integer, a `pageSize`
    other than the offered 10, 20, 50 and 100, and a `sort` that names no
    field or a reference field are ignored. The default is requested instead,
    and the URL is rewritten without them and without a history entry. Once
    the total is known, a `page` past the last page (at least 1) becomes the
    last page in the same way, so the URL and the pagination agree. With no
    records the last page is 1, so `page` is removed.
  - **Sorting.** Every column but a reference is sortable. The record API
    sorts a reference by the stored id, which does not match the label people
    see. Repeated clicks on a header sort ascending, then descending, then
    not at all.
  - **Values.** Values are formatted for display and never changed:

    | Field type | Shown as |
    | --- | --- |
    | `date-time` | date and time to the second in the UI locale and the browser's time zone. The fraction is dropped |
    | `date` | the date in the UI locale, without a time-zone shift |
    | `integer`, `decimal` | exactly as the API wrote them, right-aligned |
    | `boolean` | a localized yes or no |
    | `reference` | the label from `labels` |
    | `text`, `enum` | the value |
    | `null` | an empty cell |

  - **Number source text.** The SPA parses record responses with the source
    text of each JSON number, so `values` holds integers and decimals as
    strings. A plain parse would show `1250.50` as `1250.5` and lose digits
    beyond double precision. `version`, `page`, `pageSize` and `totalCount`
    are record metadata, not field values, so they are turned back into
    numbers. A browser without `JSON.parse` source text access (older than
    Chromium 114, Firefox 135 or Safari 18.4) falls back to the parsed
    number, so `1250.50` shows as `1250.5` there. The web unit tests need
    Node 22 or later for the same reason. The API never coerces a string to a
    number, so the form widget sends numbers back as JSON numbers.
  - **Links.** When the widget names a form page, a create button links to
    `/{site}/{formpage}/new` and each row has an open link to
    `/{site}/{formpage}/{id}`, with the page name in lower case. The create
    button is a link: a click with a modifier key or the middle button opens
    the form in a new tab or window. Both pass the table's address, with
    its paging and sorting, to the form in history state. Without a form
    page there is no create button and no open column.
  - **States.** Loading, empty and error states use the shared table and
    alert with platform texts. While the next page loads, the current rows
    stay under the loading overlay.
- **Form widget.** A page whose widget is a `form` creates a record at
  `/{site}/{page}/new` and edits one at `/{site}/{page}/{id}`, through the
  record API. It adds no rules of its own: the server validates.
  - **Inputs.** There is one input per field, in declaration order, labelled
    with the field's label or the field name. A required field's label is
    marked, but the mark blocks nothing: the server still decides.

    | Field type | Input |
    | --- | --- |
    | `text` | a text input limited to `maxLength` |
    | `integer`, `decimal` | a plain text input with a numeric keyboard hint. It keeps the typed text as it is |
    | `boolean` | a checkbox |
    | `date` | a date picker |
    | `date-time` | a date and time picker to the second, in the browser's time zone. It sends the instant in UTC with no fraction |
    | `enum` | a choice of the declared values, as written in the entity file |
    | `reference` | the label of the chosen record, read-only, with a choose button that opens the lookup. A field that is not required and is set also has a clear button |

  - **Lookup.** The choose button opens a dialog titled with the field's
    label. It lists the target entity's records through their record API,
    sorted ascending by the target's display field, in one column for that
    field. It pages 20 records at a time and keeps nothing in the URL.
    Loading, empty and error states use the table's texts. Clicking a row
    or pressing Enter on it picks the record and closes the dialog. Search
    comes with filtering in M2.
  - **Reference label.** On edit, the label starts from the record's
    `labels`. A picked record's display field value replaces it at once. A
    changed reference is sent as the record id, and a cleared one as `null`.
    Picking the record that was already set leaves the field unchanged.
  - **Changed fields.** The form sends only the fields whose value differs
    from the value it started from. A new record starts with every field
    `null`, so an untouched field stays out of a create and the server
    decides what is required. An edit also sends the `version` it read. An
    emptied text or number input is sent as `null`.
  - **Number text.** The body is written by hand, so an integer or decimal
    goes out as the typed text and no digit is lost. Text that is not a JSON
    number, such as `1,5`, goes out as a JSON string, and the server rejects
    it on its field. The form never turns number text into a JavaScript
    number.
  - **Errors.** A `400` or `409` problem is mapped by its JSON Pointer keys. A
    key `/values/<field>` for a field of the form shows its messages under
    that field. Every other key, such as `""`, `/values` or `/version`, shows
    above the form. A save that fails in any other way shows a shared error
    there too.
  - **Conflicts.** A `409` on edit that names no field is a stale version. The
    form shows a conflict message with a reload button, which loads the
    current values and version and drops the user's changes. A duplicate
    unique value is a `409` keyed `/values/<field>`, so it shows under its
    field.
  - **Return address.** Save and cancel return to the table page that opened
    the form, with its paging and sorting, which the table passes in history
    state. Without it, such as in a new tab, they go to `/{site}`, which opens
    the site's first page. Only an address inside the same site is used.
  - **Keys.** Enter in a date or date-time picker only confirms the picked
    value and never sends the form. Enter in a text input sends the form, as
    in any form.
- **Locale.** The shell header has a locale switch next to the light/dark
  toggle. The chosen locale is kept in `localStorage` under `axis.locale`,
  like the theme mode under `axis.themeMode`. One key serves every site: a
  site starts in the stored locale when it offers it, and in its default
  locale otherwise. The shell keeps the current texts until the chosen
  locale's texts have loaded. If they fail to load, it stays in the current
  locale and shows an error message.
  - Ant Design components, such as picker placeholders, follow the UI
    locale. `en` maps to Ant Design's `en_US` and `vi` to `vi_VN`, ignoring
    letter case, and any other locale uses `en_US`. dayjs, which the pickers
    use, follows the same locale. The shell sets both through a nested
    `ConfigProvider` that inherits the theme tokens.
- **Text resolution.** The SPA resolves each key through catalogs in order.
  On the platform site the only catalog is the platform texts. Inside an
  application site, the site texts come first and the platform texts second,
  so shared shell texts such as the theme toggle keep working.
  - Inside a site, the platform texts load in the site's current locale when
    the platform offers it. Otherwise only the platform fallback-locale texts
    serve the shell.
  - So a key resolves from the site's current locale, then the site's
    fallback locale, then the platform's current locale, then the platform's
    fallback locale. A key no catalog has shows its key name.
  - Within one catalog, a key missing from the current locale but present in
    the fallback locale shows its key name in development builds, so it gets
    noticed, and the fallback-locale text otherwise. Only a key missing from
    both locales of a catalog moves on to the next catalog. Every locale of a
    catalog has the same keys, so the development rule never hides a
    platform text behind a site that lacks it.
