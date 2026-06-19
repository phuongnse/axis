---
name: axis-api-contract
description: Change Axis REST/OpenAPI contracts safely. Use when adding or modifying Minimal API endpoints, request or response DTOs, required fields, status codes, generated OpenAPI, frontend API types, or SPA callers that depend on API response casing.
---

# Axis API Contract

## Goal

Change an Axis API surface without drifting from module boundaries, auth defaults, generated contracts, or frontend type parity.

## Workflow

1. Run `$axis-design-gate`.
   - API endpoint, required-field, response-shape, status-code, or public contract changes are high-risk.
   - Stop for user sign-off before code when the dossier marks the change high-risk.

2. Read the owning rules.
   - `AGENTS.md`
   - `docs/playbooks/design-gate.md`
   - `docs/playbooks/agent-checklist.md`
   - `docs/playbooks/api-patterns.md`
   - `docs/playbooks/repo-layout-discovery.md`
   - `docs/playbooks/frontend.md` when the SPA consumes the contract
   - The owning use-case file when behavior or status changes

3. Trace the contract.
   - Search the route, endpoint group, DTO, handler, field name, generated API type, and frontend callers with `rg`.
   - Identify whether the API shape is produced by Application DTOs, endpoint response mappings, OpenAPI generation, or frontend generated types.

4. Implement the API shape narrowly.
   - Keep Minimal API endpoints thin: bind, `mediator.Send`, map `Result` to response or `ToProblemDetails()`.
   - Require `.RequireAuthorization()` unless the route is explicitly public.
   - Put stable request/response DTOs in the owning Application/API contract pattern already used by the module.
   - Preserve camelCase JSON and generated frontend type parity.
   - Update API tests when route shape, status code, auth, validation, or response body changes.

5. Regenerate contracts when API shape changes.
   - Run `python scripts/axis.py generate api-contracts`.
   - Commit the generated OpenAPI and `frontend/src/lib/api-types.ts` changes when they are touched.
   - If only a frontend caller changes and the server contract does not, state that generation was not triggered.

6. Verify.
   - API contract: affected `tests/Api/Axis.Api.Tests/` tests.
   - Generated parity: `python scripts/axis.py check frontend-api-contracts` when available in the local command set.
   - Frontend consumers: `npm run ci` and `npm run test` from `frontend/`.
   - Ready review: `$axis-ready-review`.

## Output

Report the route or contract changed, generated files updated or not triggered, tests run, and any explicit deferrals.
