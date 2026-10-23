---
name: axis-nexkit
description: Runbook for the local agent session that works NexKit for the Axis owner (writing issues and their order, reading plans, stepping in where autopilot stops, triaging pull requests). Use when the owner asks to plan, implement, check, fix or split work through NexKit, or asks about the state of a NexKit issue or pull request. Not for agents running inside the NexKit pipeline.
---

# Driving NexKit for Axis

The rules, the roles and the life of an issue are in
[docs/delivery.md](../../../docs/delivery.md). Read the sections your task
touches. This runbook says how to apply them: where values live, the commands,
the checklists and what to do when something fails. It does not repeat the
rules, so follow its links.

Autopilot plans and starts the issues of the current milestone, answers
questions that have a recommended option, and splits plans that are too large.
Your part is what it cannot do: write each issue and mark what it must wait
for, read each plan and comment changes, and act where it stops and a person is
needed. You may post any command, but never `/nexkit plan` or `/nexkit go` on
an issue whose autopilot state is `start` or `active`. Ask the owner first when
a choice rests on a **Proposed** decision or only the owner can answer it.
Never approve, merge or close a pull request yourself.

## Where the values live

Look these up each time. Do not copy them into notes or docs, because they
change with a NexKit release or a settings change.

- **NexKit repository and version:** the `nexkit_repository` and `nexkit_ref`
  inputs in the workflow file.

  ```bash
  grep -E 'nexkit_(repository|ref):' .github/workflows/nexkit.yml
  ```

- **NexKit's own docs** (how it works, configuration, troubleshooting): list
  them at that ref, then read the one you need.

  ```bash
  gh api "repos/<nexkit_repository>/contents/docs?ref=<nexkit_ref>" --jq '.[].path'
  gh api "repos/<nexkit_repository>/contents/<path>?ref=<nexkit_ref>" \
    -H 'Accept: application/vnd.github.raw'
  ```

- **Settings:** `.nexkit/config.json`. A key that is `null` or missing takes
  NexKit's default, from its configuration doc at the pinned ref. A profile's
  stage settings override `stages`, which override the top-level `model` and
  `effort`. The `cache` entries must match the `actions/cache` steps in
  `.github/workflows/ci.yml`.

  ```bash
  jq '{protected_paths, fix, merge, conflicts, usage_limit, close_parent_issues, autopilot, notify}' .nexkit/config.json
  jq '{default_profile, triage: .stages.triage, profiles}' .nexkit/config.json
  jq '.cache' .nexkit/config.json
  ```

- **Required checks on `main`:** the active rules for the branch.

  ```bash
  gh api repos/{owner}/{repo}/rules/branches/main \
    --jq '.[] | select(.type=="required_status_checks") | .parameters.required_status_checks[].context'
  ```

- **NexKit's branch prefix:** the `startsWith(github.event.pull_request.head.ref, '…')`
  condition in the job `if:` block of `.github/workflows/nexkit.yml`.
- **The pull request for an issue:** the link in NexKit's run comment on the
  issue.

## Commands

Start every session with NexKit's local `status` command, at the pinned ref,
and use it to watch the work. It lists each open issue and pull request with
its state and last round, the recent merges with their CI runs on `main`, what
the next autopilot sweep does with each issue and why, and a last section with
only what needs a person. `--json` gives the same as JSON.

```bash
REPO=$(grep -oE 'nexkit_repository: \S+' .github/workflows/nexkit.yml | cut -d' ' -f2)
REF=$(grep -oE 'nexkit_ref: \S+' .github/workflows/nexkit.yml | cut -d' ' -f2)
KIT="${TMPDIR:-/tmp}/nexkit-$REF"
[ -d "$KIT" ] || git clone -q --depth 1 --branch "$REF" "https://github.com/$REPO" "$KIT"
python3 "$KIT/bin/nexkit" status
```

Post a command, read the latest plan (the newest NexKit comment, with its
profile, models and why triage chose it), or start a sweep yourself, for
example after changing what blocks what. A profile change must be a comment;
a request in the issue text does not count.

