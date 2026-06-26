---
name: axis-script-scope
description: Choose and run Axis scripts with the right scope and timing. Use when deciding whether to run repo commands, checks, tests, verify, pre-push, doc-drift, frontend/dotnet wrappers, local-dev scripts, generated-contract commands, or when editing script guidance.
---

# Axis Script Scope

## Goal

Run enough evidence for the current context without turning development into local CI.

## Inputs

- Current moment: exploring, inner loop, ready-review boundary, or CI/debugging.
- Changed paths or intended script/policy surface.
- Checks already run and whether any subsequent diff invalidated them.

## Workflow

1. Classify the moment.
   - Exploring: prefer read-only commands such as `rg`, `git status`, `git diff`, and targeted file reads.
   - Inner loop: run the smallest command that proves the edit.
   - Boundary: use `$axis-ready-review` before review; it owns `python scripts/axis.py verify` when triggered.
   - CI/debugging: run broader checks only to reproduce or diagnose a failing gate.

2. Pick the narrow check.
   - Docs: focused docs check; use `$axis-doc-hygiene` when changing docs.
   - Skills: when available, run skill-creator `scripts/quick_validate.py <skill-folder>`, then `python scripts/axis.py check codex-skills`.
   - Scripts/policy: focused script/policy test, then `python scripts/axis.py check policy-tests` when rule wiring changed.
   - Backend: targeted test or `python scripts/axis.py dotnet build`; unit tests when behavior changed.
   - Frontend: focused Vitest/Playwright or `python scripts/axis.py frontend ci` for type/lint risk.
   - API contract: regenerate/check generated contracts only when route/request/response shape changed.
   - Review follow-up: when a reviewed checkpoint exists, run `python scripts/axis.py verify --since <checkpoint>` so the wrapper selects checks from the follow-up delta plus working tree.

3. Avoid waste.
   - Do not run `verify` after every small edit.
   - Do not run full `.NET` test suite unless debugging CI, high-risk backend behavior, or claiming full-suite evidence.
   - Do not run local-dev stack unless the workflow needs external services or smoke evidence.
   - Do not rerun passing checks unless the diff changed in a way that invalidates them.
   - Do not rerun full branch review or full branch verification for a review follow-up when a checkpoint-specific delta is available.

4. Use wrappers.
   - Document repo workflows through `python scripts/axis.py ...`.
   - Keep raw Docker, dotnet, npm, Lychee, and OpenSSL commands inside wrappers or package scripts.
   - If a needed wrapper is missing, add it through [scripts/axis.py](../../../scripts/axis.py) instead of documenting a raw command.
   - Encode current invariants in deterministic checks.
   - When removing, renaming, replacing, dropping, disabling, deprecating, or otherwise retiring any supported surface, use `$axis-design-gate`'s retirement contract. Sweep old symbols, env vars, config keys, commands, flags, paths, service names, IDs, routes, markers, headings, fixtures, and artifacts before editing and again before final reporting.
   - Do not keep migration helpers, fallback branches, denylist checks, compatibility notes, docs, or tests that exist only to mention retired identifiers unless an owner doc or explicit user decision requires a compatibility exception.

5. Report honestly.
   - For each relevant check, say ran, not triggered with reason, failed with blocker, or skipped by explicit user request.
   - Never present pre-push or unit-only feedback as full ready-review verification.

## Output

Report why each command was chosen, what was intentionally not run, retired-identifier sweep results or accepted compatibility exceptions, and the next boundary check if review readiness is the goal.
