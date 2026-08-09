---
name: axis-review-readiness
description: Decide whether an immutable Axis checkpoint is ready for independent review. Use to audit changed paths, reconcile Design Gate and product evidence, run review-boundary verification once, and report readiness without reviewing, committing, or publishing.
---

# Axis Review Readiness

## Goal

Return an evidence-backed **Ready** or **Not ready** verdict before independent review begins.

## Hard gates

Follow [reference.md](../reference.md).
- Do not create commits, draft PR metadata, push, or publish.
- The implementation checkpoint must be clean with respect to tracked files; normal ignored build/test artifacts or temporary files created by review commands do not make it dirty.
- Failed, missing, or stale required evidence cannot become a green claim.
- This workflow does not perform independent review. A caller obtains the configured reviewer only after a **Ready** verdict and reuses a completed result for the exact same checkpoint.

## Inputs

- Clean committed checkpoint and comparison base; ignored verification artifacts are allowed during review.
- Design Gate/sign-off evidence when triggered.
- Product AC/AT, docs/status, and verification evidence relevant to the diff.

## Workflow

1. Inspect `git status --short` and the committed diff from its merge base; classify changed path owners and stack manifests.
2. Reconcile the diff with the Design Gate, sign-off, retirement, and contract decisions. For frontend UI work, run `python scripts/axis.py check ui-foundation`; reject a prose/status claim ahead of the manifest phase, an unfrozen consumer migration, or a frozen phase without explicit visual acceptance for this checkpoint.
3. Audit product evidence only when behavior/status is touched: AC coverage, implementation status, evidence sidecar, and exact deferrals. Build the review verification set from the diff owners and their acceptance evidence; run missing or invalidated focused browser commands, never the full Playwright suite unless the diff is cross-cutting across every browser surface.
4. Audit minimality after correctness: prefer existing code, the standard library, native platform capabilities, and installed dependencies before custom code; reject speculative abstractions, dependencies, flags, or files without weakening required safety, accessibility, or ACs.
5. Run `python scripts/axis.py review-readiness` once, or `--since <checkpoint>` for an immutable follow-up delta. Debug failures with narrow checks only.
6. Apply [reference.md § Improvement loop](../reference.md#improvement-loop) and [docs/playbooks/agent-checklist.md](../../../docs/playbooks/agent-checklist.md); update one owner only when evidence justifies promotion or retirement.
7. Return the verdict and evidence to the caller. Independent review and publication are separate workflows.

## Output

Report verdict, changed owners, command result, Design Gate/AC/docs review, improvement outcome, blockers, and every deferral with its exact scope and supporting evidence.
