# D68. Issues of several milestones run at once

Status: **Agreed**

The owner agreed this on 2026-10-25. It replaces the issue planning line of
[D13](d13-delivery-workflow.md), which created issues only for the current
milestone.

- **Autopilot takes every open issue:** NexKit's autopilot scope is set to
  every open issue. Milestones group the work and do not order it: only
  *blocked by* marks make one issue wait for another, in the same milestone or
  across milestones. A `bug` issue that autopilot opens for CI on `main` is in
  no milestone.
- **Any number of milestones may have open issues:** a milestone is broken
  down when the owner asks and it passes the readiness check below. Opening an
  issue starts it, so a milestone is broken down only when it is meant to run.
- **A readiness check comes first:** before a milestone is broken down, the
  local agent checks that every item of its "Done when" text
  has **Agreed** decisions and doc sections that issues can link to; whether
  each thing it needs is in the code, built by an open issue, or built by
  nobody; and which concepts, docs and code it shares with the milestones that
  have open issues. It answers
  *ready*, *ready with waits*, naming which issues wait for which, or
  *not ready*, naming what has to exist first. It compares the other planned
  milestones too, because the [roadmap](../roadmap.md#milestones) lets them
  change order.
- **No limit on parallel runs:** autopilot's `max_parallel` stays unset, to
  see how far parallel work goes. The owner sets a limit when one is needed.
  - The owner set the limit to six on 2026-10-27, with NexKit's parallel
    checks and branches that must be up to date. See
    [delivery](../delivery.md#decisions).
  - The owner lowered the limit to three on 2026-10-28, because pull requests
    that must be up to date merge one at a time, and each merge checked every
    waiting pull request again.
- **Turned on in steps:** NexKit is upgraded first with the milestone scope
  unchanged, and the scope changes in a pull request of its own, so a problem
  points to one of the two.
- **Reviewed when M14 ends:** the owner looks at four signs:
  - two issues built the same thing because a mark was missing;
  - CI on `main` failed more than about twice a week;
  - pull requests often used up their automatic fix rounds on failures that
    other pull requests caused;
  - issues that waited for a person piled up faster than the owner answered
    them.

_Why:_ with one milestone at a time, a milestone's last issues run alone while
the next milestone's ready work waits for the close. Marks between issues
already say what must come first, so the milestone order adds a wait that the
marks do not need. The cost is that a missing mark is no longer covered by
that order, that a breakdown made while another milestone runs knows less, and
that more pull requests run at once, so `main` breaks more often. The readiness
check keeps each breakdown checked against all open work, and the review at
the end of M14 shows whether the cost was worth it.
