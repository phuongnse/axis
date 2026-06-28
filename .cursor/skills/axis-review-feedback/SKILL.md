---
name: axis-review-feedback
description: Handle Axis PR review feedback safely. Use when addressing automated pre-PR review output, GitHub review bot or human review comments, follow-up review commits, requested changes, or suggestions that may affect design, tests, docs, or PR readiness.
---

# Axis Review Feedback

## Goal

Resolve review feedback by improving the codebase, not merely silencing a thread or making CI green.

## Hard gates

Follow [reference.md](../reference.md).
- Resolve or classify every finding before returning to `$axis-pull-request` or asking for another review pass.
- Do not claim ready while triggered verification is failing.
- When pre-PR review raised findings, return to pull-request only after commit and scoped rerun per [scripts.md § Pre-PR review checkpoint](../../../docs/playbooks/scripts.md#pre-pr-review-checkpoint).

## Inputs

- Review comments or tool output, including severity and affected file/line when available.
- Reviewed checkpoint or branch diff that produced the feedback.
- User constraints: minimal fix, broader cleanup, or follow-up review expectation.

## Workflow

1. Gather the feedback.
   - Read the review comment, affected diff, and surrounding code.
   - If comments come from a tool, treat them as signal to validate against Axis rules.
   - If feedback came from a local automated review run without a GitHub thread, preserve the issue text, severity, and file path in your working notes.
   - Before editing, record the reviewed checkpoint when follow-up review is expected:
     - For committed work, record `git rev-parse HEAD`.
     - For uncommitted work, commit the reviewed state first or stop and report that a true delta-only checkpoint rerun is unavailable.

2. Classify each item.
   - Correctness bug or missing AC.
   - Boundary or architecture concern.
   - Test gap or weakened assertion.
   - Docs/status drift.
   - Style, readability, or generated-file issue.
   - False positive, with evidence.

3. Re-read the governing source.
   - [AGENTS.md](../../../AGENTS.md)
   - [docs/playbooks/agent-checklist.md](../../../docs/playbooks/agent-checklist.md)
   - The focused playbook for the touched surface, such as [docs/playbooks/api-patterns.md](../../../docs/playbooks/api-patterns.md), [docs/playbooks/frontend.md](../../../docs/playbooks/frontend.md), or [docs/playbooks/testing.md](../../../docs/playbooks/testing.md)
   - The owning use-case and tests when behavior is involved
   - [docs/ENFORCEMENT.md](../../../docs/ENFORCEMENT.md) when the issue looks repeatable

4. Prefer the defensible change.
   - Look for the existing module pattern before accepting a reviewer-proposed simplification.
   - Improve ownership, transaction boundaries, error handling, or test coverage when that is the real issue.
   - Keep tests semantically honest: strengthen assertions or rename tests when the stated behavior and proved behavior drift apart.
   - If the user explicitly asked for the smallest change, keep the diff minimal and say so.
   - If a better resolution is deliberately deferred, record an exact `Deferred follow-ups` line.

5. Generalize the lesson.
   - For each valid finding, decide whether it is a one-off defect, missing deterministic check, missing workflow rule, stale doc/status rule, or retired-surface cleanup miss.
   - When the issue class can recur, improve the owning skill, wrapper, checker, or test in a general form before asking for another review.
   - Do not add example-specific denylist checks, compatibility notes, or "do not use old name" prose for retired surfaces; apply `$axis-design-gate`'s retirement contract and sweep old identifiers instead.
   - Keep lessons broad enough to prevent the class of mistake, but do not create new product behavior, stack policy, or use-case scope from a review comment alone.

6. Verify the touched surface.
   - Run the narrow test or policy check for the files changed.
   - When a reviewed checkpoint exists, prefer `python scripts/axis.py verify --since <reviewed-checkpoint>` so follow-up verification is scoped to the changed delta plus working tree.
   - Run `$axis-ready-review` before asking for another review pass.
   - If this feedback was part of `$axis-pull-request`, return control to that skill with the reviewed checkpoint, the files changed by the follow-up, and whether the follow-up is committed.
   - When rerunning the pre-PR review checkpoint after follow-up changes, review only the follow-up delta when possible per [docs/playbooks/scripts.md § Pre-PR review checkpoint](../../../docs/playbooks/scripts.md#pre-pr-review-checkpoint). Do not rerun the full branch diff and describe it as follow-up-only review.

7. Report resolution.
   - Mark each comment as resolved, improved beyond suggestion, false positive with evidence, or deferred with owner.
   - Do not claim ready status while triggered verification is failing.
   - Include the generalized lesson or say `N/A: one-off defect`.
   - Include the follow-up review scope: `follow-up delta`, `full diff with reason`, or `not rerun`.

## Output

Report review follow-ups as `improved` or `minimal`, list generalized lessons, verification, retired-identifier sweep results when applicable, and any unresolved or deferred comments.
