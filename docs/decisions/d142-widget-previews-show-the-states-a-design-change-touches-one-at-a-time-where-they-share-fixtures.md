# D142. Widget previews show the states a design change touches, one at a time where they share fixtures

Status: **Agreed**

## Context

A live preview ([D67](d67-live-widget-previews-in-the-design-system.md)) that draws only the typical
case hides the empty list, the validation error and the read-only input, the pictures a design change
most often breaks.

Stacked in one card, the states of a screen such as Studio would all fetch the same API path, and the
one fixture that answers a path cannot be an empty list in one state and a failure in the next.

## Decision

- **A preview draws each state that changes how its widget looks,** each under a short caption.
  The states are stacked in one card, except where they share a fixture path.
- **States that share a fixture path show one at a time.** The card chooses the state, so the one
  that is shown has its answers alone.
- **Shared states are drawn once.** Loading and a load error, which every widget shows the same way,
  are in the `states` preview.
- **In the same change.** A change that adds such a state to a widget adds it to the preview.
- **Live states need no state.** Hover, focus, pickers and dialogs work in the live preview.

The states of each preview, and how the card chooses one, are in
[widget previews](../reference/widget-previews.md).

## Consequences

- Each preview stays complete as widgets grow, at the cost of more fixtures and taller cards.
- Loading and the load error, drawn with the same alert and busy mark everywhere, are not repeated.
- A screen whose states answer the same path is complete in one card, and a reviewer sees one state
  at a time.
