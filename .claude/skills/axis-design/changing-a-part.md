# Changing a part

Use this before a part of a screen changes, in a requested change or in a design review. The rules are
in [Design changes](../../../docs/delivery.md#design-changes) and
[Shared widgets](../../../docs/reference/shared-widgets.md#the-rule).

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
3. If a screen other than the one in hand would change, stop. Take the list to the owner before you build
   it. A change the owner accepts outside the subject becomes its own held design issue, as
   [Each change](SKILL.md#each-change) says.
