# D68. Issues of several milestones run at once

Status: **Agreed**

## Context

With one milestone at a time, a milestone's last issues run alone while the next milestone's ready
work waits for the close. The _blocked by_ marks between issues already say what must come first,
so ordering by milestone adds a wait that the marks do not need.

## Decision

- **Autopilot takes every open issue.** Milestones group the work and do not order it. Only
  _blocked by_ marks make one issue wait for another, in the same milestone or across milestones. A
  `bug` issue that autopilot opens for CI on `main` is in no milestone. The setting is in
  [delivery](../delivery.md#decisions).
- **Any number of milestones may have open issues.** Opening an issue starts it, so a milestone is
  broken down only when the owner asks and it passes the readiness check.
- **A readiness check comes first.** Before a milestone is broken down, the local agent checks:
  - that every item of its "Done when" text has **Agreed** decisions and doc sections that issues
    can link to;
  - whether each thing it needs is in the code, built by an open issue, or built by nobody;
  - which concepts, docs and code it shares with the milestones that have open issues.

  It answers _ready_, _ready with waits_, naming which issues wait for which, or _not ready_, naming
  what has to exist first. It compares the other planned milestones too, because the
  [roadmap](../roadmap.md#milestones) lets them change order.

How many issues run at once is autopilot's `max_parallel` setting, see
[how issues run](../delivery.md#how-issues-run).

## Consequences

- Ready work never waits for another milestone to close.
- A missing mark is no longer covered by the milestone order, so two issues can build the same
  thing. The readiness check keeps each breakdown checked against all open work.
- A breakdown made while another milestone runs knows less about the code it will meet.
- More pull requests run at once, so `main` breaks more often.

## Alternatives

- **One milestone at a time,** with issues created only for the current milestone. It is simpler to
  follow, but the last issues of each milestone run alone.
