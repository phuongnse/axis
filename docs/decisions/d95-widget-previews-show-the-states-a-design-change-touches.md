# D95. Widget previews show the states a design change touches

Status: **Agreed**

## Context

A live preview ([D67](d67-live-widget-previews-in-the-design-system.md)) that draws only the typical
case hides the empty list, the validation error and the read-only input, the pictures a design change
most often breaks.

## Decision

- **A preview draws each state that changes how its widget looks,** stacked in one card, each under
  a short caption.
- **Shared states are drawn once.** Loading and a load error, which every widget shows the same way,
  are in the `states` preview.
- **In the same change.** A change that adds such a state to a widget adds it to the preview.
- **Live states need no state.** Hover, focus, pickers and dialogs work in the live preview.

The states of each preview are in [widget previews](../development.md#widget-previews).

## Consequences

- Each preview stays complete as widgets grow, at the cost of more fixtures and taller cards.
- Loading and the load error, drawn with the same alert and busy mark everywhere, are not repeated.
