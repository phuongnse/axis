# Frontend

The frontend is one single-page application (SPA) that draws every site, page and widget of every application from
metadata the server serves. This page covers the rules every widget follows, the design tokens, the endpoints that
describe sites and pages, the routes, a person's table views and columns, the table widget and narrow width. The frame around a page, the account button and
the sign-in prompt included, is in [shell and home page](shell.md), and buttons, menus, fields, tables and dialogs are
in [actions and controls](actions-and-controls.md).

## One SPA

These rules keep every application looking and behaving alike, and let the platform replace its UI library without
touching an application.

- **One SPA for all applications.** The SPA draws from metadata served by the server: site navigation, pages and their
  widgets, data source schemas and text resources. Publishing an application never rebuilds the frontend.
- **Shared look and feel.** A page is a route in a site, and its widgets are its content. Each widget type, such as
  `table` and `form`, has one shared component, and layout comes from [container widgets](container-widgets.md) rather
  than per-page templates. Those components and a single token-based theme with light and dark modes live in
  `web/src/platform/`. Feature code assembles them and adds no styling of its own.
- **The UI library is a detail of the SPA.** The SPA builds on Ant Design for its base components. Configuration, page
  metadata, configurable theme tokens and the custom widget API never name an Ant Design component, prop or token. A
  [custom widget](custom-widgets.md) builds on Axis widgets and Axis tokens and never imports the UI library directly.
  A specialised library is added when a widget needs one, and those in use, such as React Flow, CodeMirror and Apache
  ECharts, follow the same rule. No layer draws UI from
  a schema on top of Ant Design. The reasons are in
  [D58](../decisions/d58-ant-design-is-a-detail-of-the-spa-without-procomponents.md).
- **Widgets expose their state to tests and assistive technology.** They are found by role, accessible name and ARIA
  state, never by a class of the UI library. Where the library leaves a state out, such as loading (`aria-busy`) or
  the current page (`aria-current`, as on the active navigation entry), the widget adds it.
