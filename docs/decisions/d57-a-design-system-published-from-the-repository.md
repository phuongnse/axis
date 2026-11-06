# D57. A design system published from the repository

Status: **Agreed**

## Context

The look of Axis is its design tokens and its shared widgets. The owner wants to see and try a design
change in Claude, as a Design System artifact, before it becomes work. The design needs one place to
edit, and the docs must stay the source of behaviour.

## Decision

- **The repository is the source of truth:** the token file and the widgets.
- **The owner's local agent session publishes it** to Claude as a Design System artifact, with the
  tokens and a live preview of each widget ([D67](d67-live-widget-previews-in-the-design-system.md)),
  after every merged change to the tokens or a widget and whenever the owner asks.
- **Only the owner and Claude edit in Claude Design.** A design change returns as a NexKit issue that
  links the artifact, never as a hand-made pull request, and NexKit's agents neither read nor write
  Claude Design. An issue may link a Design artifact of a new screen as a picture.
- **The artifact runs ahead only by open issues.** A publish keeps the changes of open design issues,
  and lists every other difference before it overwrites it.

The flow is in [design changes](../delivery.md#design-changes), the token file in
[frontend](../reference/frontend.md) and the previews in
[widget previews](../development.md#widget-previews).

## Consequences

- A design change gets a plan, checks and a review like any other NexKit issue.
- A picture never replaces the docs as the source of behaviour.
- A sync never silently drops a change that is still waiting to merge.
- The local session must publish again after each merged change to the tokens or a widget.
