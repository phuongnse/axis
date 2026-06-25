---
name: axis-design-gate
description: Prepare the Axis Design Gate dossier before non-trivial code changes. Use when a task changes Axis source, tests, contracts, docs status, repo tooling, frontend behavior, API endpoints, migrations, auth, or any surface where AGENTS.md requires a Design Gate artifact before implementation.
---

# Axis Design Gate

## Goal

Produce the pre-code dossier required by `docs/playbooks/design-gate.md`. Keep it concrete: quoted rules, blast-radius searches, contract decisions, and verification commands.

Do not edit implementation files before this dossier is complete. For high-risk surfaces, stop after the dossier and ask the user for sign-off before coding.

## Workflow

1. Classify the change.
   - Trivial: typo, comment, or tiny doc-only edit. State why no dossier is needed.
   - Standard: intra-module logic, tests, repo tooling, docs status, or existing-API frontend work. Produce a short dossier and continue.
   - High-risk: new or changed endpoint, contract, required field, schema or migration, auth, new library, or public API surface. Produce a full dossier and wait for user sign-off.

2. Read the governing docs for the exact surface.
   - Always read `AGENTS.md`, `docs/playbooks/design-gate.md`, and `docs/playbooks/agent-checklist.md`.
   - Read only the relevant surface docs: `process.md`, `api-patterns.md`, `frontend.md`, `testing.md`, `WORKAROUNDS.md`, or the owning use-case file.

3. Quote governing rules.
   - Use `file:section` references.
   - Quote only the rule text needed for the surface.
   - Separate enforced checks from review-only expectations using `docs/REVIEW_FINDINGS.md` terms when status matters.

4. Run blast-radius searches before editing.
   - Prefer `rg`.
   - Search every affected symbol, endpoint route, DTO, handler, field name, test fixture, and frontend type.
   - Paste the exact command and summarize the hits. If no search applies, write `N/A because ...`.

5. Decide the contract shape.
   - Name request/response DTOs, API casing, and FE/BE generated type parity when applicable.
   - If no wire contract is touched, write `N/A because no wire shape changes`.

6. Plan verification.
   - Name the narrow development checks.
   - Name the ready-review gate commands from `docs/playbooks/agent-checklist.md`.
   - Do not call a review-only artifact a gate.

## Output

Use this shape before implementation:

```text
Affected module(s) and layer(s):
- ...

Risk tier:
- Trivial / Standard / High-risk
- Reason: ...

Governing rules:
- `path:section` - "short quoted rule"

Blast radius:
- `rg -n "..." ...` -> summary of hits

Contract decision:
- Request/response/casing: ...
- FE/BE type parity: ...
- N/A reason: ...

Plan:
- ...

Verification:
- During development: ...
- Before review: ...

Risks / ambiguities:
- ...

Sign-off:
- Required / Not required
```
