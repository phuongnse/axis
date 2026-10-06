---
name: axis-nexkit
description: Runbook for the local agent session that drives NexKit for the Axis owner (posting commands, reading plans, triaging pull requests, splitting issues). Use when the owner asks to plan, implement, check, fix or split work through NexKit, or asks about the state of a NexKit issue or pull request. Not for agents running inside the NexKit pipeline.
---

# Driving NexKit for Axis

The rules, the roles and the life of an issue are in
[docs/delivery.md](../../../docs/delivery.md). Read it first. This file holds
only the practical parts: where values live, the `gh` commands, two review
checklists and what to do when something fails.

You may post `/nexkit plan`, `/nexkit fix` and `/nexkit review` yourself. Post
`/nexkit go` only when the owner says so explicitly. Never approve a pull
request, approve a waiting CI run or merge. Those are the owner's.

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

- **Protected paths and the automatic fix limit:** `.nexkit/config.json`. When a
  key is `null`, the value is NexKit's default, from its configuration doc at
  the pinned ref.

  ```bash
  jq '.protected_paths, .max_auto_fixes' .nexkit/config.json
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

Post a command:

```bash
gh issue comment <N> --body "/nexkit plan"
gh pr comment <N> --body "/nexkit fix <instructions>"
```

Read the latest plan. It is the newest NexKit comment on the issue:

```bash
gh issue view <N> --comments
```

List open NexKit pull requests with their review decision and check status:

```bash
PREFIX=$(grep -oE "head\.ref, '[^']+'" .github/workflows/nexkit.yml | sed -E "s/.*'([^']+)'/\1/")
gh pr list --state open --json number,title,headRefName,reviewDecision,statusCheckRollup \
  --jq ".[] | select(.headRefName | startswith(\"$PREFIX\"))"
```

Find CI runs that wait for approval, then give each run URL to the owner:

```bash
gh run list --status action_required --json databaseId,displayTitle,headBranch,url
```

Split an issue that reports `too_large`. Create each part with
`gh issue create --milestone "<parent's milestone>"`, then add the parts to the
parent in order:

```bash
CHILD_ID=$(gh api repos/{owner}/{repo}/issues/<child> --jq .id)
gh api -X POST repos/{owner}/{repo}/issues/<parent>/sub_issues -F sub_issue_id="$CHILD_ID"
```

`sub_issue_id` is the issue's database `id`, not its number. If the endpoint
returns 404, upgrade `gh` and try again.

## Checklist: reviewing a plan

- The issue is one behaviour with 2–5 acceptance criteria, and each one can be
  checked by a test or command.
- Every doc link in the issue and the plan resolves.
- No **Proposed** decision in [decisions.md](../../../docs/decisions.md) is
  treated as **Agreed**.
- The decisions and risks the owner must judge are in their own short sections.
- A `too_large` result leads to sub-issues of the parent, in order and in the
  parent's milestone.
- Fixes are commented in the same issue, and `/nexkit plan` is run again there.
  No follow-up issues.

## Checklist: reviewing a pull request

- The behaviour change matches the issue and the approved plan.
- When agreed behaviour changes, the docs are updated in the same pull request.
- Tests cover the change, including denied access where it applies.
- The required checks on `main` are green.
- No edits in paths NexKit may not change, unless the pull request is hand-made.
- Remind the owner that approval and merging are theirs.

## When something fails

| Failure | What the local agent does |
| --- | --- |
| Publish fails after `/nexkit go` | Read the run log. If the base branch was force-pushed, the same `/nexkit go` fixes it. Otherwise diagnose first. Ask the owner before posting `/nexkit go` again. |
| A round ends as `blocked` | Answer the question with `/nexkit fix <decision>`. Ask the owner first when the choice is theirs. |
| Merge conflict with `main` | Comment `/nexkit fix`. NexKit merges `main` into the branch. Answer any `blocked` question it asks. |
| Conflict in a path NexKit may not change, or a merge that brings workflow changes | Merge `origin/main` into the pull request branch by hand, in a separate worktree. Run `scripts/build.sh`, `scripts/lint.sh` and `scripts/test.sh`. Ask the owner before pushing, then comment `/nexkit review`. |
| The review job errored | Comment `/nexkit review` to retry. |
| The review requested changes | NexKit runs automatic fix rounds up to the configured limit. After that, ask the owner and post `/nexkit fix <instructions>`. |
| A CI run waits as `action_required` | Remind the owner and give them the run URL. |

For the mechanics behind each failure, read NexKit's troubleshooting doc at the
pinned ref, found as described under "Where the values live".
