# D58. Ant Design is a detail of the SPA, without ProComponents

Status: **Agreed**

## Context

The SPA needs a component library. Published applications and extension packages outlive any version
of it, so a library name in configuration or in the custom widget API would let an upgrade break
them. Axis already renders UI from metadata with its own presentation runtime
([D2](d2-axis-owns-its-engines.md)).

## Decision

- **The SPA builds on Ant Design,** without ProComponents.
- **The library never shows outside the SPA.** Configuration, page metadata, configurable theme tokens
  and the custom widget API never name an Ant Design component, prop or token, and a custom widget
  ([D8](d8-custom-ui-widgets-are-packaged-react-components.md)) builds on Axis widgets and tokens.
- **A specialised library,** such as React Flow, is added when a widget needs one, under the same
  rule.
- **No schema renderer** on top of Ant Design.

The rules for the SPA's code are in [frontend](../reference/frontend.md).

## Consequences

- An upgrade or a replacement of the library stays inside the SPA.
- The SPA wraps every Ant Design component that a widget needs.

## Alternatives

- **ProComponents.** It adds a second release cycle, and its schema rendering overlaps with the Axis
  presentation runtime.
