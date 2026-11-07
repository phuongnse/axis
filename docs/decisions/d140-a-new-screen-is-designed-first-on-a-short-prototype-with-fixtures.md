# D140. A new screen is designed first, on a short prototype with fixtures

Status: **Agreed**

## Context

The owner reviews the design with a local agent session while NexKit builds the logic from issues.
That split follows layers: the look on one side, the logic on the other. In Studio and the other
hand-built screens the two cannot be split, because how a screen behaves is the product. Both sides
changed the same files at the same time.

The prototypes showed the cost. Each review ran on the local stack and started from the tag of the
review before, because `main` had not built that one yet, so prototypes stacked on prototypes. A
prototype grew routes of its own with no tests, and became a fork of the product. Each merge of `main`
lost logic that had to be found and brought back by hand. The issue that ended a review then built
all of it again, and a review had nothing that ended it, so its topic kept growing.

## Decision

- **Design and build split by stage, not by layer.** A new screen, or a new way of interacting, gets a
  design review before NexKit builds it. A screen made of agreed patterns needs none.
- **A held issue makes the build wait.** Each group of new screens has one design issue with the
  `on-hold` label, and the issues that build those screens are *blocked by* it. The design issues are
  the queue of reviews, and closing one starts its issues.
- **A review covers one screen or one group of screens, and starts from `main`.** It never starts
  from another review's tag. A screen that an earlier review's issues have not built yet waits for
  them.
- **A prototype is the real components on fixtures.** It changes only the SPA and its previews, and
  the owner sees it in the previews, where a fixture can show any state. It changes no server code.
  What a screen needs from an API goes in the docs as a contract, and NexKit builds it with its tests.
- **What a review finds outside its topic becomes an issue.** It is not built on the branch.
- **The docs are the source of truth.** The prototype's tag is a picture that the issues may link.

The steps are in [design changes](../delivery.md#design-changes).

## Consequences

- NexKit builds each new screen once, from agreed docs, and a prototype no longer has logic to carry
  over.
- A prototype's diff stays small and short-lived, so following `main` is a small merge.
- The issues that build a new screen wait for the owner's time, and so do the issues that need them.
  Issues with no new screen keep running.
- Every screen to be reviewed needs previews on fixtures, and fixtures can drift from the real data.
  They are typed against the API and have tests of their own.
- What only shows on a running system, such as speed or a real sign-in, is not reviewed. It is covered
  by the tests that come with the build.

## Alternatives

- **Keep the two tracks side by side and merge often.** Every merge has to decide what the prototype
  keeps, and the work is still done twice.
- **Review on the running stack.** It shows real data, but it needs the server, sign-in and data for
  each state, and it pulls server work into the prototype.
- **Review still pictures only.** They are the lightest, but they cannot show drawers, dialogs,
  keyboard use or how a screen moves between states, where most of a review's findings are.
- **An issue and a docs change for each agreed step.** Steps in a review often change earlier steps,
  so each change would be written, built and replaced. NexKit already splits a large issue.
- **Hold each issue with the `on-hold` label.** It needs no extra issue, but the holds are lifted one
  by one, and nothing shows the order of the reviews.
