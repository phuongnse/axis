# Design Gate: Executable Business Object Workflow

> **Navigation**: [docs/playbooks/design-gate.md](./design-gate.md) · [submit-business-object-record.md](../use-cases/business-objects/submit-business-object-record.md) · [docs/README.md](../README.md)

## Risk and scope

This is a full Design Gate for the first executable Draft → Submitted Business Object workflow. The slice is high-risk because it spans new REST/OpenAPI operations, migration-backed persistence, optimistic concurrency, authentication/workspace isolation, MCP mutation tools, and generated client contracts.

The correction checkpoint covers the existing implementation and independent review findings. It does not expand the product into generic workflow authoring, assignments, approvals, notifications, or additional record mutations.

## Governing rules

- Product behavior follows [submit-business-object-record.md](../use-cases/business-objects/submit-business-object-record.md), especially AC-001 through AC-018 and AT-001 through AT-010.
- Repository lifecycle gates follow [AGENTS.md](../../AGENTS.md#critical-rules), [reference.md](../../.agents/skills/reference.md#universal-gates), and [agent-checklist.md](./agent-checklist.md#review-verification).
- Module ownership and persistence patterns follow [axis-module-patterns](../../.agents/skills/axis-module-patterns/SKILL.md#workflow), with Business Objects owning record lifecycle/evidence and Rules owning binding revisions/evaluation.
- REST/OpenAPI parity follows [axis-api-contract](../../.agents/skills/axis-api-contract/SKILL.md#workflow) and [api-patterns.md](./api-patterns.md).
- MCP parity and runtime boundaries follow [axis-mcp-integration](../../.agents/skills/axis-mcp-integration/SKILL.md#workflow) and [mcp.md](./mcp.md#runtime-lifecycle-and-blocker-protocol).
- Client interaction and primitive ownership follow [axis-frontend-feature](../../.agents/skills/axis-frontend-feature/SKILL.md#workflow), [axis-ui-system](../../.agents/skills/axis-ui-system/SKILL.md#workflow), and [frontend.md](./frontend.md#component-design).

## Blast radius

The implementation and review sweep covers:

```text
src/Modules/BusinessObjects/
src/Modules/Rules/
src/Axis.Api/
src/Axis.Mcp/
tests/Modules/BusinessObjects/
tests/Modules/Rules/
tests/Api/
tests/Tools/
openapi.json
docs/use-cases/business-objects/submit-business-object-record.md
docs/use-cases/business-objects/submit-business-object-record.evidence.md
docs/playbooks/design-gate-reference-solution-consumer-boundary.md
```

## Contract and invariant decisions

- Submission checks the expected record revision before rule evaluation or any non-match response; stale requests always return conflict and do not reveal a newer decision.
- Create idempotency compares against an immutable create-request fingerprint; draft edits update record values without changing the fingerprint used to classify retries.
- Exact binding revision resolution is authoritative. Later current-binding disablement or edits cannot rewrite an already published field-rule snapshot; the resolved historical revision's own availability governs execution.
- Migration backfills preserve resolvable binding revisions. Databases upgraded from the pre-history schema cannot recover snapshots that were never stored, so a revision-1 compatibility alias is explicitly created from the legacy current row while the legacy current revision is retained as its own snapshot; this is a documented migration boundary, not reconstructed historical evidence.
- Record validation rejects null collections/elements, unknown/duplicate values, invalid cardinality, and malformed typed values with stable field errors. Canonical typed strings are persisted consistently, including explicit DateTime offset semantics.
- Rule mismatch evidence remains available in the response/UI cache and submitted detail renders every exact evaluation, including binding ID/revision, rule key/version, Boolean result, diagnostics, actor, and timestamp where the contract exposes them.
- REST error responses and required request/response fields match runtime status/body behavior; OpenAPI and generated frontend types are regenerated through the owning wrapper. MCP preserves problem codes and field errors while forwarding the API's revision/idempotency contract.
- Axis owns the generic record lifecycle and its REST/OpenAPI/MCP contract. Consumer-owned products own their collection and record interactions, including browser acceptance evidence.

## Retirement and compatibility

The current consumer-product clean cutover, including retired UI and browser surfaces, is owned by the [reference-solution consumer-boundary dossier](./design-gate-reference-solution-consumer-boundary.md#retirement-and-compatibility). This dossier retains only the product-neutral record lifecycle contract; it does not define a consumer UI or compatibility path.

## Verification plan

- Focused domain/application tests for stale submission, idempotency, exact binding revisions, null/malformed values, canonicalization, and every required handler.
- Infrastructure migration/repository tests for historical binding backfill and concurrency/index behavior.
- API tests plus `python scripts/axis.py generate api-contracts`, `check frontend-api-contracts`, and generated-client callers for status/body/required-field parity.
- Consumer-owned component and browser journeys are verified in independently versioned product source under the reference-solution consumer-boundary dossier; Axis verifies its generic contract and focused module behavior here.
- MCP contract, API coverage, safety, and authenticated supported-client lifecycle evidence; protocol-only evidence remains explicitly separate from live-agent evidence.
- Before publication: clean committed checkpoint, `$axis-review-readiness`, configured independent review completed (not merely running or timed out), and exact PR metadata validation.

## Acceptance and status boundary

The use-case remains incomplete until every in-scope required AT has current evidence. Any unsupported client lifecycle, missing high-risk runtime state, or incomplete acceptance row remains `blocked`/`not run`; it must not be represented as `Done`.
