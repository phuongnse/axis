---
name: axis-pull-request
description: Prepare, review, validate, create, update, or mark ready Axis pull requests. Use when the user asks to open a PR, run the pre-PR ready/review/publish flow, update PR title/body, convert a draft PR to ready, correct PR metadata, or publish a review-ready branch to GitHub.
---

# Axis Pull Request

## Goal

Own the PR publication boundary. This skill prepares the branch for publication, runs the local CodeRabbit plugin review checkpoint, validates the exact title/body before sending it to GitHub, and performs the requested PR action.

Do not perform branch readiness auditing here. Use `$axis-ready-review` first when readiness evidence is missing, stale, or not explicitly provided. Do not treat CodeRabbit as a machine gate; it is a required review checkpoint before PR publication unless the user explicitly asks to skip it.
Do not rerun local guards just to create a longer transcript. Run each required guard once, then rerun only when a subsequent change invalidates that evidence.

## Workflow

1. Confirm readiness evidence.
   - If the user asks to open, update, or mark a PR ready and no fresh ready-review result exists, run `$axis-ready-review` first.
   - If `$axis-ready-review` reports Not ready, stop before PR actions and report the blocker.
   - If the user only asks to update PR metadata, readiness evidence is not required.

2. Run the pre-PR CodeRabbit review checkpoint.
   - For create PR, update PR, or mark-ready requests, use the CodeRabbit plugin review skill (`$coderabbit:code-review`) after readiness passes and before GitHub PR actions.
   - Confirm the CLI is available through `python scripts/axis.py check coderabbit-cli` when fresh toolchain evidence is missing.
   - Review the same branch diff that will be published. Prefer the committed branch diff when the branch is already committed; otherwise review the uncommitted diff before committing.
   - If CodeRabbit raises issues, treat the output as review feedback: classify it before changing code, resolve the valid items, then rerun the touched verification checks and `$axis-ready-review`.
   - Before handing off CodeRabbit issues, record the reviewed checkpoint. If the reviewed diff is uncommitted, commit that reviewed state before applying follow-up changes when a delta-only rerun is expected.
   - Rerun CodeRabbit once after review follow-up changes when they materially change implementation behavior, contracts, docs policy, or tests. Scope the rerun to the follow-up delta by using the reviewed checkpoint as `--base-commit`; add `--dir` only as an extra narrowing filter for affected directories. If a true delta-only rerun is impossible because there was no checkpoint, say so and do not present a full-diff rerun as follow-up-only.
   - If CodeRabbit is unavailable, unauthenticated, fails, or times out, stop and report the blocker. Skip only when the user explicitly requested no CodeRabbit review, and record the skip reason in the PR requirements.

3. Determine the PR action.
   - Create PR: branch must be committed and pushed first.
   - Update PR: identify the existing PR from the current branch or the user-provided PR number/URL.
   - Mark ready: update and validate metadata before changing draft status.
   - Metadata-only update: update title/body and validate the result.

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
- $axis-ready-review result, existing evidence, or N/A for metadata-only update

CodeRabbit review:
- ran / issues resolved / skipped with reason / blocked
- scope:

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