- **Text.** Every string the UI shows comes from text resources, as [text resolution](#text-resolution) says.
- **Buttons, menus and fields.** A button's level follows its role and its place, menus mark the current entry, and
  every form places field errors the same way, as [actions and controls](actions-and-controls.md) says.

## Design tokens and previews

The theme's values live in one token file in Axis names, so a value changed once is changed everywhere and no
component holds a number of its own.

- **The token file.** `web/src/platform/designTokens.ts` holds every colour, shadow, type, spacing, radius, size and
  breakpoint value of the theme.
- **Colours.** The colours are `primary`, `success`, `warning`, `danger`, `info`, `primaryText`, `successText`,
  `warningText`, `dangerText`, `infoText`, `canvas`, `surface`, `text`, `textMuted`, `border`, `borderSubtle`,
  `divider`, `focusRing` and `onPrimary`, each with a light and a dark value. The first five are seeds, which colour
  fills, icons and tints, except a message's icon. The `*Text` tokens colour text and a message's icon in that state
  colour: state text, links, the selected tab, the active navigation entry, form validation messages, the current page
  of a pagination, destructive menu entries and the text of outlined and danger buttons, see
  [buttons](actions-and-controls.md#buttons). `focusRing` colours the solid
  keyboard focus of every control. `onPrimary` colours text and icons on a `primary` fill.
- **Contrast.** Every text colour reaches 4.5:1, and every border and focus ring 3:1, on `surface` and `canvas` in both
  modes. `onPrimary` reaches 4.5:1 on `primary` in both modes. [Each preview](widget-previews.md#the-spec) is checked.
- **Rules on a flush page** _(planned)_. `borderSubtle` stands apart from `surface` about as much in light mode as in
  dark, because on a flush page a rule is all that parts two areas. `border` stays for controls.
- **A site's brand** _(planned for M42)_. A site's `brand.primary` is its main colour, see [sites](configuration.md#sites)
  and [D159](../decisions/d159-pages-show-related-lists-boards-and-calendars-forms-take-context-and-drafts-and-each-site-has-its-brand.md).
  The compiler works out the light and dark `primary`, `primaryText`, `focusRing` and `onPrimary` from it, and the
  compiled site carries them. The SPA draws that site's shell and pages with them in place of the token file's values,
  and every other token keeps its value. The platform site, Studio and the consoles keep the token file. A colour that
  misses the Contrast rule is refused when compiled. The look is left to design reviews.
- **Shadow.** The shadow group holds `overlay`, the only shadow. Dialogs, menus, pickers and popovers cast it, and
  widgets, cards, sections and buttons cast none.
- **Type, spacing and radius.** Type has the body and code fonts and the font size, spacing runs from `spacing-xs` to
  `spacing-xl`, and radius has the base radius.
- **Sizes.** The size group holds `bar`, the height of the shell's header, a page's band and Studio's application row,
  `smallControl`, a small icon button and the line of text beside it, `tableRow`, a table's row and header, and
  `sidePanel`, the width of a side panel, see [layout](shell.md#layout).
- **Dialog sizes.** The size group also holds `dialog`, the dialog widths `small` (400 px), `medium` (560 px) and
  `large` (880 px), see [dialogs](actions-and-controls.md#dialogs).
- **No value of its own.** No size, space, type, radius or colour is a number of its own in a component, and a layout
  technique, such as a column's 1 % width, takes its size from a token.
- **Fonts.** The body font is IBM Plex Sans, a variable font for weights 100 to 700, and the code font is IBM Plex Mono
  at weight 400. The SPA ships the Google Fonts files from the Fontsource npm packages unchanged. Only the Latin, Latin
  Extended and Vietnamese subsets ship, each with a `unicode-range` in `web/src/fonts.css`, so a browser downloads only
  the subsets a page uses. The fonts are under the SIL Open Font License 1.1, whose texts ship at
  `/fonts/OFL-IBM-Plex-Sans.txt` and `/fonts/OFL-IBM-Plex-Mono.txt`, from `web/public/fonts`. The font files are part
  of the build output and load from the SPA's own origin (`font-src 'self'`), so an installation with no internet
  shows them too. The design-system bundle carries no fonts.
- **Ant Design reads the tokens through one file.** `web/src/platform/antTheme.ts` is the only file that maps the
  tokens to Ant Design tokens. A component that needs a theme value reads Axis tokens through `useDesignTokens`, never
  through Ant Design's `theme.useToken`. A Vitest test fails on a hard-coded colour, font family or radius in
  `web/src` outside the token file and `antTheme.ts`.
- **Charts.** The shared chart component in `web/src/platform/Chart.tsx` maps the Axis tokens to chart colours, font
  and grid, and loads the chart library in its own chunk.
- **Widget previews.** The previews draw every widget, container and field input from fixture metadata, in light and
  dark mode. `scripts/design-system.sh` builds them into one classic script that sets `window.Axis` with a mount for
  each preview. A mounted widget follows `data-theme` on the page's root element. For the current mode it also reads
  these custom properties from that element, and draws again when one changes:
  - `--primary` to `--onPrimary` for the colours, named as above, so `--primaryText` to `--infoText` and
    `--focusRing` too;
  - `--shadowOverlay`, `--spacing-xs` to `--spacing-xl`, `--radius-base`, `--font-sans` and `--font-code`.

  A property the page leaves out, or sets to a value the theme cannot use, keeps its token file value. A shadow value
  is usable when it is not empty and has no `;`, `{` or `}`. Only the design-system bundle reads these properties. See
  [widget previews](widget-previews.md) and [design changes](../delivery.md#design-changes).

## Icons

Icons come from one set and one style, so they look alike, and configuration never names the library's icons.

- Every icon comes from `@ant-design/icons`, and nothing in the SPA draws one by hand. A Vitest test fails on an
  `<svg>` element in a `.tsx` file under `web/src`.
- Only the outlined style is used. An icon takes the text colour and size of its control, so its colour comes from
  theme tokens and is never hard-coded.
- An icon next to its text, or inside a control with its own accessible name, is `aria-hidden`. An icon-only control
  takes its accessible name from a text resource.
- Configuration, page metadata and the custom widget API never name an icon, as
  [the UI library rule](#one-spa) says. The exception is a site's navigation entry, which names an icon from Axis's
  own list, see [navigation icons](shell.md#navigation-icons). `web/src/platform/navigationIcons.ts` maps each name to
  an icon.

## Reduced motion

When the browser or the operating system asks for reduced motion (`prefers-reduced-motion: reduce`), the theme turns
Ant Design's motion off, so dialogs, drawers, menus, messages and overlays show and hide at once. The SPA follows a
change of the setting while a page is open, without a reload, and the design-system previews follow it too. Without
the preference, motion stays on. Playwright asks for reduced motion in every project, so no end-to-end step clicks
during an opening animation, and a spec that proves a wait asks for motion again.

## Shown values

A value reads the same in every widget: in an input, a read-only field, a table cell, a detail and a KPI.

- **Dates and times.** There is one format for each locale. A date is the locale's short numeric date from `Intl`,
  such as 03/18/2026 in `en` and 18/03/2026 in `vi`. A date-time is the date, then the time to the minute on the
  locale's clock, such as `03/04/2026 4:30 PM` in `en` (12 hours, no leading zero) and `04/03/2026 16:30` in `vi` (24
  hours, two digits). There are no seconds. `Intl`'s no-break spaces are written as plain spaces. The pickers use the
  same pattern, so the input and the text agree.
- **Numbers.** Integers, decimals and money show in the locale's digit grouping wherever they are read, such as
  16,945.75 in `en` and 16.945,75 in `vi`, with a decimal's scale of digits. They are formatted from the value's
  [source text](table-widget.md#number-source-text), so no digit is lost. An input shows the digits as typed. In a table and a child
  collection numbers sit on the right, in the header, the text and the input cells, in tabular figures. On a form and
  a detail they sit on the left under their label.
- **Booleans.** A read-only boolean shows Yes or No, the platform texts `shell.table.yes` and `shell.table.no`.
- **Empty values.** A read-only field on a form or a detail shows the platform text `shell.value.notSet` ("Not set",
  in `vi` "Chưa có") in muted italics. A table cell, a child collection cell and a KPI show a muted italic "—". A
  KPI's dash carries a visually hidden `shell.kpi.empty` ("No value") for assistive technology. Words go where fields
  are read one by one, and a dash where values are read at a glance or compared. What tells an empty value from a
  typed one is the styling, in which no value is ever drawn, never the words.
- **No identifiers.** No widget shows a GUID to a user.

## Text resolution

Every string the UI shows comes from text resources, and the SPA resolves each text key through catalogs in order,
so the shell's own texts keep working inside every application site.

- On the platform site the only catalog is the platform texts. Inside an application site, the site texts come first
  and the platform texts second, so shared shell texts such as the language and theme menu keep working.
- Inside a site, the platform texts load in the site's current locale when the platform offers it. Otherwise only the
  platform's fallback-locale texts serve the shell.
- So a key resolves from the site's current locale, then the site's fallback locale, then the platform's current
  locale, then the platform's fallback locale. A key that no catalog has shows its key name.
- Within one catalog, a key missing from the current locale but present in the fallback locale shows its key name in
  development builds, so it gets noticed, and the fallback-locale text otherwise. Only a key missing from both locales
  of a catalog moves on to the next catalog. Every locale of a catalog has the same keys, so the development rule
  never hides a platform text behind a site that lacks it.

## Locale

The person picks a locale and a theme in the shell's [language and theme](shell.md#app-switcher-and-preferences) menu,
and the SPA keeps both in the browser.

- **Browser storage.** The chosen locale is kept in `localStorage` under `axis.locale`, beside the theme mode that
  [themes](shell.md#app-switcher-and-preferences) keeps. The only other key the SPA keeps in browser storage is
  `axis.tenantExport`, the id of the last [export](platform-pages.md#export-data-and-usage). The id is not sensitive
  data, because the export routes still need an administrator session. Sign-out clears all three through
  `Clear-Site-Data`, so the next sign-in starts in the system theme and the site's default locale.
- **One key for every site.** A site starts in the stored locale when it offers it, and in its default locale
  otherwise.
- **Changing the locale.** The shell keeps the current texts until the chosen locale's texts have loaded. If they fail
  to load, it stays in the current locale and shows an error message.
- **Ant Design's locale.** Ant Design components, such as picker placeholders, follow the UI locale. `en` maps to Ant
  Design's `en_US` and `vi` to `vi_VN`, ignoring letter case, and any other locale uses `en_US`. dayjs, which the
  pickers use, follows the same locale. The shell sets both through a nested `ConfigProvider` that inherits the theme
  tokens.

## Site endpoints

The server describes sites to the SPA through read-only endpoints. Like every other `/api` path, they need a known
tenant host.

- **The platform site.** The built-in platform site in `Axis.Presentation` serves the shell through
  `GET /api/site` and `GET /api/texts/{locale}`. The home page and the app switcher also use `GET /api/sites` to list
  the tenant's sites, and when it answers `401` the home page starts sign-in at once, see
  [platform home page](shell.md#platform-home-page).

  | Route                     | What it does                                                                                |
  | ------------------------- | ------------------------------------------------------------------------------------------- |
  | `GET /api/site`           | The platform site's name, navigation and legal links, see [site endpoints](#site-endpoints) |
  | `GET /api/texts/{locale}` | The platform texts of one locale, see [site endpoints](#site-endpoints)                     |

  - `GET /api/site` returns the site name, its title key, the locales (default, fallback and available), the
    navigation items and `legal`, the tenant's `{ "privacyUrl", "termsUrl" }` or `null`, see
    [legal settings](legal.md#legal-settings). Navigation labels are text keys, never literal text, and each
    navigation item has an `icon`.
  - `GET /api/texts/{locale}` returns a flat map from text key to text for one locale. A locale without texts gets a
    `404` problem response.
  - The platform site offers English (`en`, default and fallback) and Vietnamese (`vi`). It stays beside the
    application sites, which never replace or merge into it. A test checks that every locale has exactly the same keys
    as English, and the placeholder and untranslated-text rules of [UI writing](ui-writing.md#checks).
- **Application sites.** The sites of the tenant's active applications are found by path, not by application name,
  because the path is what users see and one application can have several sites. Four endpoints describe them, the page one has a second form for module pages, and a person's favourites and recent items have routes of their own.

| Route                                         | Response                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| --------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GET /api/sites`                              | `{ "sites": [ { "path", "titleKey", "titles", "descriptions", "application": { "name", "titles", "descriptions" }, "legal" } ] }`, every site of every active application, in path order. `application.titles` holds the application's title for each locale it has texts for: the text of the `label` in its `application.json`, or its name when it has no label. `descriptions` holds the text of the `description` for each locale, the site's for its available locales and the application's for each locale it has texts for, or is `null` when there is no description, see [descriptions](shell.md#application-and-site-descriptions). |
| `GET /api/sites/{path}`                       | `{ "path", "titleKey", "locales": { "default", "fallback", "available" }, "navigation": [ { "page", "labelKey", "icon" } ], "notificationsPath", "legal" }`. `icon` is an Axis icon name, or `null` for an entry with none. `legal` is `{ "privacyUrl", "termsUrl" }`, the site's own links or else its tenant's, or `null` when neither sets any, as in the list                                                                                                                                                                                                                                                                               |
| `GET /api/sites/{path}/texts/{locale}`        | `{ "locale", "texts" }`, the active release's texts for one locale, in the shape of the platform texts                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| `GET /api/sites/{path}/pages/{page}`          | the [page metadata](#page-metadata). The page of a module is `GET /api/sites/{path}/pages/{module id}/{name}`, two path segments as the record API writes a module entity. Its `name` is `{module id}/{name}` and its title and labels resolve from the module's texts                                                                                                                                                                                                                                                                                                                                                                          |
| `GET /api/sites/{path}/pages/{module}/{page}` | the [page metadata](modules-and-templates.md#module-pages) of a module's page                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `GET /api/sites/{path}/favourites` _(planned for M42)_ | `{ "favourites": [ { "id", "page", "record", "label" } ] }`, newest first. `record` is `null` for a page alone, and `label` is the record's display field value, or `null` |
| `POST /api/sites/{path}/favourites` _(planned for M42)_ | takes `{ "page", "record" }`, with `record` `null` for a page alone. Answers `201` with the favourite, or `200` with the one already kept |
| `DELETE /api/sites/{path}/favourites/{id}` _(planned for M42)_ | `204`, for the person's own favourite |
| `GET /api/sites/{path}/recent` _(planned for M42)_ | `{ "recent": [ { "page", "record", "label", "openedAt" } ] }`, latest first, at most 20 |
| `POST /api/sites/{path}/recent` _(planned for M42)_ | takes `{ "page", "record" }` and notes it as opened now, `204` |

- **Titles.** `titles` in the site list has one entry for each available locale of the site, from the locale as the
  site declares it to the title text, so the home page can show each site without one more request per site.
- **Texts.** The texts endpoint serves any locale the site's application has texts for. Without `release`, it uses the
  active release. Optional `?release={releaseId}` serves the texts of any stored release of the site's application.
  An unknown id, a non-UUID or another application's release answers `404`.
- **Any page.** The page endpoint serves any page of the site's application, not only pages in the site's navigation,
  because form pages opened from a table are not in navigation.
- **Paths.** `{path}`, `{locale}` and `{page}` match ignoring letter case. A path outside the pattern (an ASCII letter,
  then ASCII letters, digits or hyphens, at most 60 characters, matched ignoring letter case) is a `404` without a
  query. A reserved path such as `api` is a `404`, because no site can hold it.
- **Not found.** An unknown site, page or locale is a `404` problem with a fixed title. A site active only in another
  tenant does not exist for the request.
- **A session first.** The endpoints need a session. Without one they answer a `401` problem, before any `404`, so a
  signed-out caller cannot learn which sites or pages exist. So every application page shows the
  [sign-in prompt](shell.md#session-ended) when nobody is signed in, and the browser stays on the page.
  The record API and the data source endpoint also need a grant, so a page's data fails to load for a user without
  one.
- **Load time.** At typical size, a page with a three-level widget tree loads in under 2 s: its metadata, then the rows
  of its table, chart and KPIs read in parallel over 100,000 records. `PageLoadPerformanceTests` fails when the best of
  three loads takes 2 s or more.
- **Favourites and recent items** _(planned for M42)_. The lists belong to the signed-in person, the application and the
  site, and no route names a person. A `page` must be a page of the site's application. A `record` must be a record of
  the entity that page shows, and one the person may read, otherwise `404`. A 101st favourite answers `409`, and the
  recent list keeps the latest 20. A `GET` reads each `label` under the current grants, and leaves out a page or record
  the person cannot read or that no longer exists. Writes need the [CSRF header](security.md#csrf-header). An `id` that
  is not the person's own answers `404`. On a site's own host, only that site's path answers. The lists are described in
  [favourites and recent items](shell.md#favourites-and-recent-items), and the rule is
  [D159](../decisions/d159-pages-show-related-lists-boards-and-calendars-forms-take-context-and-drafts-and-each-site-has-its-brand.md).
- **Menu groups, home page and brand** _(planned for M42)_. `GET /api/sites/{path}` gives a parent entry `page: null`
  with its `entries`. It adds `homePage`, the name of the person's home page, and `brand`, which is
  `{ "logo", "colours" }` with the logo's address and the light and dark colours, or `null`. See
  [sites](configuration.md#sites).
- **Record pages** _(planned)_. `GET /api/sites/{path}` adds `"recordPages": { "<entity>": "/{site}/{page}" }`, the
  address of each entity's record page: the named page, or else the default record page, as [routing](#routing) says. A
  module page is `/{site}/{module id}/{name}`. It lists only the entities whose `read` the person's grants allow, see
  [access](#access), so the SPA links only what the person can open. The rule is
  [D162](../decisions/d162-each-entitys-records-open-on-one-record-page-in-each-site.md).

## Page metadata

The page endpoint describes a page as the SPA draws it: its title, its parameters and its tree of widgets, each with
the entity, data source, actions and access it needs, already shaped by the user's [policies](#policies). A page
response looks like this:

```json
{
  "name": "Items",
  "titleKey": "items.title",
  "parameters": [],
  "widgets": [
    {
      "type": "table",
      "entity": {
        "name": "Item",
        "labelKey": "item.label",
        "displayField": "name",
        "recordsPath": "/api/apps/RecordsApp/entities/Item/records",
        "fields": [
          {
            "name": "name", "type": "text", "labelKey": null,
            "required": true, "unique": false, "computed": false, "sequence": false,
            "creator": false, "maxLength": 100, "minLength": null,
            "precision": null, "scale": null, "currency": null, "currencyField": null, "min": null, "max": null, "values": null, "valueLabelKeys": null, "target": null, "fields": null
          },
          {
            "name": "department", "type": "reference", "labelKey": "item.department",
            "required": false, "unique": false, "computed": false, "sequence": false,
            "creator": false, "maxLength": null, "minLength": null, "precision": null,
            "scale": null, "currency": null, "currencyField": null, "min": null, "max": null, "values": null, "valueLabelKeys": null, "target": {
              "entity": "Department",
              "displayField": "name",
              "recordsPath": "/api/apps/RecordsApp/entities/Department/records"
            },
            "fields": null
          }
        ]
      },
      "dataSource": null,
      "form": null,
      "tasksPath": null,
      "process": null,
      "instancesPath": null,
      "deadLettersPath": null,
      "actions": [],
      "rowActions": [
        { "type": "navigate", "page": "ItemForm", "labelKey": "items.open", "parameters": {}, "record": "id", "opensForm": true, "paths": {}, "visibleWhen": null }
      ],
      "toolbarActions": [
        { "type": "navigate", "page": "ItemForm", "labelKey": "items.new", "parameters": {}, "record": null, "opensForm": true, "paths": {}, "visibleWhen": null }
      ],
      "parameters": {},
      "priorities": {},
      "access": {
        "actions": ["read", "create", "update", "delete"],
        "createFields": ["name", "department"],
        "updateFields": ["name", "department"]
      }
    }
  ]
}
```

### Widget keys

A leaf widget carries every key below, never left out. A key that does not apply to the widget is `null`, `[]` or
`{}`. The examples on this page leave out `detail`, `kpi` and `chart`, which are `null` on all of them.

- **`type`** is `table`, `form`, `taskInbox`, `processInspector`, `kpi`, `chart` or `detail`.
- **`entity`, `dataSource`, `form` and `access`** depend on the type:

  | Type               | `entity`                        | `dataSource`                      | `form`                       | `access` |
  | ------------------ | ------------------------------- | --------------------------------- | ---------------------------- | -------- |
  | `table`            | set, or `null` on a data source | set on a data source, else `null` | `null`                       | set      |
  | `form`             | set                             | `null`                            | its form resource, or `null` | set      |
  | `detail`           | set                             | `null`                            | its form resource, or `null` | set      |
  | `taskInbox`        | `null`                          | `null`                            | `null`                       | `null`   |
  | `processInspector` | `null`                          | `null`                            | `null`                       | `null`   |
  | `kpi`, `chart`     | `null`                          | set                               | `null`                       | `null`   |

  A table sets exactly one of `entity` and `dataSource`. The [data source binding](table-widget.md#data-source-binding) describes
  `dataSource`, and `form` is described under form metadata in the [form widget](form-widget.md).
- **`tasksPath`** is described under the [task inbox widget](task-inbox-widget.md), and **`process`, `instancesPath`
  and `deadLettersPath`** under the [process inspector widget](process-inspector-widget.md). They are `null` on every
  other widget.
- **`detail`, `kpi` and `chart`** are each described under their widget in
  [detail, chart and KPI widgets](detail-chart-kpi-widgets.md), and are `null` on every other widget. `detail` is
  written after `access`, `kpi` after `detail` and `chart` after `kpi`.
- **`actions`** is described under the [form widget](form-widget.md), and is `[]` on a widget it does not apply to, a
  `kpi` and a `chart` among them.
- **`rowActions` and `toolbarActions`** list a table's `navigate`, `startProcess` and `delete` row actions and its
  `navigate` toolbar actions, and are `[]` elsewhere. A `delete` is
  `{ "type": "delete", "labelKey": "…", "recordsPath": "…", "visibleWhen": null }`. `recordsPath` is the record API
  path of the table's entity, or of its data source's root entity, so the SPA never builds it.
- **`visibleWhen`** is on every action: the expression text of a row action's condition, or `null` when it has none.
  It is always `null` outside `rowActions`.
- **`parameters`** is, on a table, a chart or a KPI over a data source, the name of the page parameter that gives each
  data source parameter its value, as `{ "<data source parameter>": "<page parameter>" }`, in the declared names. It
  is `{}` elsewhere and on a widget that binds none.
- **`priorities`** is `high` or `low` per column of a table, by declared name, and `{}` elsewhere. It omits unreadable fields.
- **`access`** is described under [access](#access).

A page also has `parameters`, never left out: the typed values it reads from its URL, in file order, or `[]`. Each is
`{ "name", "type", "required", "values", "target" }`. `type` is the type name as page files write it. `values` is the
list for an `enum` and `null` otherwise. `target` is `{ "entity", "displayField", "recordsPath" }` for a `reference`,
as a data source parameter has it, and `null` otherwise. The SPA reads them to check the values in the address.

### Widget trees

A page may hold a tree of widgets. A container widget carries only `type` and its own keys, and a leaf widget keeps
every key above. A tab carries `labelKey`, and a `section` carries `titleKey` and `collapsed`. This page has two
columns, one holding a table and the other a section with a task inbox:

```json
{
  "name": "Overview",
  "titleKey": "pages.overview.title",
  "widgets": [
    {
      "type": "columns",
      "columns": [
        { "width": 8, "widgets": [{ "type": "table", "entity": { "name": "PurchaseRequest", "fields": [{ "name": "title", "type": "text" }] }, "dataSource": null, "form": null, "tasksPath": null, "process": null, "instancesPath": null, "deadLettersPath": null, "actions": [], "rowActions": [], "toolbarActions": [], "parameters": {}, "priorities": {}, "access": { "actions": ["read"], "createFields": [], "updateFields": [] } }] },
        {
          "width": 4,
          "widgets": [
            {
              "type": "section",
              "titleKey": "pages.overview.tasks",
              "collapsed": false,
              "widgets": [{ "type": "taskInbox", "entity": null, "dataSource": null, "form": null, "tasksPath": "/api/apps/PurchaseRequests/tasks", "process": null, "instancesPath": null, "deadLettersPath": null, "actions": [], "rowActions": [], "toolbarActions": [], "parameters": {}, "priorities": {}, "access": null }]
            }
          ]
        }
      ]
    }
  ]
}
```

### Entity and fields

`entity` describes the widget's entity: its name, its label key, its display field, the path of its record API in
`recordsPath`, and its fields in declaration order. Names in responses, the page, the entity and the application in
`recordsPath`, are the model's declared names, not the letter case of the request.

- **Listed fields.** A `child-collection` field and a `references` field are listed too. Tables show no column for
  either. Forms show a child collection's rows and a `references` field's records as a list, and a detail shows a
  `references` field's labels.
- **A field's keys.** Each field has its name, its `type` as written in entity files (such as `date-time`), its label
  key, `required`, `unique`, `computed`, `sequence` and `creator`, `maxLength` and `minLength` for text, `min` and `max`
  for integer, decimal, date and date-time, `precision` and `scale` for decimal and money, `currency` (the fixed code)
  or `currencyField` (the field holding the code) for money, `values` for enum, `valueLabelKeys` for an enum with
  `valueLabels`, `maxSize`, `accept`, `uploadPath` and `downloadPath` for file, `usersPath` for a text field marked
  `userId`, `target` for reference and references, and `fields` for child collection. A property the field's type
  does not have, or a label the file leaves out, is `null` and never left out.
- **`min` and `max`** are strings in the entity file's literal form, such as `"0.5"` or `"2026-01-01"`.
- **`valueLabelKeys`** maps each enum value to the text key of its label, and is `null` when the enum has no labels.
- **File fields.** `maxSize` (bytes) and `accept` (media types) are the entity file's, or `null` when it declares
  none. `uploadPath` is the path of the field's [upload route](file-fields.md#upload), and for a field of a child
  entity it names the collection, such as `/api/apps/{app}/entities/{entity}/fields/attachments/file/files`. It is
  `null` for a field of a module's entity, which has no upload route, so the input shows no upload button.
  `downloadPath` is `/api/apps/{app}/files`, and the SPA appends a file's id to it. The SPA never builds a file URL
  itself.
- **Values the server sets.** `computed` is `true` for a [computed field](entity-logic-and-sequences.md#entity-logic), whose value
  the server sets on every write. `sequence` is `true` for a text field that names a
  [sequence](entity-logic-and-sequences.md#sequences), which the server numbers on create. `creator` is `true` for a text field
  that declares [`creator`](roles-and-policies.md#record-creator), which the server sets to the creating user's id on
  create. A form shows each of them read-only and never sends it.
- **`target`** names the referenced entity, its display field and the path of its record API, so the SPA never builds
  a record URL itself. It is only for a reference and a references field. A child collection's `target` is `null`,
  because a child entity has no record API of its own.
- **`fields`** lists a child collection's child entity fields in declaration order, each in the same shape as an
  entity field. The form builds the columns and inputs of the collection's table from them.

## Policies

Policies shape the site and page metadata, as [authorization](security.md#authorization) describes. This only shapes
the UI, and the server checks every request. The endpoints need a session, so the metadata is always shaped by the
user's grants.

- **Readable widgets.** An unreadable widget is left out of the page. A widget is readable when the user holds what
  its type needs:

  | Widget                                                      | Readable when the user                                                                      |
  | ----------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
  | over an entity, or over a form of an entity, a `detail` too | holds a `read` grant on that entity                                                         |
  | over a data source: a `table`, a `kpi` or a `chart`         | may run the data source, as the [data source endpoint](data-sources.md#authorization) needs |
  | `taskInbox`                                                 | always                                                                                      |
  | `processInspector`                                          | holds an `inspect` grant on its process                                                     |

- **Pages with a tree.** A column or tab whose widgets are all left out is left out, and the other columns keep their
  declared widths. A container left with no widget is left out. A page is readable when at least one of its widgets
  is.
- **Unreadable pages.** The page endpoint answers `403` for a page the user cannot read. Site navigation leaves out
  entries whose page the user cannot read.
- **Fields.** `entity.fields` and the form metadata leave out the fields the user may not read, and a form section
  with no field left is left out. A child collection field covers its rows as a whole, so it is left out, or kept with
  all its `fields`. `id` and `version` are not affected. The task API's form does the same, see
  [policies on tasks](task-api.md). A `detail` leaves out unreadable fields as its form metadata does, and an empty
  section too. A `kpi` or `chart` measure over a field the user may not read follows the data source's field check,
  which leaves the widget out.
- **Actions.** A `startProcess` action is left out of `actions`, and of a table's `rowActions`, when the user has no
  `start` grant on its process. A table's `delete` row action is left out when the user has no `delete` grant on the
  table's entity, or on the data source's root entity, which is what `access.actions` lacks. A `navigate` action whose
  target page the user cannot read is left out, and a `kpi`'s `navigate` to such a page is `null`. A `navigate`
  action whose parameters or `record` use a path is also left out when the user may not read a field along the path,
  the first field on the widget's entity included, or the entity that field belongs to. This is the data source field
  check applied to a path.

### Access

Every `table`, `form` and `detail` widget gets an `access` object, which says which entity actions the user may take
and which fields each write action covers. A `taskInbox`, a `processInspector`, a `kpi` and a `chart` have
`"access": null`.

```json
"access": {
  "actions": ["read", "update"],
  "createFields": [],
  "updateFields": ["title", "department", "supplier", "currency", "lineItems", "status"]
}
```

- **`actions`** lists the entity actions of the user's grants on the widget's entity, in the order `read`, `create`,
  `update`, `delete`. On a data source widget it is about the root entity. Record filters are not evaluated here, so
  `update` means that some record may be updated, not this one.
- **Export and import.** A `table` adds `export` after them when the user may export its data source, or its entity
  when it has no data source (`PolicyEvaluator.AllowsExport`). A `table` over an entity adds `import` last when the
  user holds `import` and `create` on the entity. A form and a detail never list either, and a table over a data
  source never lists `import`. Without a user, as in Studio's draft metadata, every table lists `export` and every
  table over an entity lists `import`.
- **`createFields` and `updateFields`** list the fields the user's `create` and `update` grants cover, in declaration
  order. Each is `[]` when the action is not in `actions`.
- **On a form.** A form shows a field it may not write as read-only, as it shows a computed field, and never sends it.
  On a new record it writes the `createFields`, and on a saved record the `updateFields`. A form has no save button
  without the action it needs, `create` on a new record and `update` on a saved one. Cancel and the start process
  buttons stay.
- **On a table.** A toolbar action to a form without `record` shows only with `create`.
- **Bulk actions and inline edit** _(planned for M42)_. A table's metadata lists only the
  [bulk actions](table-widget.md#bulk-actions) the person may run. It also says whether the table has
  [inline edit](table-widget.md#inline-edit). A cell is editable only for a field in `updateFields`.

## Routing

The SPA routes on the client with `react-router` v7. The server answers every other path with the SPA, so each address
below answers `200`.

- **A data router.** The SPA uses `createBrowserRouter`, so `useBlocker` guards Back and Forward with
  [Leave without saving?](studio.md#editor).
- **Scroll restoration.** `<ScrollRestoration>` returns a person to where they were when they go Back or Forward, and
  a new address opens at the top. A change that stays on the same page keeps the position: a table's search, filter,
  sort, page or page size, or a tab. Pages load their rows after they open, so after Back the page keeps the height it
  had until its content is that tall again, and the position still fits.

| Address                                | Shows                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `/`                                    | the [platform home page](shell.md#platform-home-page): signed in, the launcher of the person's applications and tools, and with nobody signed in, sign-in at once with no screen drawn first, except at the addresses of [the sign-in card](shell.md#the-sign-in-card)                                                                                                                                                                                              |
| `/licences`                            | the [open source licences](legal.md#open-source-licences) page, open without signing in                                                                                                                                                                                                                                                                                                                                                                             |
| `/audit` _(planned for M41)_ | the [audit page](platform-pages.md#audit-page), for an administrator |
| `/recycle-bin` _(planned for M41)_ | the [recycle bin page](platform-pages.md#recycle-bin-page), for anyone signed in |
| `/{site}`                              | the site's first navigation entry, by redirect                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `/{site}/{page}`                       | the page inside the site shell, with the page title. A table page also takes `?search=&page=&pageSize=&sort=`. A table bound to a data source also takes its parameter values. A page with `tabs` widgets also takes `?tab=`, `?tab2=` and so on. A page of a module is `/{site}/{module id}/{name}`, listed in the navigation as `{module id}/{name}`, with `/new` and `/{id}` under it for a form page. A second segment that is no record id names a module page |
| `/{site}/{page}?ticket=<id>`           | a page with parameters: the values sit in the query under the parameter names                                                                                                                                                                                                                                                                                                                                                                                       |
| `/{site}/{page}/new`                   | the form of a form page, to create a record                                                                                                                                                                                                                                                                                                                                                                                                                         |
| `/{site}/{page}/{id}`                  | the form of a form page, to edit the record with that id                                                                                                                                                                                                                                                                                                                                                                                                            |
| `/{site}/records/{entity}/{id}` _(planned)_ | the default record page of an entity that the site names no record page for, shown as the form of the record |
| `/{site}/{inboxpage}/{taskId}`         | the task page of the task with that id, under the page of a task inbox                                                                                                                                                                                                                                                                                                                                                                                              |
| `/{site}/{inspectorpage}/{instanceId}` | the instance page of the process instance with that id, under the page of a process inspector                                                                                                                                                                                                                                                                                                                                                                       |

- **A site's home page** _(planned for M42)_. `/{site}` redirects to the person's home page, see [layout](shell.md#layout).
- **The home page's cards.** `/?signIn=failed`, `/?signIn=declined` and `/?signedOut` show a card in place of
  sign-in, see [the sign-in card](shell.md#the-sign-in-card).
- **Reserved paths.** The sessions screen at `/account` and the users page at `/users` with a user's page at
  `/users/{id}` are platform routes beside `/studio`, before the site route, so `account`, `users` and `licences` are reserved site paths. See
  [platform pages](platform-pages.md).
- **History and recovery paths** _(planned for M41)_. The audit page at `/audit` and the recycle bin page at
  `/recycle-bin` are platform routes too, so `audit` and `recycle-bin` are reserved site paths.
- **One owner for each part of the address** _(planned for M42)_. A page may hold several `table` widgets, each
  owning its part of the address under its `prefix`, see [address prefix](table-widget.md#address-prefix). A `calendar`,
  a `board` and `cards` own their keys under a `prefix` in the same way. A table, calendar, board or cards widget
  without a prefix, at most one, owns the plain keys: a table owns `?search=&page=&pageSize=&sort=` and the data source
  parameter values, a calendar owns `?month=`, a board owns `?search=&filter=` and cards own
  `?search=&filter=&page=&pageSize=&sort=`, see [calendar, board, cards and tree widgets](calendar-board-card-tree-widgets.md). A page holds at most one `form`,
  `taskInbox` or `processInspector`, and the compiler checks this. `/new` and `/{id}` belong to its `form`.
  `/{taskId}` and `/{instanceId}` belong to its `taskInbox` or `processInspector`. The instance list of a
  `processInspector` and the task list of a `taskInbox` also read `search`, `filter`, `columns`, `sort`, `page` and
  `pageSize`. A `kpi`, a `chart`, a `content` widget and a `tree` own no part of the address and read only
  page parameters, so they do not count. A `detail` owns no part of the address, except `/{id}` on a page that a site
  names as a record page, as [a detail as a record page](#routing) says.
- **Letter case.** `{site}` is the site path, and `{page}` is the page name in lower case, as navigation links write
  it. The server matches both ignoring letter case.
- **Page parameters.** A required page parameter that is missing from the URL, or has the wrong form, shows the
  not-found page inside the site shell. The SPA checks this on every page that declares parameters, before any widget
  draws, so no rows are requested. A value is missing when it is absent or empty, and has the wrong form when it is
  repeated or not in the plain text form of its type in [data sources](data-sources.md#query-parameters). An optional
  parameter with a missing or malformed value counts as not given.
- **Not found.** An unknown site shows the not-found page inside the platform shell, and an unknown page inside the
  site shell. An address with more than three segments, such as `/e2e/notes/42/edit`, matches no route and shows the
  not-found page inside the platform shell.
- **Record and task ids.** A form page without `new` or an id, `new` or an id on a table page, an id that is not in
  the hyphenated 8-4-4-4-12 hex form, and an id the entity has no record for show the not-found page inside the site
  shell. `new` on a task inbox page, a task id that is not in that form, and a well-formed task id that the task API
  answers `404` for show it too. An id or a task id in the wrong form is not requested.
- **A missing record** _(planned)_. A form's or a detail's page that names a well-formed id the record API answers
  `404` for, as it answers for a record the person cannot open, keeps its heading. Under it, one line of body text
  with no illustration: "This record does not exist, or you cannot open it." ("Bản ghi này không tồn tại hoặc bạn
  không có quyền mở.", `shell.record.notFound`). On the same line, one link "Go to {page}" ("Đi tới {page}",
  `shell.record.goToList`), where `{page}` is the label of that list's navigation entry. The link opens the address the
  page was opened from, with its table's search, sort and page, or else `/{site}`, which opens the first page. No save
  bar and no record action shows. Such a page is mostly opened from an email, a bookmark or a shared link, so no entry
  is marked, and at narrow width the sidebar is hidden, so the link stays.
- **The default record page** _(planned)_. `/{site}/records/{entity}/{id}` shows the entity's form over every field, in
  declaration order, as an `entity` form does. It has the heading, the save bar, the history and a missing record's
  line, as any record's page does, and the person's write grants decide which fields are read-only. A module entity's
  address is `/{site}/records/{module id}/{name}/{id}`. The not-found rule above does not catch these two addresses,
  because each matches its own route. A reference opens its entity's record page, with the record's id after the
  page's address, see [opening things](actions-and-controls.md#opening-things).
- **A detail as a record page** _(planned)_. A detail page that a site names as a record page opens its record at
  `/{site}/{page}/{id}`, and that segment fills the detail's `record` parameter. The page's other parameters still sit
  in the query, as on any page with parameters.
- **Inside a site.** The shell shows the site's title, navigation and locales in the [layout](shell.md#layout) of
  [shell and home page](shell.md). The page header, the app switcher and the language and theme menu work as they do
  on the platform site, see [page header](shell.md#page-header) and
  [app switcher and preferences](shell.md#app-switcher-and-preferences).

## Table views and columns

A person's columns and saved views of a table are kept on the server for that person, so they come back on any
browser. The address still wins, and [D153](../decisions/d153-a-persons-table-views-and-columns-are-kept-on-the-server-and-the-address-still-wins.md)
gives the reasons. The rules that a table follows are in [actions and controls](actions-and-controls.md#tables).

| Route                                                        | What it does                                                                                                                              |
| ------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------- |
| `GET /api/me/tables/{key}` | Answers `{ "columns": [..] or null, "views": [{ "id", "name", "search", "filter", "sort" }] }`, with the views ordered by name           |
| `PUT /api/me/tables/{key}/columns` | Keeps the columns of `{ "columns": [..] }`, the keys of the shown columns in order, and answers `204`                                    |
| `DELETE /api/me/tables/{key}/columns` | Forgets the kept columns and answers `204`                                                                                                |
| `POST /api/me/tables/{key}/views` | Saves a view of `{ name, search, filter, sort }` and answers `201` with the view                                                          |
| `PUT /api/me/tables/{key}/views/{id}` | Replaces all four fields of one view and answers `204`                                                                                    |
| `DELETE /api/me/tables/{key}/views/{id}` | Deletes one view and answers `204`                                                                                                        |

- **The table key.** A key is 1 to 200 characters, made of segments of letters, digits, `-` and `_` joined by `.`,
  and it is compared exactly. A `table` widget's key is `site.{site path}.{page name}` in lower case, with the `/` of
  a module page written as `.`. The `taskInbox` and `processInspector` lists of a page use the page's key too. A
  platform table's key is a fixed name for its screen, such as `console.deadLetters`,
  `studio.{application}.history` or `studio.{application}.{kind}`. A platform page with a second table gives that
  table a key of its own. A key in any other form answers `400`.
- **A prefixed table's key** _(planned for M42)_. A table with a `prefix` has the key `site.{site path}.{page name}.{prefix}`,
  in lower case.
- **The answer.** Every route needs a session, see [sign-in needed](security.md#sign-in-needed), and answers `401`
  without one. The `GET` route never answers `404` for a key with nothing kept. It answers `columns: null` and
  `views: []` instead, so the SPA needs one call when a table opens.
- **The bodies.** A `PUT` of columns answers `400` for more than 100 keys, an empty key or a repeated key. A view's
  `search`, `filter` and `sort` are the address's values as they are, a string or null, and the SPA checks them as it
  checks the address.
- **The limits.** A view's name is trimmed and is 1 to 80 characters, and a person has at most 50 views for each
  table. `search`, `filter` and `sort` together are at most 8,192 bytes of UTF-8. A body past a limit answers `400`.
  A name that another of the person's views of the table already has, ignoring letter case, answers `409`, and so
  does a 51st view.
- **Not found.** An id that is not one of the person's own views for that key answers `404`, even when another
  person's view has that id.
- **Writes.** Every write needs the [CSRF header](security.md#csrf-header). No write is refused over the `storage`
  quota, see [refusals](tenant-quotas.md#refusals).
- **A site's own host.** There the routes serve only the keys that start with `site.{that site's path}.`. Any other
  key answers a `404` problem before authentication, see [a host of its own](identity-provider.md#sites-and-sign-in).

## Widgets

A page draws its whole tree of widgets. Each widget type has one shared component and a page of its own, and the
[container widgets](container-widgets.md) `columns`, `tabs` and `section` hold other widgets, show them in file order
and can nest.

| Widget                       | What it does                                                                                                                      | Reference                                                    |
| ---------------------------- | --------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------ |
| `table`                      | lists the records of an entity, or the rows of a [data source](data-sources.md)                                                   | [Table widget](table-widget.md)                              |
| `form`                       | creates and edits one record through the record API, laid out by a `form` resource or the entity                                  | [Form widget](form-widget.md)                                |
| `detail`                     | shows one record read-only                                                                                                        | [Detail, chart and KPI widgets](detail-chart-kpi-widgets.md) |
| `chart`                      | draws the groups of a data source                                                                                                 | [Detail, chart and KPI widgets](detail-chart-kpi-widgets.md) |
| `kpi`                        | shows one value of a data source                                                                                                  | [Detail, chart and KPI widgets](detail-chart-kpi-widgets.md) |
| `taskInbox`                  | lists the signed-in user's open tasks, and opens one on the task page `/{site}/{inboxpage}/{taskId}`                              | [Task inbox widget](task-inbox-widget.md)                    |
| `processInspector`           | lists the instances of one process and opens one                                                                                  | [Process inspector widget](process-inspector-widget.md)      |
| `content` _(planned for M42)_ | shows fixed text, an image, a link or a note                                                                                     | [Content widget](content-widget.md)                          |
| `calendar`, `board`, `cards`, `tree` _(planned for M42)_ | show an entity's records by a date, in groups, as tiles or under their parent                                       | [Calendar, board, cards and tree widgets](calendar-board-card-tree-widgets.md) |
| `columns`, `tabs`, `section` | hold other widgets: `columns` stack at narrow width, `tabs` keep the selected tab in `?tab=`, and a `section` can start collapsed | [Container widgets](container-widgets.md)                    |

A widget opens another page, and sets its parameters, through a `navigate` action. A table opens and creates records
through its [navigate actions](table-widget.md#navigate).

## Printing

_(planned for M40)_

A record page prints on paper, with a layout made for it, so a person can print a purchase request or a ticket
without a screenshot. This section says what prints and what does not. A report as a PDF is in
[reports as PDF](document-templates.md#reports-as-pdf), and the document of one record is in the
[page download](document-templates.md#page-download).

- **What prints.** The page's content: the title of the page and the widgets of its regions in reading order,
  with a `detail` or a `form` laid out in one column and a `table` with the rows that it shows. A `columns` or
  a `tabs` widget prints all of its panels, one after another, so a tab that is not selected is not lost.
- **What hides.** The shell, which means the header, the sidebar and the bell, the actions and buttons of every
  widget, the paging and the filters of a table, and a toast. What a person cannot use on paper is not drawn.
- **Colours and breaks.** The print layout draws on white with dark text, whatever the colour mode of the screen.
  A table header repeats on each sheet, and a row or a section does not split across two sheets when it fits on
  one.
- **The action.** A page with a `detail` or `form` widget shows a Print… action. It is a Secondary
  [button](actions-and-controls.md#buttons), and it ends in an ellipsis because it opens the browser's print
  dialog. A user without the grant to read the page sees no Print action, as for any page.
- **Text.** The words of the action and of the page's footer, such as the date and time of printing, come from
  the platform's texts in the user's locale, and name the concept as the [glossary](ui-glossary.json) does.

## Live pages

_(planned for M43)_

Some pages change while a person has them open, such as the bell, the task inbox and a list of records. The server
tells the open page when the data changes, and the page reloads through its usual routes.

| Route | What it does |
| --- | --- |
| `GET /api/live` _(planned for M43)_ | Opens the live event stream of the signed-in session |

- **Stream.** The SPA opens one stream for each browser tab. It uses server-sent events on the person's session, which
  go one way, from the server to the page.
- **Events.** An event is `{ "kind": "records" | "tasks" | "notifications", "application": "...", "entity": "..." }`.
  It names what changed and never carries an id or a value.
- **Who gets an event.** The server sends an event only when the session's roles give a `read` grant on the entity,
  or when the person, or one of their roles, is addressed by the change.
- **Reloads.** The bell, the task inbox and the open tables of the entity reload through their usual routes. The
  [notifications](notifications.md#the-bell), the [task inbox](task-inbox-widget.md) and the
  [table widget](table-widget.md) say which routes those are.
- **Reconnect.** When a stream breaks and comes back, the page reloads its data once, since it may have missed events.
- **Replicas.** A change on one replica reaches the others through `NOTIFY axis_live` on the platform database. The
  server and the worker send it after the commit, as [waking a tenant](worker-scheduling.md#waking-a-tenant) does. The
  signal may name the user or role that a task or a notification is for, so only their sessions reload. Each server
  holds one `LISTEN` connection, see [connections](cells.md#connections).
- **Without a platform database.** With `Tenancy:Source=configuration`, events come only from changes on the same
  replica.
- **Limits.** Opening a stream counts once in the session's rate limit. The streams that a session and a replica hold
  are capped, see [rate limits](headers-and-rate-limits.md#rate-limits).

## Account button and sign-in prompt

The account button and the sign-in prompt are parts of the shell, see [account button](shell.md#account-button) and
[session ended](shell.md#session-ended).

## Narrow width

Axis has one layout breakpoint, the `narrowWidth` token in `breakpointTokens` of `web/src/platform/designTokens.ts`.
It is Ant Design's `lg`, the smallest desktop, so every desktop gets the desktop layout and everything smaller the
narrow one.

- **What changes.** Below the breakpoint is narrow width: columns stack in file order, tab headers scroll, a row's
  actions fold into one menu and the shell navigation collapses. A widget has no setting of its own for it.
- **One value.** The narrow-width hook reads the token, and each card's marker in the Design System artifact sets that
  width, see [widget previews](widget-previews.md). Studio's page designer previews the narrow layout,
  see [Studio](studio.md).
- **Tables.** Every platform table, form line items and the reference lookup included, scrolls sideways inside its
  widget, so the page never does.
- **The shell header** folds the navigation and the language and theme menu into a Menu panel, see
  [layout](shell.md#layout).
- **Side panels** _(planned for M35)_. At narrow width a side panel takes the window's width, see [layout](shell.md#layout).

## Platform field help

Every field of the platform's own screens has help text, enforced three ways, because in an application
builder help is not optional.

- **`AxisField` requires `help`,** so a screen cannot leave it out.
- **A test** fails when a `shell.studio.field.*` text has no `.help` in every locale. Texts under that prefix
  that are not fields, such as `modules.addTitle`, move out of it.
- **Every platform field label is drawn through `AxisField` or `AxisFieldGroup`**, the connector and process editors included.
- **Lint rule `axis/field-label-through-axis-field` refuses a hand-drawn label.** [Shared widgets](shared-widgets.md#what-the-build-checks) lists where it is off.

An application's own forms keep `help` optional, as their author decides.

## Studio

The Studio page is in [studio.md](studio.md).
