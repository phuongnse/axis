# D61. Widget trees in columns, tabs and sections, and pages at phone width

Status: **Agreed**

## Context

Real pages need more than one widget, and [D15](d15-presentation-model.md) makes layout a matter of
container widgets. A page's address holds state, such as a table's paging or a form's record, so
each part of it needs one owner. Pages must also work on a phone.

## Decision

- **A tree of widgets.** The containers `columns`, `tabs` and `section` hold other widgets, nested to
  at most 4 levels, and column widths are twelfths that add up to 12.
- **One owner for each part of the address.** A page holds at most one `table`, and at most one of
  `form`, `taskInbox` and `processInspector`.
- **Policies prune the tree.** A widget the user cannot read is left out, and so is a container left
  empty. A page is readable when one of its widgets is.
- **One breakpoint for the platform.** Below it, columns stack, tables scroll inside their widget and
  the navigation collapses into a menu, with no setting per widget.
- **Studio edits the tree,** as [D53](d53-typed-editors-and-the-process-graph-in-studio.md) sets out.

The shapes are in [resource files](../reference/resource-files.md#resource-file-shape), the drawing
in [container widgets](../reference/container-widgets.md), the policies and the breakpoint in
[frontend](../reference/frontend.md), and the editor in [Studio](../reference/studio.md#widget-tree).

## Consequences

- Layout stays in configuration, and the address, routes and compile checks stay simple.
- A page cannot show two tables or two forms.
- Every page has the same phone behaviour, with nothing to set.

## Alternatives

- **A responsive setting on each widget.** It would add a setting that no sample needs.
