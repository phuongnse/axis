# D13. Delivery workflow

Status: **Agreed**

## Context

Axis is built mostly by AI agents, one session at a time. A large up-front breakdown into layered
issues delays anything runnable until the very end, and its issues are too big for one session.

## Decision

- **NexKit runs one issue per agent session.**
- **Milestones are vertical slices.** Each ends with a running feature and an end-to-end journey.
  When a milestone is broken into issues is
  [D68](d68-issues-of-several-milestones-run-at-once.md)'s choice.
- **One issue is one behaviour,** with 2 to 5 acceptance criteria that a test or a command checks.
  More than five is a sign to check whether the issue holds more than one behaviour, and to split it
  if it does. When every criterion checks the same behaviour, more are fine, and a plan is not
  revised only to cut their number.
- **The docs hold the product and architecture knowledge,** and issues link to them rather than
  copy them.

How the work runs, step by step, is in [delivery](../delivery.md).

## Consequences

- Every milestone leaves something that runs, so a wrong turn shows early.
- Each issue fits one session, and its criteria tell the review what to check.
- The docs must be complete before an issue can link them, so design work comes first.
