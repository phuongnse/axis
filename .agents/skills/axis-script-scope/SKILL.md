---
name: axis-script-scope
description: Select or change Axis repository commands, environment bootstrap, and checks. Use for setup/doctor profiles, inner-loop proof, review-boundary command selection, scripts/axis.py, policy checks, wrappers, local-dev tooling, generation, or CI command wiring.
---

# Axis Script Scope

## Goal

Run the smallest evidence that proves the current edit and keep repeatable commands behind one Axis wrapper.

## Hard gates

Follow [reference.md](../reference.md).
- Do not substitute a broad suite for missing targeted evidence.
- `$axis-ready-review` owns immutable review-boundary verification.
- Editing durable guidance **Requires** entering `$axis-doc-hygiene` before edit; reuse an active handoff supplied by the caller.
- Report omitted checks with a reason.

## Inputs

- Current moment: bootstrap, diagnosis, exploration, inner loop, review boundary, or CI debugging.
- Changed paths and evidence already run.
- Owner playbook for the touched surface.

## Workflow

1. Classify the moment. Bootstrap runs `python scripts/axis.py setup --profile build` or the required cumulative profile; use `--plan-only` before downloads and `--install-user-tools` only with user authorization. Classify known external native prerequisites from observed failure signatures; print an exact host action only for a verified OS/version and never mutate the OS. When local dev must serve a host browser, surface the trust-store decision and add `--trust-local-ca` only with explicit authorization. Other diagnosis selects the narrowest `doctor --profile`; exploration is read-only; inner loop uses focused proof; review uses `$axis-ready-review`; CI debugging reproduces only the failing boundary.
2. Select by surface: focused docs/skills/policy checks, related .NET tests, focused Vitest/Playwright, the JSON dependency audit plus accepted-risk manifest, contract generation/parity, or the local-dev wrappers defined by [docs/playbooks/local-dev.md](../../../docs/playbooks/local-dev.md).
3. Avoid waste: do not rerun valid evidence, full suites, containers, or browser journeys unless the changed risk invalidates narrower proof. After `local-dev up`, require its readiness result; when host-browser access is in scope, verify HTTPS through the host trust stack before claiming the system is usable.
4. Use `python scripts/axis.py ...` for repo workflows. Add a missing reusable wrapper in [scripts/axis.py](../../../scripts/axis.py); keep coherent policy logic in a focused `scripts/axis_*_policy.py` module and package-native commands inside their owner wrapper.
5. Encode only deterministic reusable invariants. Apply [reference.md § Improvement loop](../reference.md#improvement-loop) before adding or removing a check.

## Output

Report these fields:

- `Moment`
- `Selected checks` with reasons
- `Omitted broad checks` with reasons
- `Results`
- `Next verification boundary`
