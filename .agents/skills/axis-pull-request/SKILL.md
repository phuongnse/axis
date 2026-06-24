---
name: axis-pull-request
description: Prepare, review, validate, create, update, or mark ready Axis pull requests. Use when the user asks to open a PR, run the pre-PR ready/review/publish flow, update PR title/body, convert a draft PR to ready, fix PR metadata, or publish a review-ready branch to GitHub.
---

# Axis Pull Request

## Goal

Own the PR publication boundary. This skill prepares the branch for publication, runs the local CodeRabbit plugin review checkpoint, validates the exact title/body that will be sent to GitHub, performs the requested PR action, and validates the resulting GitHub PR again.

Do not perform branch readiness auditing here. Use `$axis-ready-review` first when readiness evidence is missing, stale, or not explicitly provided. Do not treat CodeRabbit as a machine gate; it is a required review checkpoint before PR publication unless the user explicitly asks to skip it.
Do not rerun local guards just to create a longer transcript. Run each required guard once, then rerun only when a subsequent change invalidates that evidence.

## Workflow

1. Confirm readiness evidence.
   - If the user asks to open, update, or mark a PR ready and no fresh ready-review result exists, run `$axis-ready-review` first.
   - If `$axis-ready-review` reports Not ready, stop before PR actions and report the blocker.
   - If the user only asks to fix PR metadata, readiness evidence is not required.

2. Run the pre-PR CodeRabbit review checkpoint.
   - For create PR, update PR, or mark-ready requests, use the CodeRabbit plugin review skill (`$coderabbit:code-review`) after readiness passes and before GitHub PR actions.
   - Confirm the CLI is available through `python scripts/axis.py check coderabbit-cli` when fresh toolchain evidence is missing.
   - Review the same branch diff that will be published. Prefer the committed branch diff when the branch is already committed; otherwise review the uncommitted diff before committing.
   - If CodeRabbit raises issues, use `$axis-review-feedback` to classify and fix them, then rerun the touched verification checks and `$axis-ready-review`.
   - Rerun CodeRabbit once after review fixes when the fixes materially change implementation behavior, contracts, docs policy, or tests.
   - If CodeRabbit is unavailable, unauthenticated, fails, or times out, stop and report the blocker. Skip only when the user explicitly requested no CodeRabbit review, and record the skip reason in the PR requirements.

3. Determine the PR action.
   - Create PR: branch must be committed and pushed first.
   - Update PR: identify the existing PR from the current branch or the user-provided PR number/URL.
   - Mark ready: update and validate metadata before changing draft status.
   - Metadata-only fix: update title/body and validate the result.

4. Draft exact PR metadata before any GitHub action.
   - Title must be Conventional Commit style: `type(scope): subject` or `type: subject`.
   - Never rely on tool-generated prefixes such as `[codex]`.
   - Body must contain only:
     - `## Summary`
     - `## Linked spec`
     - `## Requirements & rules followed`
   - Do not include commit lists, CI transcripts, release notes, bot summaries, or extra sections.
   - The body may mention verification results inside the Requirements checklist, but must not paste long logs.

5. Validate the draft locally.
   - Write the exact body draft to a temporary file.
   - Run `python scripts/axis.py check pr --title "<exact title>" --body-file <draft-body-file>`.
   - If validation fails, fix the draft and rerun. Do not create, update, or mark ready while validation is failing.

6. Perform the PR action with exact metadata.
   - Create/update the PR using the exact validated title and body.
   - When using a GitHub tool or CLI, pass the exact title/body explicitly; do not accept generated defaults.
   - Keep the PR draft unless the user asked to mark ready and readiness evidence is current.

7. Validate GitHub state after the action.
   - Read the PR title/body back from GitHub.
   - If the PR metadata differs from the validated draft, run `python scripts/axis.py check pr --title "<github title>" --body-file <github-body-file>`.
   - If a tool or bot inserted extra PR-body content, remove it when possible and revalidate.
   - Report any remaining external mutation as a blocker instead of claiming the PR is clean.

## Output

Report:

```text
PR action:
- create / update / mark-ready / metadata-fix

Ready-review source:
- $axis-ready-review result, existing evidence, or N/A for metadata-only fix

CodeRabbit review:
- ran / issues fixed / skipped with reason / blocked
- scope:

PR metadata:
- Title: ...
- Body sections: Summary / Linked spec / Requirements
- Draft validation: command -> pass/fail
- GitHub post-action validation: pass by exact metadata match / command -> pass/fail

GitHub:
- PR URL:
- Draft status:

Blocking issues:
- ...
```
