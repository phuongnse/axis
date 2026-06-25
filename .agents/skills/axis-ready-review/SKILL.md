---
name: axis-ready-review
description: Prepare an Axis branch for review by auditing changed paths, running triggered verification once, checking docs/status/workarounds, and producing an honest PR-readiness summary. Use when deciding whether branch work is ready for review.
---

# Axis Ready Review

## Goal

Decide whether the branch is ready for review. Run evidence once, avoid transcript inflation, and never turn missing evidence into a green claim.

## Workflow

1. Inspect the diff.
   - Run `git status --short`.
   - Run `git diff --name-only HEAD`.
   - Classify paths as docs/skills/scripts/source/tests/API/frontend/visuals.

2. Reconcile the Design Gate.
   - Confirm `$axis-design-gate` was used for non-trivial work.
   - Confirm user sign-off happened before code for high-risk surfaces.
   - Self-review the diff against the dossier: governing rules, blast radius, contract decision, and verification plan.

3. Audit product evidence.
   - Behavior/status changes need the owning use-case callout updated.
   - Implemented/refreshed use cases need AC IDs and an Acceptance Test Matrix.
   - Every in-scope AC must map to automated evidence or an exact deferral.
   - Required AT expectations must cite a spec section, AC ID, or flow step in the implementation/verification report.
   - `Deferred follow-ups` must name exact ACs or say `N/A`.
   - Intentional P0/P1 shortcuts must appear in `docs/WORKAROUNDS.md` and at the site.

4. Run verification at the boundary.
   - Run narrow checks only when they are missing, stale, or needed to debug failure.
   - Skill changes: run skill-creator validation when available, then `python scripts/axis.py check codex-skills`.
   - Before PR review, run `python scripts/axis.py verify` once when triggered.
   - If `verify` fails, report Not ready and list the failing step.
   - If full local suite is claimed, it must include the relevant repo suite for touched surfaces, with integration/API tests when applicable.

5. Keep review-only audits honest.
   - AC map complete: pass/fail/N/A with reason.
   - Docs review: updated/not triggered/deferred with owner.
   - Retrospective review: answer each line from `docs/playbooks/agent-checklist.md`.
   - Enforcement status: use `docs/REVIEW_FINDINGS.md` terms; do not call review-only work a gate.

6. Hand off PR publication separately.
   - If Ready and the user wants a PR action, use `$axis-pull-request`.
   - Do not draft PR metadata or perform GitHub actions in this skill.

## Output

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
- Docs review: ...
- Retrospective review: ...

Blocking issues:
- ...
```
