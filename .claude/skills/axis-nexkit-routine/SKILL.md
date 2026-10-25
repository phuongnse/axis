---
name: axis-nexkit-routine
description: What Axis adds to NexKit's drive skill in the hourly "Axis nexkit driver" routine, which keeps Axis's issues and NexKit pull requests moving while the owner is away. Use only in that routine. Not for the owner's interactive session, which uses axis-nexkit, and not for agents inside the NexKit pipeline.
---

# Axis's hourly driver

The routine runs NexKit's `drive` skill on this repository. That skill holds
everything about driving NexKit: what to look at, what to do at each stop,
the rules for commands, notifying and the loop. This skill holds only Axis's
part: what the owner lets the driver do, what it runs, and when it hands
something to the owner. Where the `drive` skill leaves a choice to the
repository, this skill makes it.

## Start of a run

1. Work on an up-to-date checkout of `main`.
2. Read NexKit's repository and pinned ref from the `nexkit_repository` and
   `nexkit_ref` inputs in `.github/workflows/nexkit.yml`. Clone NexKit at that
   ref into a temporary folder. Read `skills/drive/SKILL.md` there and follow
   it, with the docs it links at the same ref.
3. Read [Who does what](../../../docs/delivery.md#who-does-what) and
   [Decisions](../../../docs/delivery.md#decisions) in `docs/delivery.md`, and
   this skill. Read them again only when `main` gets new commits.

## What the owner lets the driver do

- **Act as the owner.** The owner has confirmed in advance every option that
  NexKit, the plan, the issue or `docs/` recommends or supports. Answer from
  the plan, the issue, `docs/` and [decisions.md](../../../docs/decisions.md).
  Where the `drive` skill or NexKit's troubleshooting says to ask a person,
  take NexKit's recommendation instead. Notify only for the cases in the
  `drive` skill's Notify list and under [Hand to the owner](#hand-to-the-owner).
- **Never approve or merge,** and never change a decision's status.
- **Push only** the hand merges below, on the pull request's own branch, and
  hand-made changes, on the session's designated branch. Open pull requests
  only for hand-made changes.

## Hand merges

When NexKit cannot push a merge, because it brings changes to
`.github/workflows/` or the conflict is in a path NexKit may not change: merge
`main` into the pull request's branch in a separate worktree, run
`scripts/build.sh`, `scripts/lint.sh` and `scripts/test.sh`, push to that
branch, and comment `/nexkit review`.

## Hand-made changes

When the latest plan says the change is in paths NexKit may not change:

1. Make the change on the designated branch, exactly as the plan says.
2. Run at least the plan's check commands, `node scripts/check-links.mjs` and
   `node scripts/check-docs.mjs`.
3. Commit, push, and open a pull request that closes the issue, with the
   plan's points for the owner in their own short section.
4. When its CI fails, fix and push. When CI and every check are green, hand it
   to the owner for the admin-bypass merge.

## Issues the driver opens

An issue the driver opens for one CI failure is in no milestone, like the
`bug` issues NexKit opens, as
[D68](../../../docs/decisions/d68-issues-of-several-milestones-run-at-once.md)
says. Autopilot takes it all the same.

## Hand to the owner

Notify the owner with `PushNotification`, in addition to the cases the `drive`
skill lists, only for:

- a choice that rests on a **Proposed** decision the owner has not settled,
  with no basis in the plan, the issue or `docs/`;
- a hand-made pull request that is ready for the admin-bypass merge.

## Timing

The routine starts every hour at minute 20. Stop 45 minutes after the run
started, so the next run never overlaps this one.