```bash
gh issue comment <N> --body "/nexkit plan"
gh issue comment <N> --body "/nexkit plan Use the hard profile"
gh pr comment <N> --body "/nexkit fix <instructions>"
gh issue view <N> --comments
gh workflow run nexkit.yml -f command=autopilot -f number=autopilot
```

Mark an issue *blocked by* another, and make an issue a sub-issue of a parent.
Autopilot reads no "Depends on" text, and the order of sub-issues does not
count. Both endpoints take the database `id`, not the number. If one returns
404, upgrade `gh`.

```bash
BLOCKER_ID=$(gh api repos/{owner}/{repo}/issues/<blocking> --jq .id)
gh api -X POST repos/{owner}/{repo}/issues/<blocked>/dependencies/blocked_by -F issue_id="$BLOCKER_ID"
gh api repos/{owner}/{repo}/issues/<blocked>/dependencies/blocked_by --jq '.[].number'

CHILD_ID=$(gh api repos/{owner}/{repo}/issues/<child> --jq .id)
gh api -X POST repos/{owner}/{repo}/issues/<parent>/sub_issues -F sub_issue_id="$CHILD_ID"
```

Split an issue by hand only when autopilot cannot, for example when the plan
reports `too_large` without suggesting parts. Write each part as a new issue
with the parent's milestone and labels, titled as
[step 3 of the life of an issue](../../../docs/delivery.md#the-life-of-an-issue)
says, for example `Connector call steps pass compile checks`, not
`Call connector step: compile checks and model`. Then add the parts to the
parent and mark each *blocked by* the parts it needs.

## Writing an issue

The rules are in step 1 of
[the life of an issue](../../../docs/delivery.md#the-life-of-an-issue) and in
[writing on GitHub](../../../docs/delivery.md#writing-on-github).

1. Read the doc sections the issue implements. Write the behaviour, by role,
   with 2–5 acceptance criteria, and name the concepts it touches in the
   terms of [concepts.md](../../../docs/domain/concepts.md). Triage reads only
   the issue and its discussion, not the links.
2. Read the labels with `gh label list` and pick every one whose description
   fits.
3. Create it in the current milestone:
   `gh issue create --title "<behaviour>" --milestone "<milestone>" --label "<a>,<b>" --body-file <file>`.
4. Before the next sweep, mark it *blocked by* every issue it must wait for,
   or autopilot plans it at once. The milestone's docs sweep is blocked by
   every other issue of the milestone.

## Reviewing a plan

Check:

- The issue is one behaviour. More than five criteria: split only if it holds
  more than one behaviour, never re-plan only to cut the number.
- The profile fits the `when` texts. If not, re-plan with
  `/nexkit plan Use the <profile> profile`. If a `when` text caused the wrong
  choice, propose a fix to the owner as a hand-made pull request.
- Every doc link resolves, and no **Proposed** decision in
  [decisions.md](../../../docs/decisions.md) is treated as **Agreed**.
- The plan names roles, not development users, and puts the decisions and
  risks the owner must judge in their own short sections.
- A `too_large` plan suggests parts titled by their own behaviour, each naming
  the earlier parts it needs.
- After a re-plan, its *Since the last plan* section lists every change that
  was asked for, and nothing else changed without a reason.

Comment every change, however small, on the issue before `/nexkit go`. Once
`/nexkit go` has started, post it as `/nexkit fix <change>` on the pull
request instead. A re-plan keeps what no newer comment changes, so:

- State only the changes. Choices accepted as they are need no comment.
- One short line per change, with the exact values, names and rules. No
  reasons or prose, and nothing the issue or the docs already say: link
  instead. For example:
  `Change: limits 2,000 chars / depth 32 / 500 nodes; == null-safe; trim not in SQL.`
- Use `/nexkit plan from scratch` only when the plan has gone wrong, and
  comment every choice to keep before you post it.

## Breaking down a milestone

Before the owner closes the current milestone:

- Create the next milestone's issues and their *blocked by* marks. Autopilot
  takes them at the first sweep after the close.
- Check each risky concept that the **From** column of
  [concepts.md](../../../docs/domain/concepts.md) lists for the new milestone
  falls under `hard` by its consequence, and that work the platform has not
  done before, such as a new kind of editor, widget or library, is not
  described by the `standard` text.
- Compare the profiles' results in the ending milestone: the share of issues
  whose rounds stopped on an error, ran out of time or failed checks. When
  `standard` fails more often than `hard`, narrow the `standard` text.
- Check whether newer models make a profile's models out of date.
- Propose any change to the owner as a hand-made pull request.

## Reviewing a pull request

While `merge.auto` is on, NexKit has merged by the time you look: review the
merged pull request and raise problems with the owner. While it is off, NexKit
mentions the owner when a pull request is ready.

- The behaviour change matches the issue and the approved plan.
- When agreed behaviour changes, the docs change in the same pull request.
- Tests cover the change, including denied access where it applies.
- The required checks on `main` are green.
- No edits in paths NexKit may not change, unless the pull request is
  hand-made.
- A change to what `scripts/setup.sh` downloads, such as a new toolchain or a
  lock file in a new place, needs matching `cache` entries and CI cache steps.
  Those are in paths NexKit may not change, so tell the owner and propose that
  hand-made pull request.

## When something fails

NexKit handles these by itself, so do nothing until it mentions the owner: a
conflict with `main`, the Claude usage limit, failing checks and requested
changes within the automatic fix limit, a CI failure on `main`, which it runs
again and then turns into a `bug` issue that it plans first, a run on `main`
that passed only when run again, which it records on a `bug` issue for each
failure, named by the failing test, and a missed dependency cache. Its troubleshooting doc at the pinned ref has the mechanics.

| Failure | What you do |
| --- | --- |
| Publish fails after `/nexkit go` | Read the run log. If the base branch was force-pushed, the same `/nexkit go` fixes it. Otherwise diagnose first, and ask the owner before posting `/nexkit go` again. |
| A plan or a `blocked` round has a question with no recommended option | Ask the owner when the choice is theirs. Answer a plan's question in a comment on the issue, so the next sweep plans again, and a round's question with `/nexkit fix <decision>`. |
| No automatic fix rounds left, for failing checks or requested changes | Read the failing check's log or the review, ask the owner, then post `/nexkit fix <instructions>`. |
| `conflicts.max_rounds` conflict rounds ran | Comment `/nexkit fix` yourself. |
| A conflict in a path NexKit may not change, or a merge that brings workflow changes | Merge `origin/main` into the pull request branch by hand, in a separate worktree. Run `scripts/build.sh`, `scripts/lint.sh` and `scripts/test.sh`. Ask the owner before pushing, then comment `/nexkit review`. |
| An implement or fix round ran out of time twice, in the issue or in one of its parts | Autopilot has already planned once with a note to split, and parts inherit that time-out. Read the plan, the run logs and the result files in the round's `nexkit-checks` artifact. When the cause is not known, find it first; then split the issue by hand or comment a smaller plan. |
| A diagnosis round stopped as `blocked` | It found no cause in the evidence. Read what it found and the result files, ask the owner when the choice is theirs, then answer with `/nexkit fix <instructions>`. |
| The plan comment says triage failed | The plan ran on the previous plan's profile or on `default_profile`. If that is wrong for the issue, re-plan with `/nexkit plan Use the <profile> profile`. |
| A round stops because its plan names a profile that is no longer configured | Re-plan with `/nexkit plan`, then repeat the command that stopped. Ask the owner first when that command is `/nexkit go`. |
| A run paused at the usage limit stops again with an unknown reset time | Repeat the command. |
| Every run misses a dependency cache | Compare the `cache` entries with the CI cache steps, and check that CI on `main` succeeds, because `actions/cache` saves only after a successful job. |
| The review job errored | The round's comment says why. Comment `/nexkit review` to retry, unless it names an authentication error: then tell the owner, because a retry stops the same way. |
| NexKit's automatic merge was refused (`merge_refused`) for a reason other than a conflict | The round's comment says why, usually a rule on `main`. NexKit has mentioned the owner; tell them what you found. |
| Autopilot leaves an issue alone | `nexkit status` gives the reason. A command that never ran is not started again; after six hours it waits for a person, so post the command yourself. An issue opened without write access is never taken; open it again from the owner's account. |
