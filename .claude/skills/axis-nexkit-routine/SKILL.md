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

When the latest plan says the change is in paths NexKit may not change, under
*Needs a hand-made change*, or a `/nexkit go` or fix round stopped as blocked
because its change needs those paths:

1. Make the change on the designated branch, exactly as the plan says.
2. Run at least the plan's check commands, `node scripts/check-links.mjs` and
   `node scripts/check-docs.mjs`.
3. Commit, push, and open a pull request that closes the issue, with the
   plan's points for the owner in their own short section.
4. When its CI fails, fix and push. When CI and every check are green, hand it
   to the owner for the admin-bypass merge.

## Issues the driver opens

The driver opens its issues in no milestone, like the `bug` issues NexKit
opens, as
[D68](../../../docs/decisions/d68-issues-of-several-milestones-run-at-once.md)
says. Autopilot takes them all the same.

1. For one CI failure, open one issue.
2. For Dependabot alerts, run `node scripts/dependabot-issues.mjs`, as
   [D107](../../../docs/decisions/d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md)
   and [dependency updates](../../../docs/development.md#dependency-updates)
   say. It prints a JSON list of issues, one for each high or critical alert
   that no open issue names, and opens nothing. For each printed issue:
   - keep the printed labels that `gh label list` shows, and add every other
     label whose description fits, as
     [delivery](../../../docs/delivery.md#the-life-of-an-issue) says;
   - write the body to a temporary file and run `gh issue create --title ...
     --label ... --body-file ...` with no `--milestone`.

   The issue names the advisory id, so the next run prints nothing for it.
   When the script fails, open no issue in that run and
   [hand it to the owner](#hand-to-the-owner).

## Hand to the owner

Notify the owner with `PushNotification`, in addition to the cases the `drive`
skill lists, only for:

- a choice that rests on a **Proposed** decision the owner has not settled,
  with no basis in the plan, the issue or `docs/`;
- a NexKit pull request that NexKit did not merge because its issue was
  closed (`merge_issue_closed`), for the owner to merge or close;
- a hand-made pull request that is ready for the admin-bypass merge;
- `scripts/dependabot-issues.mjs` failing, for example with no access to the
  Dependabot alerts, because alerts then go unhandled.

## Timing

The routine starts every hour at minute 20. Stop 45 minutes after the run
started, so the next run never overlaps this one.
