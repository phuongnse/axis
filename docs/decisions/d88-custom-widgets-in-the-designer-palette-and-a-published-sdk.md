# D88. Custom widgets in the designer palette and a published SDK

Status: **Agreed**

The owner agreed this on 2026-10-25. M17 builds the custom widgets that
[D8](d8-custom-ui-widgets-are-packaged-react-components.md) agreed and
deferred, and publishes the SDK that package authors build on. D8 gave the
rules a custom widget follows but no design. This decision gives that design,
and D8 keeps its status line. It builds on
[D28](d28-extension-packages.md),
[D58](d58-ant-design-is-a-detail-of-the-spa-without-procomponents.md),
[D65](d65-a-visual-page-designer-on-a-canvas-drawn-by-the-sites-widgets.md)
and [D67](d67-live-widget-previews-in-the-design-system.md). Everything here is
_(planned for M17)_. See [custom widgets](../reference/custom-widgets.md).

- **A widget in a package:**
  - An extension package may hold widgets as well as operations. Its manifest,
    `axis-package.json`, gets a `widgets` list. Each widget has a `name`, an
    `entry` ES module in the package folder, a `label` per locale, typed
    `properties` and an `sdk` version range.
  - Property types are `text`, `textKey`, `number`, `boolean`, `entity`,
    `dataSource`, `field` and `page`. A property may be `required`.
  - `assembly` becomes optional, so a package may hold only widgets. A package
    with operations still names its assembly.
  - Custom widgets are page widgets only. Custom inputs for form fields come
    later.
- **On a page:**
  - A page names a custom widget as
    `{ "type": "custom", "widget": "<package>/<name>", "properties": { … } }`.
  - The application declares the package in `packages`, as for operations.
    Compilation checks the widget and its properties against the manifest of
    the pinned version, and the release pins it.
  - An unknown widget, a property the manifest does not declare, a missing
    required property, or a value of the wrong type is a new diagnostic.
    `entity`, `dataSource`, `field` and `page` values are checked as
    references, so the reference index lists them.
- **Loading in the SPA:**
  - The server serves each installed version's widget files read-only under
    `/extensions/<package>/<version>/`, with long-lived caching, because a
    version's files never change.
  - Page metadata gives each custom widget its entry URL and the content hash
    of the entry. The SPA loads the entry through an import map entry that
    carries that hash as its integrity.
  - The import map gives the widget `react`, `react-dom` and
    `@axis/widgets-sdk` from the SPA, so there is one React. A widget bundle
    keeps them external and never imports Ant Design.
  - A widget runs no `eval` and no inline script, so it works under the
    security headers that M16 adds.
- **The SDK hooks:** `useDataSource`, `useRecord`, `useText`, `useTokens` and
  `runAction`. Each calls the platform APIs that the SPA already uses, so the
  server checks the user's grants as for any other widget.
- **Not a sandbox:**
  - A custom widget runs in the SPA's origin with the user's session. It can
    call any API the user may call, and do anything the user may do.
  - The server still enforces every grant, so a widget never reaches more
    than the user's grants. It is not stopped from acting as the user.
  - "Only through platform APIs" in D8 is a rule for package authors, not a
    limit the platform enforces.
  - The operator installs and reviews packages for the whole installation.
    Authors never upload code.
- **In the page designer:**
  - The palette lists the widgets of the packages that the application
    declares and that are installed, through a new authoring route.
  - The property panel is built from the widget's `properties`, with the
    reference pickers of
    [D52](d52-reference-pickers-label-texts-and-the-expression-editor.md).
  - The canvas draws the widget with `preview: true` and placeholder data made
    from field types, as D65 draws the standard widgets.
- **The published SDK:**
  - `Axis.Extensions` is published as a NuGet package and
    `@axis/widgets-sdk` as an npm package, to the public registries.
  - Both are 0.x versions with their own SemVer, apart from the platform
    version. They make no compatibility promise. The one-format rule of
    [D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)
    holds until the first production release.
  - A package declares the `sdk` range it was built for. An installed version
    whose range the host's SDK does not meet is a load diagnostic, and the host
    leaves the version out, as for a broken manifest.
  - A package template and a test harness come with the SDK: a fake operation
    context for operations, and a render harness for widgets that uses the D67
    fixtures.

_Why:_ D8 already agreed that a custom widget is a packaged React component.
Installing widgets the way packages are installed keeps one way to version,
pin and review custom code. A sandboxed frame would isolate the widget, but it
would break theming, overlays, focus and screen readers, which the standard
widgets get right. So the docs say plainly that a widget is trusted code, as
[D28](d28-extension-packages.md) says for packages. A 0.x SDK lets the contract
change while the platform has one format, instead of adding compatibility code
before the first production release.
