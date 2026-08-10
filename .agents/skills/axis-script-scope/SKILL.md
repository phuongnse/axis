---
name: axis-script-scope
description: Own Axis repository command selection, execution, wrappers, environment bootstrap, checks, local-dev tooling, and CI wiring. Use before any repository command that is not read-only inspection, or when changing those command surfaces.
---

# Axis Script Scope

## Goal

Run the smallest evidence that proves the current edit and keep repeatable commands behind one Axis wrapper.

## Hard gates

Follow [reference.md](../reference.md).
- Do not substitute a broad suite for missing targeted evidence.
- Run checks only for touched surfaces. During the inner loop, run the focused proof owned by the current writer; run a triggered broad suite once at the immutable review boundary.
- One agent owns each check. Reuse current checkpoint evidence across agents and do not repeat a passing check unless a later edit or concrete finding invalidates it.
- Do not run multiple .NET build, test, format, migration, or generation workflows concurrently in one checkout unless every workflow has explicitly isolated `obj` and `bin` outputs. Shared MSBuild artifacts make concurrent results invalid; sequence those commands and parallelize only independent non-.NET surfaces.
- `$axis-review-readiness` owns immutable review-boundary verification.
- Editing durable guidance **Requires** entering `$axis-doc-hygiene` before edit; reuse an active handoff supplied by the caller.
- Report omitted checks with a reason.
- Native read-only inspection is allowed. Repeatable repository workflows, repo-owned tools, and verification evidence require a finite `python scripts/axis.py ...` route. A rare one-off mutation without a route requires explicit approval of the exact command and targets; recurrence requires a route. A pass-through shell never counts as a finite route or evidence.
- General shell access remains inspection-only unless the exact rare-mutation exception above is approved.
- Apply the blocker protocol to every missing or unusable command/runtime prerequisite. Report the required prerequisite, dependent check, and exact host action needed; keep that evidence blocked until the required route succeeds. Approval of an alternate action does not change its evidence status. Never install, extract, symlink, prepend, substitute a user-local runtime/tool, change `PATH`, or use an alternate environment to make a check pass.
- Keep dependency changes controlled: exact direct versions, generated lockfiles, repository vulnerability gates, and Renovate-owned proposals. Do not use ranges, manual lockfile edits, compatibility shims, or risk acceptance as an update mechanism.
- When a command fails, classify any proposed substitute against the owning contract using [reference.md § Universal gates](../reference.md#universal-gates). A substitute that changes the required execution or evidence boundary to keep progressing returns to `$axis-design-gate` and cannot turn the original boundary green.

## Inputs

- Current moment: bootstrap, diagnosis, exploration, inner loop, review boundary, or CI debugging.
- Changed paths and evidence already run.
- Owner playbook for the touched surface.

## Workflow

1. Classify the moment. Bootstrap runs `python scripts/axis.py setup --profile build` or the required cumulative profile; use `--plan-only` before downloads and `--install-user-tools` only with user authorization. Classify known external native prerequisites from observed failure signatures; print an exact host action only for a verified OS/version and never mutate the OS. For external command prerequisites, distinguish a missing tool from an installed command in a discovered user directory that is not active in `PATH`; report the discovered location and shell-refresh action without turning a host-specific path into durable guidance. When local dev must serve a host browser, surface the trust-store decision and add `--trust-local-ca` only with explicit authorization. Other diagnosis selects the narrowest `doctor --profile`; exploration is read-only; inner loop uses focused proof; review uses `$axis-review-readiness`; CI debugging reproduces only the failing boundary.
2. Select by surface: focused docs/skills/policy checks, related .NET tests, focused Vitest/Playwright, exact dependency-version policy plus the JSON dependency audit and accepted-risk manifest, contract generation/parity, or the local-dev wrappers defined by [docs/playbooks/local-dev.md](../../../docs/playbooks/local-dev.md). Every frontend UI diff runs `python scripts/axis.py check ui-foundation`; every frontend diff runs both dependency gates so local review matches CI; the scheduled workflow covers dormant default-branch graphs. For browser work, use one affected title or file in the inner loop. At review, run journeys triggered by the diff and owning acceptance evidence; use the full Playwright suite only in CI or when a cross-cutting change invalidates every browser surface.
3. Avoid waste: map checks to touched paths, assign each selected check to one owner, and reuse its checkpoint result. Do not run checks for untouched surfaces or rerun valid evidence, full suites, containers, or browser journeys unless a later edit or concrete finding invalidates narrower proof. After `local-dev up`, require its readiness result; when host-browser access is in scope, verify HTTPS through the host trust stack before claiming the system is usable.
4. Before execution, classify it as native read-only inspection, a repeatable Axis workflow, or a rare mutation. Use or add the smallest finite [scripts/axis.py](../../../scripts/axis.py) route for repeatable work. Run a rare mutation natively only after exact command-and-target approval; do not create a one-use wrapper or disguise it through pass-through shell access. Keep coherent policy logic in a focused `scripts/axis_*_policy.py` module and package-native commands inside their owner wrapper.
5. Encode only deterministic reusable invariants. Apply [reference.md § Improvement loop](../reference.md#improvement-loop) before adding or removing a check.

## Output

Report these fields:

- `Moment`
- `Selected checks` with reasons
- `Execution proof` listing the exact Axis invocations used
- `Omitted broad checks` with reasons
- `Results`
- `Next verification boundary`
