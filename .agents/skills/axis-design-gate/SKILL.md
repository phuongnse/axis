---
name: axis-design-gate
description: Prepare the Axis Design Gate dossier before non-trivial code changes. Use when a task changes Axis source, tests, contracts, docs status, repo tooling, scripts/axis.py, docker-compose, local-dev, cross-platform stack behavior, frontend behavior, API endpoints, EF migrations, schema, auth, removes or renames supported surfaces, or any surface where AGENTS.md requires a Design Gate artifact before implementation.
---

# Axis Design Gate

## Goal

Produce the pre-code dossier required by [docs/playbooks/design-gate.md](../../../docs/playbooks/design-gate.md). Scale the dossier to the risk: compact for ordinary standard work, full for high-risk, retirement, contract, or broad blast-radius changes.

Do not edit implementation files before the required dossier scale is complete. For high-risk surfaces, stop after the dossier and ask the user for sign-off before coding.

## Hard gates

Follow [reference.md](../reference.md).
- Do not edit implementation files until the dossier output is complete.
- High-risk: stop after the dossier until user sign-off — no code.
- Trivial-only bypass must state why no dossier is needed in the output.

## Inputs

- User request, intended files, and touched surfaces.
- Governing owner docs for the touched surface.
- Blast-radius search terms for affected symbols, contracts, docs, tests, and generated artifacts.
- Retired or renamed identifiers when the request removes, drops, replaces, disables, or stops supporting anything.

## Workflow

1. Classify the change.
   - Trivial: typo, comment, or tiny doc-only edit. State why no dossier is needed.
   - Standard compact: localized source, test, docs/status, guidance, or existing-contract frontend work with no retirement, no wire/schema/auth/stack change, and no broad cross-surface blast radius. Produce a compact dossier and continue.
   - Standard full: non-high-risk work that retires or renames a supported surface, changes deterministic checks or workflow behavior, spans multiple ownership surfaces, or has a broad blast radius. Produce a full dossier and continue.
   - High-risk: new or changed endpoint, contract, required field, schema or migration, auth, new/replaced runtime, framework, service, major library, or public API surface. Produce a full dossier and wait for user sign-off.

2. Read the governing docs for the exact surface.
   - Always read [AGENTS.md](../../../AGENTS.md), [docs/playbooks/design-gate.md](../../../docs/playbooks/design-gate.md), and [docs/playbooks/agent-checklist.md](../../../docs/playbooks/agent-checklist.md).
   - Read only the relevant surface docs: [docs/playbooks/api-patterns.md](../../../docs/playbooks/api-patterns.md), [docs/playbooks/frontend.md](../../../docs/playbooks/frontend.md), [docs/playbooks/testing.md](../../../docs/playbooks/testing.md), or the owning use-case file.
   - For stack/library changes, read [docs/TECH_STACK.md](../../../docs/TECH_STACK.md) and the touched version owner manifest before deciding whether sign-off is required.

3. Quote governing rules.
   - Use `path:section` references.
   - Quote only the rule text needed for the surface. Compact dossiers usually need one to three rule bullets.
   - Separate enforced checks from review-only expectations using [docs/ENFORCEMENT.md](../../../docs/ENFORCEMENT.md) terms when status matters.

4. Run blast-radius searches before editing.
   - Prefer `rg`.
   - Compact dossiers need the smallest search that proves the touched owner surface and terminology.
   - Full dossiers search every affected symbol, endpoint route, DTO, handler, field name, test fixture, frontend type, retired identifier, and generated artifact.
   - For stack/library changes, include [docs/TECH_STACK.md](../../../docs/TECH_STACK.md) and touched manifests ([global.json](../../../global.json), [Directory.Packages.props](../../../Directory.Packages.props), [frontend/package.json](../../../frontend/package.json), [docker-compose.yml](../../../docker-compose.yml)) in the blast-radius summary.
   - Paste the exact command and summarize the hits. If no search applies, write `N/A because ...`.

5. Apply the retirement contract only when removing or renaming.
   - Trigger on user intent such as remove, delete, drop, no fallback, no legacy, stop using, replace, rename, retire, deprecate, or no longer needed.
   - List every retired identifier: symbols, env vars, config keys, commands, flags, paths, files, service names, IDs, endpoints, DTO fields, feature branches, docs anchors, tests, fixtures, generated artifacts, and user-facing names.
   - Default to zero current-source references after the edit. Do not keep compatibility shims, migrations, fallbacks, denylist checks, legacy tests, stale docs, or "do not use old name" guidance unless an owner doc or explicit user decision requires compatibility.
   - If compatibility is required, document the owner, expiry/removal condition, and proving test. Otherwise delete tests and docs that exist only to preserve the retired name.
   - Plan a post-edit repo-wide `rg` sweep for retired identifiers. Final reporting must say zero matches or name each accepted compatibility exception.
   - Compact dossiers may write `N/A because no supported surface is retired`.

6. Decide the contract shape.
   - Name request/response DTOs, API casing, and FE/BE generated type parity when applicable.
   - If no wire contract is touched, write `N/A because no wire shape changes`.

7. Plan verification.
   - Name narrow checks via `$axis-script-scope` and before-review work via `.agents/skills/axis-ready-review/SKILL.md` (`$axis-ready-review`).
   - Do not call a review-only artifact a gate.

## Output

Use the smallest output shape that satisfies the risk:

- Trivial bypass: `Design Gate: bypassed as trivial`, reason, triggered verification.
- Standard compact: surface, risk reason, one to three rule refs, one blast-radius search, contract/retirement `N/A` or decision, verification, sign-off.
- Standard full / high-risk: affected modules/layers, risk reason, governing rules, blast radius, retirement contract, contract decision, plan, verification, risks/ambiguities, sign-off.

Full dossiers may still be concise; include only facts needed to govern the edit.
