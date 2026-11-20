# Breaking down a milestone

When the owner asks, give the readiness verdicts of
[D68](../../../docs/decisions/d68-issues-of-several-milestones-run-at-once.md)
first, with the decisions, docs and open issues behind each. Give them for the
milestones of the roadmap's current phase only, unless the owner names another,
as [D166](../../../docs/decisions/d166-axis-is-designed-for-small-businesses-first-and-the-milestones-in-three-phases.md)
agrees, and write no issue of a later phase unless the owner asks. On *ready*:

- Create the milestone's issues as in [Writing an issue](SKILL.md#writing-an-issue). The first one creates
  the milestone on GitHub, titled exactly as its roadmap row.
- Find the new screens and new ways of interacting. For each subject, a page, view, entity or shared
  widget, open one design issue titled by it, with the `design` and `on-hold` labels and no milestone, as
  [how issues run](../../../docs/delivery.md#how-issues-run) says. Mark its build issues *blocked by* it, as
  [Design changes](../../../docs/delivery.md#design-changes) says.
- Check that each risky concept the **From** column of
  [concepts.md](../../../docs/domain/concepts.md) lists for the new milestone
  falls under `hard` by its consequence, and that work the platform has not
  done before, such as a new kind of editor, widget or library, is not
  described by the `standard` text.
- Compare the profiles' results in the ending milestone and check their
  models against the models now available, as NexKit's configuration doc says
  for writing profiles.
- Propose any change to the owner as a hand-made pull request.
