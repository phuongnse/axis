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
   - `docs/playbooks/patterns-index.md` plus the focused pattern owner, or `docs/playbooks/frontend.md` for frontend implementation patterns
   - The owning use-case and tests when behavior is involved
   - `docs/REVIEW_FINDINGS.md` when the issue looks repeatable

4. Prefer the defensible fix.
   - Look for the existing module pattern before accepting a reviewer-proposed shortcut.
   - Improve ownership, transaction boundaries, error handling, or test coverage when that is the real issue.
   - If the user explicitly asked for the smallest fix, keep the diff minimal and say so.
   - If a better fix is deliberately deferred, record an exact `Deferred follow-ups` line.

5. Verify the touched surface.
   - Run the narrow test or policy check for the files changed.
   - Run `$axis-ready-review` before asking for another review pass.
   - If this feedback was part of `$axis-pull-request`, return control to that skill so it can decide whether to rerun CodeRabbit before PR publication.

6. Report resolution.
   - Mark each comment as fixed, improved beyond suggestion, false positive with evidence, or deferred with owner.
   - Do not claim ready status while triggered verification is failing.

## Output

Report review fixes as `improved` or `minimal`, list verification, and name any unresolved or deferred comments.
