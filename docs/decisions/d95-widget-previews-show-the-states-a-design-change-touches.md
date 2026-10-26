# D95. Widget previews show the states a design change touches

Status: **Agreed**

A widget preview in the design system draws its widget in each state that changes how the widget
looks, stacked in one card, each under a short caption. The owner agreed this on 2026-10-26. It
extends [D67](d67-live-widget-previews-in-the-design-system.md)'s one mount per widget: a mount now
draws every state of its preview.

- **Each state that changes the look:** a preview draws its widget in each state that changes how the
  widget looks.
- **Shared states once:** the states every widget shows the same way, loading and a load error, are
  drawn once, in the `states` preview.
- **Same change:** a change that adds such a state to a widget adds it to the widget's preview in the
  same change.
- **Live states need no state:** hover, focus, pickers and dialogs need no state of their own, as the
  previews are live.

The states of each widget:

| Widget                          | States                                                                 |
| ------------------------------- | ---------------------------------------------------------------------- |
| `field-<type>`                  | typical, required, read-only, validation error                         |
| `field-child-collection`        | reference and computed cells, a cell error, read-only, empty           |
| `form`                          | validation errors, a field made read-only by a condition, wizard steps |
| `table`                         | row and toolbar actions, long text, empty                              |
| `taskInbox`                     | an overdue task, a claimed task, empty                                 |
| `processInspector`              | a failed instance, open dead letters                                   |
| `detail`, `chart-<kind>`, `kpi` | with data, empty                                                       |
| `columns`, `tabs`, `section`    | their nested layout                                                    |

_Why:_ a preview that draws only the typical case hides the empty list, the validation error and the
read-only input, which are the pictures a design change most often breaks. Listing the states in the
decision keeps each preview complete as widgets grow, and drawing loading and the load error once
avoids repeating one picture for every widget, as all of them use the same alert and the same busy
mark. The cost is more fixtures to keep, and a card that grows with its states.
