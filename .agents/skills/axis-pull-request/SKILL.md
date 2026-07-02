---
name: axis-pull-request
description: Prepare, review, validate, create, update, push to, or mark ready Axis pull requests. Use when the user asks to open a PR, run the pre-PR ready/review/publish flow, update PR title/body, push new commits to an existing PR branch, convert a draft PR to ready, correct PR metadata, or publish a review-ready branch to GitHub.
---

# Axis Pull Request

## Goal

Own the PR publication boundary. This skill prepares the branch for publication, decides whether the pre-PR review checkpoint from [docs/playbooks/scripts.md](../../../docs/playbooks/scripts.md) is triggered, validates PR metadata when metadata changes, and performs the requested PR action.

Do not perform branch readiness auditing here. Read `.agents/skills/axis-ready-review/SKILL.md` (`$axis-ready-review`) first when readiness evidence is missing, stale, or not explicitly provided. The checkpoint is required when triggered, not a machine-enforced CI gate, unless the user explicitly asks to skip it.
Do not rerun local guards just to create a longer transcript. Run each required guard once, then rerun only when a subsequent change invalidates that evidence.

## Hard gates

Follow [reference.md § Publication gate](../reference.md#publication-gate-pr-boundary).
- Step 1 requires `$axis-ready-review` **Ready** before any PR publication step except metadata-only updates.
- Step 2 must close the pre-PR review checkpoint when triggered — including `$axis-review-feedback` when findings exist — before push or `gh pr create`.
- Push-only updates to a published branch or existing PR branch are branch/diff updates; pre-push quick gates are not a substitute.
- Do not push, open a PR, or mark ready while valid review findings remain open unless the user explicitly deferred each item.
- Do not describe open findings as follow-up, non-blocking, or later in PR metadata.

## Inputs

- Requested PR action: create, branch/diff push update, mark ready, or metadata-only update.
- Fresh ready-review evidence from `.agents/skills/axis-ready-review/SKILL.md` (`$axis-ready-review`) unless the action is metadata-only.
- Exact title/body draft or enough summary, linked spec, and requirement evidence to draft one when metadata changes.

## Workflow

1. Confirm readiness evidence.
   - If the user asks to open a PR, push or update the published branch/diff, or mark a PR ready and no fresh ready-review result exists, read `.agents/skills/axis-ready-review/SKILL.md` (`$axis-ready-review`) first.
   - Treat a request to push the current branch as a branch/diff update when the branch has an upstream, an open PR, or explicit PR intent.
   - If ready-review reports Not ready, stop before branch/diff PR actions or mark-ready and report the blocker.
   - If the user only asks to update PR metadata, readiness evidence is not required.

2. Decide and run the pre-PR review checkpoint.
   - Trigger the checkpoint for non-trivial implementation, behavior, contract, or high-risk changes.
   - For docs-only, metadata-only, or small guidance/tooling-text changes, record `not triggered` with the reason.
   - When unsure, run the checkpoint.
   - When triggered for create PR, branch/diff update, or mark-ready requests, follow [docs/playbooks/scripts.md § Pre-PR review checkpoint](../../../docs/playbooks/scripts.md#pre-pr-review-checkpoint) after readiness passes and before GitHub PR actions.
   - Metadata-only title/body updates do not require the checkpoint.
   - When triggered and the review raises issues or follow-up verification is needed, read `.agents/skills/axis-review-feedback/SKILL.md` (`$axis-review-feedback`), resolve valid items, commit the follow-up, and rerun scoped to the checkpoint per scripts.md.
   - Do not push the branch or open the PR until checkpoint findings are resolved, explicitly deferred with user approval, or classified false positive with evidence.
   - Skip only when the user explicitly requested no pre-PR review, and record the skip reason in the PR requirements.

3. Determine the PR action.
   - Create PR: branch must be committed first; push only after step 2 is complete.
   - Branch/diff push update: identify the existing PR from the current branch or user input when present; no metadata draft is needed unless metadata changes.
   - Mark ready: update and validate metadata before changing draft status.
   - Metadata-only update: update title/body and validate the result.

4. Draft exact PR metadata before any PR metadata action.
   - Title must be Conventional Commit style: `type(scope): subject` or `type: subject`.
   - Never rely on tool-generated prefixes in PR titles; write Conventional Commit style only.
   - Body must contain only:
     - `## Summary`
     - `## Linked spec`
     - `## Requirements & rules followed`
   - Do not include commit lists, CI transcripts, release notes, bot summaries, or extra sections.
   - The body may mention verification results inside the Requirements checklist, but must not paste long logs.

5. Validate the draft locally when metadata changes.
   - Write the exact body draft to a temporary file.
   - Run `python scripts/axis.py check pr --title "<exact title>" --body-file <draft-body-file>`.
   - If validation fails, revise the draft and rerun. Do not create, update, or mark ready while validation is failing.
   - For push-only branch/diff updates with no metadata changes, record `N/A`.

6. Perform the PR action with exact metadata.
   - Push the branch or create/update the PR only after steps 1-5 are satisfied.
   - Create/update PR metadata using the exact validated title and body.
   - When using a GitHub tool or CLI, pass the exact title/body explicitly; do not accept generated defaults.
   - Keep the PR draft unless the user asked to mark ready and readiness evidence is current.

7. Hand off after the GitHub action.
   - Do not rerun local verification or PR-body guards after pushing or creating/updating the PR; CI owns post-push checks.
   - Report the PR URL, draft status, and which pre-action validations ran.
   - If GitHub rejects the action or reports a metadata mutation/error, treat that as a new metadata-only update instead of adding an after-push guard loop.

## Output

Report:

```text
PR action:
- create / branch-diff-update / mark-ready / metadata-update

Ready-review source:
- `.agents/skills/axis-ready-review/SKILL.md` result, existing evidence, or N/A for metadata-only update

Pre-PR review checkpoint:
- ran / issues resolved / not triggered with reason / skipped with reason / blocked
- scope:
- reviewed checkpoint:
- follow-up review scope:

PR metadata:
- Title: ... / N/A for push-only branch/diff update
- Body sections: Summary / Linked spec / Requirements & rules followed / N/A
- Draft validation: command -> pass/fail / N/A
- Post-push local checks: not run; CI owns post-push validation

GitHub:
- PR URL:
- Draft status:

Blocking issues:
- ...
```
