# Changing a part

Use this before a change is built, in a requested change or in a design review. It checks the change's
subject, the look of a part and the users of a shared widget. The rules are
in [Design changes](../../../docs/delivery.md#design-changes) and
[Shared widgets](../../../docs/reference/shared-widgets.md#the-rule).

## The subject of a change

1. Before you restate a change the owner asks for, name the subject it belongs to, by the one-subject rule in
   [Design changes](../../../docs/delivery.md#design-changes): a page, view, entity or shared widget. A drawer,
   dialog or panel belongs to a subject only when it opens from it and serves only it.
2. List the open design issues with `gh issue list --label design --state open --json number,title`.
3. In a design review, when the subject is not the review's, tell the owner in your reply without being asked.
   Name the subject. Recommend the open design issue of that subject, or a new held design issue opened as
   [Each change](SKILL.md#each-change) says. Build nothing of it until the owner chooses.
4. In a requested change, when the subject is an open design issue's, say so the same way and recommend that
   issue. When it spans several subjects, recommend one issue for each. When no open design issue has the
   subject, the change goes on as any requested change does.

## One look for a control

1. Look at the part on a screenshot of the whole page it draws, not at the region alone.
2. Find the same control elsewhere. Search `web/src/` for the widget, class or token that draws it, and
   check its previews in [widget previews](../../../docs/reference/widget-previews.md). Look at every tab
   row, every button of the same role and every title row's rule.
3. Keep one look. Never trim padding, size or a rule for one place. A tab row with less padding than the
   others looks wrong beside them. If a change seems right, propose it for the control everywhere, list
   the places it reaches, and let the owner decide.

## A shared widget's users

1. List every place that uses the widget. Search `web/src/` for its name, and list its previews too.
2. For each place, say what the change does there. For example, a button may appear or change with a
   person's rights.
3. Make the change for every screen that uses the widget. Name that reach, the places of step 1, in the
   step's rule comment, or in a requested change's NexKit issue. Ask the owner only about the design choice
   itself, never whether the other screens follow. A change that is itself outside the subject went to the
   owner already, as [the subject of a change](#the-subject-of-a-change) says.
