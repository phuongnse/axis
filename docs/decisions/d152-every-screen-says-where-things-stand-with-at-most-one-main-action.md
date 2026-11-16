# D152. Every screen says where things stand, with at most one main action

Status: **Agreed**

## Context

A person who opens a screen must see at once where things stand and which action finishes the task. A
screen with several solid actions leaves the person to guess which one to press, and a screen that says
nothing of its state leaves them to read the whole page to find out.

## Decision

- Every screen says where things stand in a few plain words, such as "Release 7 is live.", as
  [what comes next](../reference/actions-and-controls.md#what-comes-next) says.
- A screen has at most one main action, which is its one solid Finish button, as
  [buttons](../reference/actions-and-controls.md#buttons) says. Every other action beside it is a text
  button.

## Consequences

- A person reads a screen's state without opening anything else.
- A screen that needs two finishing actions must split its work, or make the second one a text button.
- Each screen gives a line to its state, which costs some space on a small screen.

## Alternatives

- **No statement of state.** A person works out the state from the table and the buttons.
- **Several main actions, each solid.** A person must judge which one to press, and the screen looks
  as if every action finishes the task.
