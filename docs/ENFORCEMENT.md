# Enforcement

> **Navigation**: [docs/README.md](./README.md) · [AGENTS.md](../AGENTS.md)

Recurring rule classes and their enforcement status.

## Status

| Status | Meaning |
|---|---|
| Enforced | Tooling can fail the PR; proof names the check or test. |
| Partial | Tooling catches a subset; proof names the known gap. |
| Review-only | Human judgment is required. |

## Ledger

| Finding class | Rule owner | Trigger / scope | Mechanism | Proof / gap | Status |
|---|---|---|---|---|---|
| REST API/SPA wire drift | [docs/playbooks/api-patterns.md](./playbooks/api-patterns.md) | REST DTO, endpoint, `openapi.json`, or generated SPA types | OpenAPI snapshot tests; `python scripts/axis.py frontend gen-api-types` | Generated SPA types must match `openapi.json`; contract changes need focused API tests | Enforced |
| Skipped tests added | [AGENTS.md](../AGENTS.md) | Added or changed tests | Added-line policy check rejects `Skip =` under tests | Existing policy tests cover the check | Enforced |
| Database setup bypasses migrations | [AGENTS.md](../AGENTS.md) | Source or test database setup changes | Added-line policy check rejects `EnsureCreated` / `EnsureCreatedAsync` | Existing policy tests cover the check | Enforced |
| Domain external dependency | [AGENTS.md](../AGENTS.md) | Domain project changes | Architecture tests | Reflection catches common infrastructure dependencies in Domain assemblies | Enforced |
| Changed handler without behavior test | [docs/playbooks/agent-checklist.md](./playbooks/agent-checklist.md) | Changed command/query handler | Diff-based policy check | Known gap: only changed handlers are checked; unchanged files are not swept | Partial |
| Endpoint over-orchestration | [docs/playbooks/api-patterns.md](./playbooks/api-patterns.md) | Minimal API endpoint changes | Endpoint policy check and review | Known gap: named handlers can be counted mechanically; inline or semantic orchestration still needs review | Partial |
| Route access grouping | [docs/playbooks/frontend.md](./playbooks/frontend.md) | SPA route files under `frontend/src/routes` | `python scripts/axis.py check frontend-quality` requires guest-only auth/register routes to live under the `_guest` access group and inherit its guard | Known gap: tooling enforces current route groups; new access groups still require owner review | Partial |
| Dead-end route states | [docs/playbooks/frontend.md](./playbooks/frontend.md) | Public/auth route screen states and authenticated route shells | `python scripts/axis.py check frontend-quality` requires public route escape metadata for routes that render screen states; focused UI tests cover current auth routes | Known gap: route metadata proves declared escape targets, while semantic visibility still needs review/tests | Partial |
| Transient handoff screens | [docs/playbooks/frontend.md](./playbooks/frontend.md) | SPA callback, bootstrap, and redirect handoffs | `python scripts/axis.py check frontend-quality` rejects callback success handoff render logic and stale callback pending copy; focused browser journeys assert current sign-in handoffs do not visit/render callback screens | Known gap: broader visual flash detection across future handoff routes still needs review plus journey-specific tests | Partial |
| Dependency automation config drift | [docs/TECH_STACK.md](./TECH_STACK.md) | `.github/renovate.json5` changes | Renovate config validator | CI runs `python scripts/axis.py check renovate-config` | Enforced |
| Spec rewritten around incomplete work | [AGENTS.md](../AGENTS.md) | Behavior/spec/status changes | Review | Requires intent judgment; reviewers compare acceptance criteria, code, and tests | Review-only |
