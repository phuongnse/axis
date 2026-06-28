---
name: axis-pull-request
description: Prepare, review, validate, create, update, or mark ready Axis pull requests. Use when the user asks to open a PR, run the pre-PR ready/review/publish flow, update PR title/body, convert a draft PR to ready, correct PR metadata, or publish a review-ready branch to GitHub.
---

# Axis Pull Request

## Goal

Own the PR publication boundary. This skill prepares the branch for publication, runs the pre-PR review checkpoint from [docs/playbooks/scripts.md](../../../docs/playbooks/scripts.md), validates the exact title/body before sending it to GitHub, and performs the requested PR action.

Do not perform branch readiness auditing here. Read `.cursor/skills/axis-ready-review/SKILL.md` (`$axis-ready-review`) first when readiness evidence is missing, stale, or not explicitly provided. The checkpoint is a required review step before PR publication, not a machine-enforced CI gate, unless the user explicitly asks to skip it.
Do not rerun local guards just to create a longer transcript. Run each required guard once, then rerun only when a subsequent change invalidates that evidence.

## Hard gates

Follow [reference.md § Publication gate](../reference.md#publication-gate-pr-boundary).
- Step 1 requires `$axis-ready-review` **Ready** before any PR publication step except metadata-only updates.
- Step 2 must close the pre-PR review checkpoint — including `$axis-review-feedback` when findings exist — before push or `gh pr create`.
- Do not push, open a PR, or mark ready while valid review findings remain open unless the user explicitly deferred each item.
- Do not describe open findings as follow-up, non-blocking, or later in PR metadata.

## Inputs

- Requested PR action: create, update, mark ready, or metadata-only update.
- Fresh ready-review evidence from `.cursor/skills/axis-ready-review/SKILL.md` (`$axis-ready-review`) unless the action is metadata-only.
- Exact title/body draft or enough summary, linked spec, and requirement evidence to draft one.

## Workflow

1. Confirm readiness evidence.
   - If the user asks to open a PR, update the published branch/diff, or mark a PR ready and no fresh ready-review result exists, read `.cursor/skills/axis-ready-review/SKILL.md` (`$axis-ready-review`) first.
   - If ready-review reports Not ready, stop before branch/diff PR actions or mark-ready and report the blocker.
   - If the user only asks to update PR metadata, readiness evidence is not required.

2. Run the pre-PR review checkpoint.
   - For create PR, branch/diff update, or mark-ready requests, follow [docs/playbooks/scripts.md § Pre-PR review checkpoint](../../../docs/playbooks/scripts.md#pre-pr-review-checkpoint) after readiness passes and before GitHub PR actions.
   - Metadata-only title/body updates do not require the checkpoint.
   - If the review raises issues or follow-up verification is needed, read `.cursor/skills/axis-review-feedback/SKILL.md` (`$axis-review-feedback`), resolve valid items, commit the follow-up, and rerun scoped to the checkpoint per scripts.md.
   - Do not push the branch or open the PR until checkpoint findings are resolved, explicitly deferred with user approval, or classified false positive with evidence.
   - Skip only when the user explicitly requested no pre-PR review, and record the skip reason in the PR requirements.

3. Determine the PR action.
   - Create PR: branch must be committed first; push only after step 2 is complete.
   - Update PR: identify the existing PR from the current branch or the user-provided PR number/URL.
   - Mark ready: update and validate metadata before changing draft status.
   - Metadata-only update: update title/body and validate the result.

4. Draft exact PR metadata before any GitHub action.
   - Title must be Conventional Commit style: `type(scope): subject` or `type: subject`.
   - Never rely on tool-generated prefixes in PR titles; write Conventional Commit style only.
   - Body must contain only:
     - `## Summary`
     - `## Linked spec`
     - `## Requirements & rules followed`
   - Do not include commit lists, CI transcripts, release notes, bot summaries, or extra sections.
   - The body may mention verification results inside the Requirements checklist, but must not paste long logs.

5. Validate the draft locally.
   - Write the exact body draft to a temporary file.
   - Run `python scripts/axis.py check pr --title "<exact title>" --body-file <draft-body-file>`.
   - If validation fails, revise the draft and rerun. Do not create, update, or mark ready while validation is failing.

6. Perform the PR action with exact metadata.
   - Create/update the PR using the exact validated title and body.
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
- create / update / mark-ready / metadata-update

Ready-review source:
- `.cursor/skills/axis-ready-review/SKILL.md` result, existing evidence, or N/A for metadata-only update

Pre-PR review checkpoint:
- ran / issues resolved / skipped with reason / blocked
- scope:
- reviewed checkpoint:
- follow-up review scope:

PR metadata:
- Title: ...
- Body sections: Summary / Linked spec / Requirements
- Draft validation: command -> pass/fail
- Post-push local checks: not run; CI owns post-push validation

GitHub:
- PR URL:
- Draft status:

Blocking issues:
- ...
```
