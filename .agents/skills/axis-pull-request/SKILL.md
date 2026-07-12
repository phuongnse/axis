---
name: axis-pull-request
description: Publish or update Axis pull-request branches and metadata. Use for branch creation or rename, PR creation, diff pushes, draft-to-ready changes, metadata updates, and the user-authorized pre-publication review loop.
---

# Axis Pull Request

## Goal

Own the user-authorized publication state machine: immutable readiness evidence, triggered review, feedback closure, exact metadata, then GitHub action.

## Hard gates

Follow [reference.md](../reference.md).
- Non-metadata publication **Requires** a current `$axis-ready-review` **Ready** result.
- This workflow **Delegates** triggered review findings to `$axis-review-feedback`; they **Return to** this workflow with fresh delta evidence.
- Do not push, create, or mark ready while required evidence or valid findings remain open.
- Branches follow [CONTRIBUTING.md § Branches and commits](../../../CONTRIBUTING.md#branches-and-commits); project convention overrides tool defaults.
- Metadata-only updates may bypass code readiness but still require exact metadata validation.

## Inputs

- User-authorized action: create, branch/diff update, mark ready, or metadata-only update.
- Current branch/PR state and readiness evidence.
- Exact title/body or enough source evidence to draft them.

## Workflow

1. Classify the requested action, authorization, and branch state. Create or rename branches through the contributing owner before readiness.
2. For publication actions, obtain a clean committed checkpoint; this workflow **Delegates** readiness to `$axis-ready-review` and stops on **Not ready**.
3. Decide the pre-PR review checkpoint using [docs/playbooks/scripts.md § Pre-PR review checkpoint](../../../docs/playbooks/scripts.md#pre-pr-review-checkpoint). Review the publishable commit; this workflow **Delegates** valid findings to `$axis-review-feedback`; rerun readiness/review only for the immutable follow-up delta.
4. When metadata changes, draft a Conventional Commit title and only `Summary`, `Linked spec`, and `Requirements & rules followed` body sections. Validate the exact branch and draft with `python scripts/axis.py check pr`.
5. Perform only the requested GitHub action with the validated metadata. Keep draft status unless the user requested ready state.
6. After publication, report remote state and let CI own post-push checks; do not rerun local guards without a new diff or failure.

## Output

Report action, readiness source, review checkpoint/result, metadata validation, PR URL/status, and blockers.
