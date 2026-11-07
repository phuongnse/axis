# D67. Live widget previews in the design system

Status: **Agreed**

## Context

The Design System artifact of [D57](d57-a-design-system-published-from-the-repository.md) shows a
preview of each widget. A static rendition shows how a widget looks, but not its pickers, dialogs,
validation messages or hover states, and a token edited on the page changes nothing in it.

## Decision

- **One bundle.** The repository builds one classic script that sets `window.Axis`, with one mount per
  preview. It holds the shared widgets, their fixtures, React and Ant Design, and needs no server or
  sign-in. It carries its own React, because React 19 ships no UMD build for a page to provide.
- **Fixtures stay inside it.** They answer only the fixture application's API paths, and every other
  request goes to the page's own `fetch`.
- **Theme and tokens follow the page.** A mounted widget follows the page's `data-theme` and the Axis
  tokens in its CSS custom properties. A token the page leaves out keeps its value from the token
  file, which stays the only source.
- **One harness.** The repository's preview page mounts the same bundle the same way, and the
  end-to-end tests check every preview there.
- **Not the widget SDK.** The bundle previews the standard widgets. It is not the SDK of
  [D88](d88-custom-widgets-in-the-designer-palette-and-a-published-sdk.md), and gives configuration
  no new way to run code.

The detail is in [widget previews](../reference/widget-previews.md) and
[frontend](../reference/frontend.md).

## Consequences

- The owner tries a design change on the page before it becomes an issue, and a Design artifact can
  use the real widgets instead of drawing copies.
- The bundle carries React and Ant Design, its fixtures must not leak into other pages, and the local
  session builds it before every publish.

## Alternatives

- **Static HTML renditions.** They show neither the interactive states nor an edited token.
