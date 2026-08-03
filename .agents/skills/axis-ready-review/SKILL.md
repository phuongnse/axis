---
name: axis-ready-review
description: Decide whether an immutable Axis checkpoint is ready for review. Use to audit changed paths, reconcile Design Gate and product evidence, run review-boundary verification once, and report readiness without committing or publishing.
---

# Axis Ready Review

## Goal

Return an evidence-backed **Ready** or **Not ready** verdict for an immutable checkpoint.

## Hard gates

Follow [reference.md](../reference.md).
- Do not create commits, draft PR metadata, push, or publish.
- The implementation checkpoint must be clean with respect to tracked files; normal ignored build/test artifacts or temporary files created by review commands do not make it dirty.
- Failed, missing, or stale required evidence cannot become a green claim.
- Non-trivial implementation or cross-surface changes **Require** the configured independent reviewer; primary self-review is not a substitute. If that reviewer is unavailable, record the limitation and use another independent reviewer only when that preserves the user's intent.
- A delegated reviewer reported as `running` or `pending`, or a bounded wait that returns no result, is **Review pending**, not a verdict. Keep the reviewer alive and continue waiting until a final result or explicit runtime failure; do not close it merely because a wait window elapsed.
- Review read-only reuses current primary evidence and permits only the smallest focused command needed for a finding or missing or invalidated evidence. It forbids repeating passing routine suites and intentional edits to tracked source, tests, contracts, migrations, docs, Git state, or PR state.

## Inputs

- Clean committed checkpoint and comparison base; ignored verification artifacts are allowed during review.
- Design Gate/sign-off evidence when triggered.
- Product AC/AT, docs/status, and verification evidence relevant to the diff.

## Workflow

1. Inspect `git status --short` and the committed diff from its merge base; classify changed path owners and stack manifests.
2. Reconcile the diff with the Design Gate, sign-off, retirement, and contract decisions.
3. Audit product evidence only when behavior/status is touched: AC coverage, implementation status, evidence sidecar, and exact deferrals. Build the review verification set from the diff owners and their acceptance evidence; run missing or invalidated focused browser commands, never the full Playwright suite unless the diff is cross-cutting across every browser surface.
4. Audit minimality after correctness: prefer existing code, the standard library, native platform capabilities, and installed dependencies before custom code; reject speculative abstractions, dependencies, flags, or files without weakening required safety, accessibility, or ACs.
5. Delegate an independent review of the immutable implementation diff to the configured reviewer. Pass the current verification evidence and explicitly retain routine-check ownership on the primary. Keep the review alive while it is `running`; the reviewer inspects the diff and runs only a smallest reproducer for a finding or evidence gap. Treat only its final result as review evidence. Classify every finding by severity and resolve or explicitly defer it with user-approved evidence. Primary verification does not replace this review.
6. Run `python scripts/axis.py ready-review` once, or `--since <checkpoint>` for an immutable follow-up delta. Debug failures with narrow checks only.
7. Apply [reference.md § Improvement loop](../reference.md#improvement-loop) and [docs/playbooks/agent-checklist.md](../../../docs/playbooks/agent-checklist.md); update one owner only when evidence justifies promotion or retirement.
8. Return the verdict and evidence to the caller. Publication is a separate user-authorized workflow.

## Output

Report verdict, changed owners, command result, Design Gate/AC/docs review, improvement outcome, blockers, and every deferral with its exact scope and supporting evidence.
