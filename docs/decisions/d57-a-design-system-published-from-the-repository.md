# D57. A design system published from the repository

Status: **Agreed**

The repository holds the Axis design system, and the owner's local agent
session publishes it to Claude as a Design System artifact. A design change
comes back as a NexKit issue. The owner agreed these points on 2026-10-22, and the last two on 2026-10-24. See
[Design changes](../delivery.md#design-changes) for the flow and the
[frontend reference](../reference/frontend.md) for the token file and the
preview pages.

- **Source of truth:** the repository. It holds an Axis token file for colours
  in light and dark mode, type, spacing and radius, and the widgets
  themselves.
- **Published design system:** the owner's local agent session publishes a
  Design System artifact in Claude from the repository. It shows the tokens
  and a preview of each widget, named in Axis terms. The widget previews are
  drawn live from one bundle that the repository builds, as
  [D67](d67-live-widget-previews-in-the-design-system.md) agrees.
- **Who edits the design:** only the owner and Claude. A design change goes
  back into the repository as a NexKit issue that links the artifact. It is
  never a hand-made pull request.
- **New screens:** they may be drawn as Design artifacts that use this design
  system. An issue may link one as a picture. The docs stay the source of
  behaviour.
- **Publishing again:** after a merged change to the tokens or a widget, or
  whenever the owner asks, the local session publishes the design system
  again.
- **NexKit's agents:** they do not read or write Claude Design.
- **Running ahead:** the artifact may differ from the repository only by
  changes that have an open NexKit issue, and its last change note names that
  issue.
- **Sync:** a publish from the repository keeps those changes, and lists every
  other difference it overwrites before it removes anything.

_Why:_ the design has one place to edit, and the repository and the published
design stay in step. A design change is an ordinary NexKit issue, so it gets a
plan, checks and a review like any other change. Hand-made pull requests then
stay only for paths NexKit may not change. Keeping NexKit's agents out of
Claude Design means a picture never replaces the docs as the source of
behaviour. The owner sees a change before it becomes an issue, and a sync
never silently drops a change that is still waiting to merge. The cost is that the local session must publish again after each
merged change to the tokens or a widget.
