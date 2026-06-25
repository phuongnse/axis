---
name: axis-script-scope
description: Choose and run Axis scripts with the right scope and timing. Use when deciding whether to run repo commands, checks, tests, verify, pre-push, doc-drift, frontend/dotnet wrappers, local-dev scripts, generated-contract commands, or when editing script guidance.
---

# Axis Script Scope

## Goal

Run enough evidence for the current context without turning development into local CI.

## Workflow

1. Classify the moment.
   - Exploring: prefer read-only commands such as `rg`, `git status`, `git diff`, and targeted file reads.
   - Inner loop: run the smallest command that proves the edit.
   - Boundary: use `$axis-ready-review` before review; it owns `python scripts/axis.py verify` when triggered.
   - CI/debugging: run broader checks only to reproduce or diagnose a failing gate.

2. Pick the narrow check.
   - Docs: focused docs check; use `$axis-doc-hygiene` when changing docs.
   - Skills: skill-creator validation plus `python scripts/axis.py check codex-skills`.
   - Scripts/policy: focused script/policy test, then `python scripts/axis.py check policy-tests` when rule wiring changed.
   - Backend: targeted test or `python scripts/axis.py dotnet build`; unit tests when behavior changed.
   - Frontend: focused Vitest/Playwright or `python scripts/axis.py frontend ci` for type/lint risk.
   - API contract: regenerate/check generated contracts only when route/request/response shape changed.

3. Avoid waste.
   - Do not run `verify` after every small edit.
   - Do not run full `.NET` test suite unless debugging CI, high-risk backend behavior, or claiming full-suite evidence.
   - Do not run local-dev stack unless the workflow needs external services or smoke evidence.
   - Do not rerun passing checks unless the diff changed in a way that invalidates them.

4. Use wrappers.
   - Document repo workflows through `python scripts/axis.py ...`.
   - Keep raw Docker, dotnet, npm, Lychee, and OpenSSL commands inside wrappers or package scripts.
   - If a needed wrapper is missing, add it through `scripts/axis.py` instead of documenting a raw command.

5. Report honestly.
   - For each relevant check, say ran, not triggered with reason, failed with blocker, or skipped by explicit user request.
   - Never present pre-push or unit-only feedback as full ready-review verification.

## Output

Report why each command was chosen, what was intentionally not run, and the next boundary check if review readiness is the goal.
