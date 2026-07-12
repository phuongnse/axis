---
name: axis-script-scope
description: Select or change Axis repository commands and checks. Use for inner-loop proof, review-boundary command selection, scripts/axis.py, policy checks, wrappers, local-dev tooling, generation, or CI command wiring.
---

# Axis Script Scope

## Goal

Run the smallest evidence that proves the current edit and keep repeatable commands behind one Axis wrapper.

## Hard gates

Follow [reference.md](../reference.md).
- Do not substitute a broad suite for missing targeted evidence.
- `$axis-ready-review` owns immutable review-boundary verification.
- Report omitted checks with a reason.

## Inputs

- Current moment: exploration, inner loop, review boundary, or CI debugging.
- Changed paths and evidence already run.
- Owner playbook for the touched surface.

## Workflow

1. Classify the moment. Exploration is read-only; inner loop uses focused proof; review uses `$axis-ready-review`; CI debugging reproduces only the failing boundary.
2. Select by surface: focused docs/skills/policy checks, related .NET tests, focused Vitest/Playwright, contract generation/parity, or the local-dev wrappers defined by [docs/playbooks/local-dev.md](../../../docs/playbooks/local-dev.md).
3. Avoid waste: do not rerun valid evidence, full suites, containers, or browser journeys unless the changed risk invalidates narrower proof.
4. Use `python scripts/axis.py ...` for repo workflows. Add a missing reusable wrapper in [scripts/axis.py](../../../scripts/axis.py); keep package-native commands inside their owner wrapper.
5. Encode only deterministic reusable invariants. Apply [reference.md § Improvement loop](../reference.md#improvement-loop) before adding or removing a check.

## Output

Report selected commands and why, intentionally omitted evidence, results, and the next verification boundary.
