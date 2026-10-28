# Custom widgets

Detailed reference for custom widgets: React components that an extension package adds to the
page widgets, how a page names one, how the SPA loads it, the SDK it builds on and how the page
designer offers it. Everything in this file is _(planned for M22)_. The design follows
[D8](../decisions/d8-custom-ui-widgets-are-packaged-react-components.md) and
[D88](../decisions/d88-custom-widgets-in-the-designer-palette-and-a-published-sdk.md). How a
package is installed, versioned and pinned is in [extension packages](extension-packages.md).
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

Custom widgets should stay rare. Each one is a sign that a standard widget is missing something,
see [knowledge](../domain/knowledge.md#user-interface).

## Widgets in a package manifest

A package lists its widgets in `axis-package.json`, next to its operations. A package may hold
only widgets, and then it has no `assembly`. This example uses an illustrative package,
`asset-maps`:

```json
{
  "id": "asset-maps",
  "version": "0.3.0",
  "widgets": [
    {
      "name": "floorPlan",
      "entry": "widgets/floor-plan.js",
      "label": { "en": "Floor plan", "vi": "Sơ đồ tầng" },
      "sdk": "^0.4.0",
      "properties": [
        { "name": "dataSource", "type": "dataSource", "required": true },
        { "name": "roomField", "type": "field" },
        { "name": "title", "type": "textKey" },
        { "name": "showLegend", "type": "boolean" }
      ]
    }
  ]
}
```

- **`name`.** Unique in the package, ignoring letter case. A page names the widget as
  `<package>/<name>`.
- **`entry`.** An ES module in the package folder. Its default export is the React component.
- **`label`.** The name the palette shows, per locale. `en` is required. Studio shows the
  author's locale, or `en` when the label lacks it.
- **`sdk`.** The range of `@axis/widgets-sdk` versions the widget was built for, in the range
  forms of [versions and pinning](extension-packages.md#versions-and-pinning). A host whose SDK
  does not meet the range leaves the package version out and logs a load diagnostic, as for a
  broken manifest.
- **`properties`.** Typed properties that a page sets. The types are:

  | Type | Value on the page | Checked at compile |
  | --- | --- | --- |
  | `text` | a string | no |
  | `textKey` | `{ "textKey": "<key>" }` | the key exists in every locale |
  | `number` | a number | no |
  | `boolean` | `true` or `false` | no |
  | `entity` | an entity name | it names an entity of the application or of a declared module |
  | `dataSource` | a data source name | it names a data source |
  | `field` | a field name | it names a field of the widget's `entity` or `dataSource` property |
  | `page` | a page name | it names a page |

  A property may be `required`. `entity`, `dataSource` and `page` values are references, so the
  [reference index](../decisions/d39-reference-index-and-where-a-resource-is-used.md) lists
  them.

## On a page

```json
{
  "type": "custom",
  "widget": "asset-maps/floorPlan",
  "properties": {
    "dataSource": "AssetsByRoom",
    "roomField": "room",
    "title": { "textKey": "pages.floorPlan.title" },
    "showLegend": true
  }
}
```

- The application declares the package in its
  [`packages`](extension-packages.md#declaring-packages-in-an-application). The release pins the
  version, as for operations.
- Compilation checks the widget and its properties against the manifest of the pinned version.
  An unknown package or widget, a property the manifest does not declare, a missing required
  property and a value of the wrong type are each a new diagnostic, reported at the property.
- A custom widget sits anywhere a page widget may, including inside the container widgets.
- Custom widgets are page widgets only. Custom inputs for form fields come later.
- Page metadata leaves out a custom widget whose `dataSource` or `entity` property the user may
  not read, as it does for a standard widget.

## Loading in the SPA

- **Served files.** The server serves each installed package version's files under
  `/extensions/<package>/<version>/`, read-only, with long-lived caching. A version's files
  never change, so a cached copy is never stale. The server serves only the files under the
  folder of the widget's `entry`.
- **Page metadata.** Each custom widget in page metadata carries its entry URL, the content hash
  of the entry and its checked properties.
- **Import map.** The server writes one import map into the SPA's page when it starts. It has an
  entry for the entry URL of every installed widget, with the content hash as its integrity, and
  gives `react`, `react-dom` and `@axis/widgets-sdk` from the SPA's own bundle, so every widget
  shares one React. A widget bundle keeps these three external. Installed packages change only
  with a restart, so the map does not change while the host runs. An import map is an inline
  script, so the `script-src` of the
  [security headers](headers-and-rate-limits.md#security-headers) lists the map's `sha256` hash.
  No other inline script is allowed.
- **No Ant Design.** A widget builds on the SDK's Axis widgets and theme tokens and never imports
  Ant Design, as [D58](../decisions/d58-ant-design-is-a-detail-of-the-spa-without-procomponents.md)
  says.
- **No inline code.** A widget runs no `eval` and adds no inline script, so it works under the
  content security policy that M16 adds.
- **A widget that fails.** A widget whose entry does not load, or that throws while it draws,
  shows an error box in its place. The rest of the page still draws.

## The SDK

`@axis/widgets-sdk` is the npm package a widget builds on. `Axis.Extensions` is the NuGet package
an operation builds on, see [contracts](extension-packages.md#contracts).

- **Hooks.** Each one calls a platform API that the SPA already uses, so the server checks the
  user's grants as for any standard widget:
  - `useDataSource(name, parameters)` reads the rows of a data source;
  - `useRecord(entity, id)` reads one record;
  - `useText(key)` gives a text in the user's locale;
  - `useTokens()` gives the theme tokens of the current mode;
  - `runAction(action)` runs a page action, such as navigate or `startProcess`.
- **Props.** The component gets its checked `properties`, and `preview`, which is `true` on the
  designer's canvas.
- **Components.** The SDK exports the Axis widgets a custom widget may compose, such as the
  table and the detail, drawn with the site's theme.
- **Versions.** Both SDK packages are published to the public npm and NuGet registries. They are
  0.x versions with their own SemVer, apart from the platform version, and make no compatibility
  promise until the first production release.
- **Template and harness.** A package template and a test harness come with the SDK. The harness
  draws a widget with the fixtures of the
  [design system's widget previews](../decisions/d67-live-widget-previews-in-the-design-system.md),
  and gives operations a fake operation context.

## Designer palette

- **Listing.** A new authoring route lists the widgets of the packages that the application
  declares and that are installed, with each widget's label and properties. The
  [page designer](studio.md)'s palette shows them after the standard widgets.
- **Property panel.** The panel is built from the widget's `properties`. `entity`, `dataSource`,
  `field` and `page` use the reference pickers, and `textKey` uses the label text editor.
- **Canvas.** The canvas draws the widget with `preview: true` and placeholder data made from
  the field types, as it draws the standard widgets. A widget that throws on the canvas shows the
  error box and stays selectable.
- **Undeclared package.** A widget of an installed package that the application does not declare
  is not listed. The author declares the package first.

## Not a sandbox

A custom widget runs in the SPA's origin with the user's session.

**What holds:**

- The server checks every request against the user's grants, so a widget never reads or changes
  more than the user may.
- The operator installs and reviews packages for the whole installation. Authors pick installed
  widgets and never upload code.
- The import map pins each entry to its content hash.

**What does not hold:**

- A widget can call any API the user may call, and act as the user without asking.
- It can read what the page shows and what the SPA holds in memory.
- "Only through platform APIs" is a rule for package authors, not a limit the platform enforces.
