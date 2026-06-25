---
name: axis-review-feedback
description: Handle Axis PR review feedback safely. Use when addressing local CodeRabbit plugin output, GitHub CodeRabbit or human review comments, follow-up review commits, requested changes, or suggestions that may affect design, tests, docs, or PR readiness.
---

# Axis Review Feedback

## Goal

Resolve review feedback by improving the codebase, not merely silencing a thread or making CI green.

## Workflow

1. Gather the feedback.
   - Read the review comment, affected diff, and surrounding code.
   - If comments come from a tool, treat them as signal to validate against Axis rules.
   - If feedback came from the local CodeRabbit plugin, preserve the issue text, severity, and file path in your working notes; there may be no GitHub thread to resolve.
   - Before editing, record the reviewed checkpoint when follow-up review is expected:
     - For committed work, record `git rev-parse HEAD`.
     - For uncommitted work, either commit the reviewed state first or report that a true delta-only CodeRabbit rerun is unavailable.

2. Classify each item.
   - Correctness bug or missing AC.
   - Boundary or architecture concern.
   - Test gap or weakened assertion.
   - Docs/status/workaround drift.
   - Style, readability, or generated-file issue.
   - False positive, with evidence.

3. Re-read the governing source.
   - `AGENTS.md`
   - `docs/playbooks/agent-checklist.md`
   - The focused playbook for the touched surface, such as `docs/playbooks/api-patterns.md`, `docs/playbooks/frontend.md`, or `docs/playbooks/testing.md`
   - The owning use-case and tests when behavior is involved
   - `docs/REVIEW_FINDINGS.md` when the issue looks repeatable

4. Prefer the defensible change.
   - Look for the existing module pattern before accepting a reviewer-proposed shortcut.
   - Improve ownership, transaction boundaries, error handling, or test coverage when that is the real issue.
   - Keep tests semantically honest: strengthen assertions or rename tests when the stated behavior and proved behavior drift apart.
   - If the user explicitly asked for the smallest change, keep the diff minimal and say so.
   - If a better resolution is deliberately deferred, record an exact `Deferred follow-ups` line.

5. Verify the touched surface.
   - Run the narrow test or policy check for the files changed.
   - Run `$axis-ready-review` before asking for another review pass.
   - If this feedback was part of `$axis-pull-request`, return control to that skill with the reviewed checkpoint, the files changed by the follow-up, and whether the follow-up is committed.
   - When rerunning local CodeRabbit after follow-up changes, review only the follow-up delta when possible: use the checkpoint SHA as the `--base-commit` and optionally add `--dir` only to narrow affected directories further. Do not rerun the full branch diff and describe it as follow-up-only review.

6. Report resolution.
   - Mark each comment as resolved, improved beyond suggestion, false positive with evidence, or deferred with owner.
   - Do not claim ready status while triggered verification is failing.
   - Include the follow-up review scope: `follow-up delta`, `full diff with reason`, or `not rerun`.

## Output

Report review follow-ups as `improved` or `minimal`, list verification, and name any unresolved or deferred comments.
