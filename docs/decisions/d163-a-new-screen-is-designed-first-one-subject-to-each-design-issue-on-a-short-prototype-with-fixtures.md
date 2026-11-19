# D163. A new screen is designed first, one subject to each design issue, on a short prototype with fixtures

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

A design issue that covered a group of loosely tied screens had the same problem. The review went on
to whatever the group touched, and a review of one site's pages reached dashboards after the task and
instance pages. Nothing in the group said where the review should stop.

## Decision

- **Design and build split by stage, not by layer.** A new screen, or a new way of interacting, gets a
  design review before NexKit builds it. A screen made of agreed patterns needs none.
- **A held issue makes the build wait.** Each subject has one design issue with the `on-hold` label,
  and the issues that build that subject are *blocked by* it. The design issues are the queue of
  reviews, and closing one starts its issues.
- **A design issue designs one subject:** one page, one view, one entity or one shared widget. Its
  title names that subject. Something belongs to the subject only when it opens from the subject and
  serves only it, such as a drawer, a dialog or a panel, or when the owner cannot judge the subject
  without it. A shared widget is reviewed in the places that use it.
- **Everything else is its own design issue.** When a review's scope or a step reaches anything outside
  the subject, the session proposes it as a design issue of its own. That issue is held, with no
  milestone, and is *blocked by* the first one only when it builds on what that one agrees. The step
  goes on without it.
- **A review covers its design issue's subject, and starts from `main`.** It never starts from another
  review's tag. A screen that an earlier review's issues have not built yet waits for them.
- **A prototype is the real components on fixtures.** It changes only the SPA and its previews, and
  the owner sees it in the previews, where a fixture can show any state. It changes no server code.
  What a screen needs from an API goes in the docs as a contract, and NexKit builds it with its tests.
- **The docs are the source of truth.** The prototype's tag is a picture that the issues may link.

The steps are in [design changes](../delivery.md#design-changes).

## Consequences

- NexKit builds each new screen once, from agreed docs, and a prototype no longer has logic to carry
  over.
- A prototype's diff stays small and short-lived, so following `main` is a small merge.
- Reviews stay short. Each design issue's title tells the queue what it designs.
- There are more design issues. The owner judges where a review's subject ends, when a review starts
  and at each step.
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
- **One design issue for each group of new screens.** A review of loosely tied screens keeps growing,
  because nothing bounds it. Each subject gets its own issue instead.
