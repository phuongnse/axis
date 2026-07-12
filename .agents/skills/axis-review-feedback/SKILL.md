---
name: axis-review-feedback
description: Resolve Axis automated or human review findings. Use for local review output, PR comments, requested changes, false positives, and follow-up deltas that need classification, correction, and fresh evidence.
---

# Axis Review Feedback

## Goal

Resolve each finding against current owners, improve the underlying class when reusable, and return delta evidence to the caller.

## Hard gates

Follow [reference.md](../reference.md).
- Classify every finding as fixed, false positive with evidence, or explicitly user-deferred.
- Do not claim readiness while required verification fails.
- Preserve the reviewed checkpoint before changing a follow-up delta.

## Inputs

- Finding text, severity, file/line, and reviewed checkpoint.
- Caller and expected return state.
- User-approved constraints or deferrals.

## Workflow

1. Read the finding, affected diff, surrounding code, and governing ACs, contracts, docs, and code. Surface conflicts for a decision instead of treating review text as authoritative.
2. Classify correctness, boundary, test, docs/status, generated output, readability, or false positive.
3. Implement the smallest defensible resolution; preserve behavior, safety, and test honesty.
4. Apply [reference.md § Improvement loop](../reference.md#improvement-loop) to valid findings. Promote a rule only when the evidence supports a reusable decision or invariant, even if first observed once.
5. Run focused proof, commit only when the caller authorized a follow-up change, and produce delta-ready evidence for `$axis-ready-review`.
6. Return to the caller with the reviewed checkpoint, changed paths, classifications, evidence, and unresolved decisions; do not auto-publish.

## Output

Report each finding outcome, improvement outcome, delta scope, verification, and exact deferrals.
