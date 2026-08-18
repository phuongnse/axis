---
name: axis-pull-request
description: Publish or update Axis pull-request branches and metadata. Use for branch creation or rename, PR creation, diff pushes, draft-to-ready changes, metadata updates, and the user-authorized pre-publication review loop.
---

# Axis Pull Request

## Goal

Own the user-authorized publication state machine: immutable readiness evidence, triggered review, feedback closure, exact metadata, then GitHub action.

## Hard gates

Follow [reference.md](../reference.md).
- Non-metadata publication **Requires** a current `$axis-review-readiness` **Ready** result.
- A non-trivial publishable diff **Requires** the configured independent reviewer; primary verification is not the review.
- Never start or delegate independent review while readiness is pending or running. The **Ready** result must name the exact immutable checkpoint and comparison base that the reviewer receives.
- This workflow **Delegates** triggered review findings to `$axis-review-feedback`; they **Return to** this workflow with fresh delta evidence.
- Do not push, create, or mark ready while required evidence or valid findings remain open.
- Branches and commit subjects follow [CONTRIBUTING.md § Branches and commits](../../../CONTRIBUTING.md#branches-and-commits); project convention overrides tool defaults, and `check publish-metadata` must pass before readiness or push.
- Metadata-only updates may bypass code readiness but still require exact metadata validation.

## Inputs

- User-authorized action: create, branch/diff update, mark ready, or metadata-only update.
- Current branch/PR state and readiness evidence.
- Exact title/body or enough source evidence to draft them.

## Workflow

1. Classify the requested action, authorization, and branch state. Create or rename branches through the contributing owner before readiness.
2. For publication actions, obtain focused proof from the implementation owner, then create one clean immutable checkpoint through `python scripts/axis.py git checkpoint --branch <branch> --subject <subject>`. Do not create the checkpoint before the focused proof is current; the wrapper rejects invalid branch or commit metadata before mutation.
3. Decide the comparison base using [docs/playbooks/scripts.md § Pre-PR review checkpoint](../../../docs/playbooks/scripts.md#pre-pr-review-checkpoint), then **Delegates** readiness to `$axis-review-readiness` with the exact checkpoint and base. Stop on **Not ready** and keep review unstarted until the **Ready** verdict returns for that pair.
4. Only after **Ready**, obtain the configured independent reviewer for the exact checkpoint/base unless a completed review already covers that pair. This workflow **Delegates** valid findings to `$axis-review-feedback`; findings return with focused proof, then repeat clean checkpoint → scoped readiness → review only for the immutable follow-up delta.
5. When metadata changes, draft a Conventional Commit title and only `Summary`, `Linked spec`, and `Requirements & rules followed` body sections. Validate the exact branch and draft with `python scripts/axis.py check pr`.
6. Perform only the requested GitHub action with the validated metadata. Keep draft status unless the user requested ready state.
7. After publication, report remote state and let CI own post-push checks; do not rerun local guards without a new diff or failure.

## Output

Report action, readiness source, review checkpoint/result, metadata validation, PR URL/status, and blockers.
