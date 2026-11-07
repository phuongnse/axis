# D88. Custom widgets in the designer palette and a published SDK

Status: **Agreed**

## Context

[D8](d8-custom-ui-widgets-are-packaged-react-components.md) agreed that a custom widget is a React
component in an extension package ([D28](d28-extension-packages.md)), with no design. Package authors
need a manifest, a way for the SPA to load their code under the content security policy, an SDK to
build on, and a place in the page designer.

## Decision

- **Widgets in a package.** A package's manifest lists its widgets beside its operations, each with
  an entry module, a label, typed properties and the SDK range it was built for. A package may hold
  only widgets, and custom widgets are page widgets only.
- **Checked like any reference.** The application declares the package, compilation checks a page's
  custom widget against the manifest of the pinned version, and properties that name a resource join
  the reference index.
- **Loaded through an import map.** The server serves each installed version's files read-only and
  cacheable, and writes one import map into the SPA's page at start. The map pins each entry to its
  content hash and gives every widget the SPA's own React and SDK. The content security policy of
  [D85](d85-rate-limits-and-security-headers.md) allows that map, and no other inline script, so a
  widget runs no `eval` and no inline script.
- **Not a sandbox.** A widget runs in the SPA's origin with the user's session, through SDK hooks
  that call the platform APIs. It can do anything the user may do and never more, because the server
  enforces every grant. "Only through platform APIs" in D8 binds package authors, and the platform
  does not enforce it. The operator installs and reviews packages, and authors never upload code.
- **In the page designer.** The palette lists the widgets of the declared, installed packages, the
  property panel is built from their properties, and the canvas draws them in preview mode, as
  [D65](d65-a-visual-page-designer-on-a-canvas-drawn-by-the-sites-widgets.md) draws the standard
  widgets.
- **A published 0.x SDK.** `Axis.Extensions` on NuGet and `@axis/widgets-sdk` on npm are 0.x, with
  their own SemVer and no compatibility promise until the first production release. A host leaves out
  a package version whose SDK range it does not meet.

The detail is in [custom widgets](../reference/custom-widgets.md) and
[extension packages](../reference/extension-packages.md).

## Consequences

- Custom code is versioned, pinned and reviewed one way, as packages are.
- A custom widget gets the theme, overlays, focus and screen readers of the standard widgets.
- A custom widget is trusted code that can act as the user without asking.
- The SDK's contract can change while the platform keeps one format
  ([D46](d46-an-enterprise-application-platform-for-any-domain-and-the-milestones-after-m9.md)).

## Alternatives

- **A sandboxed frame.** It would isolate the widget, but break theming, overlays, focus and screen
  readers.
- **A compatible SDK from the start.** It would add compatibility code before the first production
  release.
