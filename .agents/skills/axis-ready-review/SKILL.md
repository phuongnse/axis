---
name: axis-ready-review
description: Prepare an Axis branch for review by auditing changed paths, running the triggered verification commands, checking docs/status/workarounds, and producing an honest PR-readiness summary. Use when deciding whether branch work is ready for review.
---

# Axis Ready Review

## Goal

Decide whether the branch is ready for review using Axis enforced checks plus review-only self-audits. Do not turn missing evidence into a green claim.

## Workflow

1. Inspect the diff.
   - Run `git status --short`.
   - Run `git diff --name-only HEAD` to classify changed paths.
   - Re-read `docs/playbooks/agent-checklist.md` and `docs/REVIEW_FINDINGS.md` for triggered checks and enforcement status.

2. Reconcile the Design Gate.
   - Confirm a dossier exists for non-trivial work.
   - For high-risk changes, confirm the user signed off before code.
   - Self-review the diff against the dossier: rules, blast radius, contract decisions, and verification plan.

3. Audit use-case and docs status.
   - For behavior/spec/status changes, verify the owning use-case callout, domain README, and `docs/PROGRESS.md` when triggered.
   - For use cases being implemented, closed, or materially refreshed, verify local AC IDs and the `## Acceptance Test Matrix` exist.
   - Confirm every in-scope AC appears in at least one required AT row and every required AT row names an automated runner/tool.
   - Confirm the use-case matrix stays high-level: no test file paths, class names, commands, or `Evidence source` implementation-detail column.
   - Confirm the implementation/verification report cites the spec section, AC ID, or flow step behind each required AT expected behavior.
   - Confirm the implementation report includes `Spec Readiness Verdict: Ready`; if any required expectation cannot be cited from the spec, mark the branch Not ready.
   - Confirm the verification report includes pass/fail evidence by AT ID.
   - Verify `Deferred follow-ups` names exact AC bullets or says `N/A`.
   - Check `docs/WORKAROUNDS.md` when an intentional P0/P1 workaround ships.

4. Run enforced checks by changed path.
   - Skill changes: run skill-creator `quick_validate.py` for each changed skill, then run `python scripts/axis.py check codex-skills`.
   - Docs/scripts/layout/policy: run `python scripts/axis.py check policy-tests` and `python scripts/axis.py check doc-drift`.
   - Frontend: run `python scripts/axis.py frontend ci` and `python scripts/axis.py frontend test`.
   - Source/tests: run `python scripts/axis.py dotnet build`, `python scripts/axis.py check test-naming`, and `python scripts/axis.py test unit`.
   - API contract: regenerate API contracts and run the API tests named by `agent-checklist.md`.
   - Before PR review, run `python scripts/axis.py verify` unless the change is clearly outside its triggers.

5. Keep review-only items honest.
   - AC map has no blank in-scope rows.
   - Acceptance Test Matrix has no uncovered in-scope ACs and no required AT row without an automated runner.
   - Required AT expectations have spec citations in the implementation/verification report.
   - Docs review lines are `updated`, `not triggered`, or a named deferral.
   - Retrospective review answers each line, especially repeat findings and new enforceable rules.

6. Hand off PR publication separately.
   - If the branch is Ready and the user wants to open, update, or mark a PR ready, use `$axis-pull-request`.
   - Do not draft PR title/body or perform GitHub PR actions in this skill.

## Output

Use this summary:

```text
Ready status:
- Ready / Not ready

Changed path classes:
- ...

Verification:
- command -> pass/fail/not run with reason

Review-only audits:
- Design Gate: ...
- AC map: ...
- Acceptance Test Matrix: ...
- Docs review: ...
- Retrospective review: ...

Blocking issues:
- ...
```
