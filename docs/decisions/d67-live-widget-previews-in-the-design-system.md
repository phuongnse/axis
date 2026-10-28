# D67. Live widget previews in the design system

Status: **Agreed**

The Design System artifact draws each widget live from one bundle that the
repository builds, in place of the static HTML files it shows today. The owner
agreed this on 2026-10-24. The
artifact then works the way its type expects: a person can click a widget's
inputs, open its pickers and dialogs, switch light and dark, and see a token
edited on the page in the widgets. This changes how
[D57](d57-a-design-system-published-from-the-repository.md) publishes the
widget previews. Everything else in D57 stays: the repository is the source of
truth, only the owner and Claude edit in Claude Design, a design change is a
NexKit issue, and NexKit's agents neither read nor write Claude Design.

- **One bundle:** the repository builds a design-system bundle, one classic
  script that sets `window.Axis`. It holds the shared widget components of
  `web/src/platform/`, their fixtures, React 19 and Ant Design. It needs no
  server, database or sign-in, like the preview page today. The Design System
  type caps a bundle at 6 MB; today's preview build, with the same contents,
  is 1.3 MB.
- **Its own React:** React 19 ships no UMD build, so the bundle carries React
  and mounts its widgets itself. It never uses a React that the page provides.
- **One mount per widget:** `window.Axis` has one entry per widget and field
  input in the preview list: `table`, `form`, `taskInbox`, `processInspector`,
  `columns`, `tabs`, `section`, and the input of each field type. Each preview
  in the artifact mounts one of them into its own element.
- **Fixtures stay inside the bundle:** the fixtures answer only the API paths
  of the fixture application. Every other request goes to the page's own
  `fetch`, unchanged, so a page that loads the bundle keeps working. Today the
  preview page replaces `window.fetch` for every request.
- **Theme follows the page:** a mounted widget draws in the mode the page sets
  with `data-theme`, and redraws when it changes.
- **Tokens follow the page:** at mount, and when the page's token values
  change, the bundle reads the Axis colour, type, spacing and radius tokens
  from the page's CSS custom properties and passes them to the theme. A token
  the page does not declare keeps its value from
  `web/src/platform/designTokens.ts`, which stays the only source in the
  repository.
- **The preview page becomes the bundle's harness:** `/design-system.html`
  loads the same bundle the artifact loads, and mounts each preview the same
  way, in light and dark. The Playwright spec checks it there: every preview
  draws without a console error in both modes, and no fixture reaches the
  server's web root.
- **No static files:** `scripts/design-system.sh` builds the bundle and no
  longer saves HTML files. The artifact's saved renditions, its
  `project/previews/` files, are removed when the local session first
  publishes the bundle.
- **Not the widget SDK:** the bundle is a preview of the standard widgets with
  fixtures. It is not the published SDK for custom widgets that
  [D8](d8-custom-ui-widgets-are-packaged-react-components.md) and M22 plan,
  and it gives configuration no new way to run code.

_Why:_ static files show how a widget looks, but not its pickers, dialogs,
validation messages or hover states, and a token edited on the page changes
nothing in them. Live widgets let the owner try a design change on the page
before it becomes an issue, and let a Design artifact use the real widgets
instead of drawing copies. Reading tokens from the page keeps
`designTokens.ts` the source while the page can try new values. The cost is a
bundle that holds React and Ant Design, a fixture layer that must not leak
into other pages, and a build the local session runs before every publish.
