---
name: axis-api-contract
description: Change Axis REST/OpenAPI wire contracts. Use for routes, request/response DTOs, required fields, status codes, auth exposure, generated OpenAPI/frontend types, and SPA callers affected by wire shape.
---

# Axis API Contract

## Goal

Change one wire surface without drifting from module ownership, auth defaults, generation, or frontend parity.

## Hard gates

Follow [reference.md](../reference.md).
- Contract entry work **Requires** `$axis-design-gate`; public contract changes stop for its high-risk sign-off.
- Regenerate every affected contract artifact; do not hand-maintain generated parity.
- Request fields represent user-authored decisions or required protocol tokens; server-owned derived values stay out of the request.

## Inputs

- Route/DTO/status/auth change and owning product contract.
- Current Design Gate evidence.
- Endpoint, handler, DTO, OpenAPI, generated type, and caller references.

## Workflow

1. Carry current Design Gate evidence; do not rerun it when delegated by a use-case orchestrator.
2. Read [docs/playbooks/api-patterns.md](../../../docs/playbooks/api-patterns.md), the owning use case, and frontend contract rules when the SPA consumes the surface.
3. Trace route, DTO, handler, field names, tests, OpenAPI, generated frontend types, and callers with `rg`.
4. Implement narrowly: thin endpoint, explicit authorization/public decision, stable DTOs, generated casing parity, and business-safe result mapping.
5. Run `python scripts/axis.py generate api-contracts` when wire shape changes; otherwise record generation as not triggered.
6. Test route shape, auth, validation, status/body, generation parity, and affected callers at the lowest reliable boundary.
7. Return the changed contract, generated artifacts, tests, and unresolved decisions to the caller.

## Output

Report route/shape, auth decision, generation result, tests, and exact deferrals.
