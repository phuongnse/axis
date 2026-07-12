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
- Dirty or missing checkpoint evidence is **Not ready**.
- Failed, missing, or stale required evidence cannot become a green claim.

## Inputs

- Clean committed checkpoint and comparison base.
- Design Gate/sign-off evidence when triggered.
- Product AC/AT, docs/status, and verification evidence relevant to the diff.

## Workflow

1. Inspect `git status --short` and the committed diff from its merge base; classify changed path owners and stack manifests.
2. Reconcile the diff with the Design Gate, sign-off, retirement, and contract decisions.
3. Audit product evidence only when behavior/status is touched: AC coverage, implementation status, evidence sidecar, and exact deferrals.
4. Run `python scripts/axis.py ready-review` once, or `--since <checkpoint>` for an immutable follow-up delta. Debug failures with narrow checks only.
5. Apply [reference.md § Improvement loop](../reference.md#improvement-loop) and [docs/playbooks/agent-checklist.md](../../../docs/playbooks/agent-checklist.md); update one owner only when evidence justifies promotion or retirement.
6. Return the verdict and evidence to the caller. Publication is a separate user-authorized workflow.

## Output

Report verdict, changed owners, command result, Design Gate/AC/docs review, improvement outcome, blockers, and every deferral with its exact scope and supporting evidence.
