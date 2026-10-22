# D58. Ant Design is a detail of the SPA, without ProComponents

Status: **Agreed**

The SPA builds on Ant Design and drops ProComponents. The UI library is a
detail of the SPA and never shows outside it. This replaces the frontend line
of [D3](d3-technology-stack.md). The owner agreed these points on issue #928
on 2026-10-22.

- **Base components:** the SPA builds on Ant Design for its base components.
  It does not use ProComponents.
- **A detail of the SPA:** configuration, page metadata, configurable theme
  tokens and the custom widget API never name an Ant Design component, prop or
  token.
- **Custom widgets:** a custom widget of
  [D8](d8-custom-ui-widgets-are-packaged-react-components.md) builds on Axis
  widgets and Axis tokens. It never imports the UI library directly. So a major
  Ant Design upgrade stays inside the SPA.
- **Specialised libraries:** they are added when a widget needs one, as React
  Flow and CodeMirror were. The same rule applies to them.
- **No schema renderer:** no layer on top of Ant Design renders UI from a
  schema. Rendering from metadata is the presentation runtime that Axis owns
  ([D2](d2-axis-owns-its-engines.md)).
- **Until it is removed:** the SPA still uses ProComponents for the shell and
  the page containers (`ProLayout`, `PageContainer` and `ProCard`). Its removal
  is _(planned for M12)_.

_Why:_ keeping the library out of configuration and out of the widget API means
that upgrading or replacing it never breaks a published application or a
package. ProComponents adds a second release cycle and its own schema
rendering, which overlaps with the Axis presentation runtime. The cost is that
the SPA wraps every Ant Design component that a widget needs.
