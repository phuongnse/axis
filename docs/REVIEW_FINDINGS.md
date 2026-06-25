# Review Findings

> **Navigation**: [docs](./README.md) · [AGENTS.md](../AGENTS.md)

This file records recurring finding classes and their enforcement status. It should stay small; delete rows when the rule no longer applies to the current repository.

## Enforcement Taxonomy

| Status | Meaning |
|---|---|
| Enforced | CI/build/tooling can fail the PR for this class. |
| Partial | Tooling catches a deterministic subset and the known gap is named. |
| Review-only | Human judgment is required. |
| Guidance | Useful convention, not a defect class. |
| Not a rule | Deliberately not enforced or reviewed. |

## Ledger

| Finding class | Rule owner | Trigger / scope | Mechanism | Proof / gap | Status |
|---|---|---|---|---|---|
| REST wire-shape drift between API and SPA | [api-patterns.md](./playbooks/api-patterns.md) | REST endpoint, DTO, `openapi.json`, or generated frontend API types change | OpenAPI snapshot tests and `python scripts/axis.py frontend gen-api-types` | Generated `frontend/src/lib/api-types.ts` must match `openapi.json`; contract changes need focused API tests | Enforced |
| Frontend route/component contract drift | [design-system.md](./playbooks/design-system.md) | Route-bound frontend surfaces change | `python scripts/axis.py check frontend-component-composition` | Negative policy tests prove missing contracts and metadata fail | Enforced |
| Frontend raw style bypasses design tokens | [design-system.md](./playbooks/design-system.md) | Frontend component styling changes | `python scripts/axis.py check frontend-style` | Negative policy tests prove token bypasses fail outside approved primitive sources | Enforced |
| New skipped tests | [AGENTS.md](../AGENTS.md) | Added or changed tests | Added-line policy check rejects `Skip =` under tests | Existing policy tests cover the ratchet | Enforced |
| `EnsureCreated` reintroduced | [AGENTS.md](../AGENTS.md) | Source or test database setup changes | Added-line policy check rejects `EnsureCreated` / `EnsureCreatedAsync` | Existing policy tests cover the ratchet | Enforced |
| Domain external dependency | [AGENTS.md](../AGENTS.md) | Domain project changes | Architecture tests | Reflection catches common infrastructure dependencies in Domain assemblies | Enforced |
| Application handler without matching behavior test | [agent-checklist.md](./playbooks/agent-checklist.md) | Changed command/query handler | Diff-based policy check | Known gap: only changed handlers are ratcheted; untouched legacy code is not swept | Partial |
| Endpoint does too much orchestration | [api-patterns.md](./playbooks/api-patterns.md) | Minimal API endpoint changes | Endpoint policy check and review | Known gap: named handlers can be counted mechanically; inline or semantic orchestration still needs review | Partial |
| Spec rewritten to match an implementation shortcut | [AGENTS.md](../AGENTS.md) | Behavior/spec/status changes | Review | Requires intent judgment; reviewers compare acceptance criteria, code, and tests | Review-only |
| One public C# type per file | None | C# source changes | None | Intentional grouping is allowed when it improves clarity | Not a rule |
