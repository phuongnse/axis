---
name: axis-nexkit
description: Runbook for the owner's local agent session that works NexKit for Axis when the owner asks (writing issues and their order, breaking a milestone down, proposing NexKit setting changes, upgrading NexKit, looking at a stopped issue). Use when the owner asks to plan, file, split or check work through NexKit, or asks about the state of a NexKit issue or pull request. Not for the hourly driver routine, which uses axis-nexkit-routine, and not for agents running inside the NexKit pipeline.
---

# Working NexKit for Axis

Axis's rules are in [docs/delivery.md](../../../docs/delivery.md). How NexKit
works, and what a person does around it, are in NexKit's own docs and skills
at the ref Axis pins. This runbook says only how to apply Axis's rules: where
the values live, how to write Axis's issues and milestones, and how to upgrade
NexKit for Axis. It does not repeat either, so follow its links.

Autopilot and the hourly driver keep the work moving. Act when the owner asks.
Ask the owner first when a choice rests on a **Proposed** decision or only the
owner can answer it. Never approve, merge or close a pull request yourself.

## Where the values live

Look these up each time. Do not copy them into notes or docs, because they
change with a NexKit release or a settings change.

- **NexKit repository and version:** the `nexkit_repository` and `nexkit_ref`
  inputs in the workflow file.

  ```bash
  grep -E 'nexkit_(repository|ref):' .github/workflows/nexkit.yml
  ```

- **NexKit's docs and skills at that ref:** clone NexKit at the pinned ref and
  read `docs/` and `skills/` there. Use its `status` skill to see what runs,
  what waits and why, and its `request` skill for the rules of marks between
  issues.

  ```bash
  REPO=$(grep -oE 'nexkit_repository: \S+' .github/workflows/nexkit.yml | cut -d' ' -f2)
  REF=$(grep -oE 'nexkit_ref: \S+' .github/workflows/nexkit.yml | cut -d' ' -f2)
  KIT="${TMPDIR:-/tmp}/nexkit-$REF"
  [ -d "$KIT" ] || git clone -q --depth 1 --branch "$REF" "https://github.com/$REPO" "$KIT"
  ```

- **Settings:** `.nexkit/config.json`. A key that is missing takes NexKit's
  default, from its configuration doc at the pinned ref.

  ```bash
  python3 -c "import json; c = json.load(open('.nexkit/config.json')); print(json.dumps({k: c.get(k) for k in ('fix', 'merge', 'conflicts', 'usage_limit', 'close_parent_issues', 'autopilot', 'notify')}, indent=1))"
  python3 -c "import json; c = json.load(open('.nexkit/config.json')); print(json.dumps({k: c.get(k) for k in ('default_profile', 'profiles', 'cache')}, indent=1))"
  ```

- **Required checks on `main`:** the active rules for the branch.

  ```bash
  gh api repos/{owner}/{repo}/rules/branches/main \
    --jq '.[] | select(.type=="required_status_checks") | .parameters.required_status_checks[].context'
  ```

## Writing an issue

The rules are in
[the life of an issue](../../../docs/delivery.md#the-life-of-an-issue), in
[writing on GitHub](../../../docs/delivery.md#writing-on-github), and, for
marks between issues, in NexKit's `request` skill.

1. Read the doc sections the issue implements. Write the behaviour, by role,
   with 2–5 acceptance criteria, and name the concepts it touches in the
   terms of [concepts.md](../../../docs/domain/concepts.md).
2. Read the labels with `gh label list` and pick every one whose description
   fits.
3. Create it in the milestone that delivers it:
   `gh issue create --title "<behaviour>" --milestone "<milestone>" --label "<a>,<b>" --body-file <file>`.
4. Mark what it waits for, as NexKit's `request` skill says, before the next
   sweep, or autopilot plans it at once. When the milestone's docs sweep is
   open, mark it *blocked by* the new issue too.

## Breaking down a milestone

When the owner asks, give the readiness verdicts of
[D68](../../../docs/decisions/d68-issues-of-several-milestones-run-at-once.md)
first, with the decisions, docs and open issues behind each. On *ready*:

- Create the milestone's issues as in [Writing an issue](#writing-an-issue).
- Check that each risky concept the **From** column of
  [concepts.md](../../../docs/domain/concepts.md) lists for the new milestone
  falls under `hard` by its consequence, and that work the platform has not
  done before, such as a new kind of editor, widget or library, is not
  described by the `standard` text.
- Compare the profiles' results in the ending milestone and check their
  models against the models now available, as NexKit's configuration doc says
  for writing profiles.
- Propose any change to the owner as a hand-made pull request.

## Upgrading NexKit

An upgrade is one hand-made pull request. Follow NexKit's upgrade steps in its
configuration doc, then:

1. Name every key left at its default in the pull request, with the reason.
2. Change `docs/delivery.md`, this runbook and the
   [`axis-nexkit-routine`](../axis-nexkit-routine/SKILL.md) skill wherever the
   release changes a setting Axis chooses or a choice NexKit leaves to Axis.
3. Say in the pull request what changes, what the release brings that needs
   nothing here, and what the owner does: merge with the admin bypass, and any
   secret or setting only the owner can add.

## When something stops

The hourly driver handles NexKit's stops, as NexKit's `drive` skill and
troubleshooting doc say. When the owner asks you to look at one, read
`nexkit status` and that doc at the pinned ref. A hand merge follows
[the routine's steps](../axis-nexkit-routine/SKILL.md#hand-merges); ask the
owner before you push it.
