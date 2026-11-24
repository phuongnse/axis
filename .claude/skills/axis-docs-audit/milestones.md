# Milestone issues

The file step checks each GitHub milestone against its row in [the roadmap](../../../docs/roadmap.md).
A milestone is matched by the `M<n>` that starts its title. A milestone whose title does not start
that way is skipped. The step lists two cases, as [the life of a milestone](../../../docs/delivery.md#the-life-of-a-milestone)
says the close must agree with the roadmap:

1. A milestone is closed on GitHub, and its roadmap row does not read Done.
2. A milestone has no open issue on GitHub, its roadmap row does not read Planned, and a `_(planned for Mx)_` marker in a doc under `docs/` names it.

A Planned milestone is not broken down yet, so it is expected to have no issue, and its markers wait for its issues.

For each listed milestone, the step opens one issue. Its title is `The roadmap and the docs of Mx match
its close on GitHub`, and it carries the labels `documentation` and `docs-audit`. Its hidden marker is
`docs-audit milestone Mx`, so an open issue with that marker stops a second one. The issue links each
marker it names, and its acceptance criteria say that the row must read Done, that each marker is removed
or moved as [planned work](../../../docs/writing-docs.md#planned-work) says, and that the two checks of the
docs pass.

The file step opens these issues before the others, so the cap of ten does not leave them out. The
listing is read again on each run. A milestone that still disagrees gets no second issue while its
first one is open.
