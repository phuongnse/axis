---
name: axis-pull-request
description: Prepare, validate, create, update, or mark ready Axis pull requests. Use when the user asks to open a PR, update PR title/body, convert a draft PR to ready, fix PR metadata, or publish a review-ready branch to GitHub.
---

# Axis Pull Request

## Goal

Own the PR publication boundary. This skill prepares the exact title/body that will be sent to GitHub, validates that metadata with the same PR guard CI runs, performs the requested PR action, and validates the resulting GitHub PR again.

Do not perform branch readiness auditing here. Use `$axis-ready-review` first when readiness evidence is missing, stale, or not explicitly provided.

## Workflow

1. Confirm readiness evidence.
   - If the user asks to open, update, or mark a PR ready and no fresh ready-review result exists, run `$axis-ready-review` first.
   - If `$axis-ready-review` reports Not ready, stop before PR actions and report the blocker.
   - If the user only asks to fix PR metadata, readiness evidence is not required.

2. Determine the PR action.
   - Create PR: branch must be committed and pushed first.
   - Update PR: identify the existing PR from the current branch or the user-provided PR number/URL.
   - Mark ready: update and validate metadata before changing draft status.
   - Metadata-only fix: update title/body and validate the result.

3. Draft exact PR metadata before any GitHub action.
   - Title must be Conventional Commit style: `type(scope): subject` or `type: subject`.
   - Never rely on tool-generated prefixes such as `[codex]`.
   - Body must contain only:
     - `## Summary`
     - `## Linked spec`
     - `## Requirements & rules followed`
   - Do not include commit lists, CI transcripts, release notes, bot summaries, or extra sections.
   - The body may mention verification results inside the Requirements checklist, but must not paste long logs.

4. Validate the draft locally.
   - Write the exact body draft to a temporary file.
   - Run `python scripts/axis.py check pr --title "<exact title>" --body-file <draft-body-file>`.
   - If validation fails, fix the draft and rerun. Do not create, update, or mark ready while validation is failing.

5. Perform the PR action with exact metadata.
   - Create/update the PR using the exact validated title and body.
   - When using a GitHub tool or CLI, pass the exact title/body explicitly; do not accept generated defaults.
   - Keep the PR draft unless the user asked to mark ready and readiness evidence is current.

6. Validate GitHub state after the action.
   - Read the PR title/body back from GitHub.
   - Run `python scripts/axis.py check pr --title "<github title>" --body-file <github-body-file>`.
   - If a tool or bot inserted extra PR-body content, remove it when possible and revalidate.
   - Report any remaining external mutation as a blocker instead of claiming the PR is clean.

## Output

Report:

```text
PR action:
- create / update / mark-ready / metadata-fix

Ready-review source:
- $axis-ready-review result, existing evidence, or N/A for metadata-only fix

PR metadata:
- Title: ...
- Body sections: Summary / Linked spec / Requirements
- Draft validation: command -> pass/fail
- GitHub post-action validation: command -> pass/fail

GitHub:
- PR URL:
- Draft status:

Blocking issues:
- ...
```
