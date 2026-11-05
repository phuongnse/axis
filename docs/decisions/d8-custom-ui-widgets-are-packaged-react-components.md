# D8. Custom UI widgets are packaged React components

Status: **Agreed, deferred**

## Context

Some applications need a widget that the standard ones do not offer. Where the only answer is raw
HTML and JavaScript in configuration, theme, localization, security and upgrades break, as
[knowledge](../domain/knowledge.md#user-interface) records.

## Decision

- **A custom widget is a React and TypeScript component in an extension package.**
- **It builds on Axis widgets, theme tokens and text resources,** and never imports a UI library
  directly ([D58](d58-ant-design-is-a-detail-of-the-spa-without-procomponents.md)).
- **It declares a schema** for its properties and data bindings, and calls the backend only through
  the platform APIs.
- **Configuration holds no raw HTML or JavaScript.**

The standard widgets must be good enough that custom widgets stay rare, since each one signals a
missing capability. [D88](d88-custom-widgets-in-the-designer-palette-and-a-published-sdk.md) gives
the design, detailed in [custom widgets](../reference/custom-widgets.md).

## Consequences

- Custom code is versioned and reviewed with its package, and follows the theme and the locale.
- Writing a custom widget needs a developer.
