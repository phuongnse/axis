# Evaluate Published Rules

> **Navigation**: [docs/use-cases/rules/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Evaluate exact published rule versions through one deterministic, pure engine. Every version follows `Inputs -> Logic -> Outputs`; the current language capability returns one scalar Boolean output through `isMatch`. A match means the rule's stated assertion is satisfied; the consumer decides how that result affects its own business transaction.

## Primary actor

- Authorized Rules consumer requesting deterministic evaluation

## Supporting actors

- A workspace user may supply typed samples while authoring; a consumer module supplies typed context for an applied binding.

## Preconditions

- The caller supplies an exact eligible Rule version or binding and typed input context within the allowed evaluation limits.

## Trigger

- A consumer needs to evaluate an exact published rule version before committing its own transaction.
- A user simulates a draft or published rule during authoring.

## Success guarantee

- The caller receives deterministic typed outputs and safe ordered diagnostics for the exact requested version without consumer-state mutation.

## Minimal guarantee

- Invalid input, unsupported language, unresolved version, complexity overflow, or evaluator failure returns a stable failure and never becomes a successful match or business decision.

## Main flow

1. Consumer supplies an exact binding ID and a typed public `RuleContext` created by its own `IRuleContextAdapter<TConsumerContext>`.
2. Rules resolves the binding to one exact definition key/version, enforcing workspace isolation and enabled-state/failure behavior.
3. Rules validates mapped input keys, types, cardinality, requiredness, allowed values, language version, and complexity limits.
4. Rules evaluates canonical logic through one bounded pure evaluator.
5. Rules returns exact definition/version, the declared outputs, and safe per-node diagnostics in deterministic order; `isMatch` is `true` exactly when the canonical positive assertion evaluates to true.
6. Consumer decides whether a satisfied or unsatisfied assertion means reject, warn, allow, continue, or another consumer-owned result; Rules never mutates the consumer transaction.

## Alternate / error flows

- Unknown or unresolved version: fail rather than substituting another version.
- Missing required, malformed, oversized, or type-incompatible inputs: fail before condition evaluation; an omitted optional input remains absent for canonical logic to evaluate.
- Unsupported language/operator/function or complexity limit: fail safely.
- Archived version referenced by an existing immutable snapshot: resolve the exact version; archived versions cannot be newly bound.
- Unexpected evaluator fault: return an evaluation failure, never a successful match or implicit business decision.
- Cross-workspace workspace-rule reference: return not found without disclosing definition existence.

## Acceptance Criteria

- **AC-001** Evaluator accepts exact rule key/version references and typed input values; it does not accept consumer scope, context schema, purpose, or enforcement effect as rule semantics.
- **AC-002** Built-in and workspace versions use the same deterministic evaluator and `Inputs -> Logic -> Outputs` contract.
- **AC-003** The registered language supports only declared typed operators, pure functions, and all/any/not groups.
- **AC-004** Evaluation resolves a version with a scalar Boolean output contract and returns exact version, boolean match state through `isMatch`, and safe per-node diagnostics in deterministic order; `true` means the rule's stated assertion is satisfied and `false` means it is not satisfied.
- **AC-005** Simulation uses the same evaluator and canonical condition as runtime evaluation without mutating rule or consumer state.
- **AC-006** Archived exact versions remain resolvable for existing consumer snapshots but cannot be newly bound.
- **AC-007** Evaluation enforces input, expression, collection, precision, and execution limits.
- **AC-008** Evaluation errors never become a successful match or consumer decision.
- **AC-009** Rules owns resolution and pure evaluation; consumers own binding, context construction, authorization, persistence, and result handling.
- **AC-010** A binding evaluates through a public consumer-neutral typed context contract, and a neutral consumer can use the same evaluator without importing Object or another consumer module.
- **AC-011** A missing mapped context value is omitted from evaluation input; optional inputs reach canonical logic as absent values, while required inputs fail validation without changing match polarity.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Domain boundary | Shared evaluator returns deterministic positive-assertion match/non-match for typed inputs, functions, operators, groups, and temporal values | AC-002, AC-003, AC-004, AC-007 | Domain test | Yes |
| AT-002 | Domain boundary | Invalid inputs, unsupported capabilities, complexity overflow, and evaluator faults fail closed | AC-001, AC-007, AC-008 | Domain test | Yes |
| AT-003 | Application boundary | Exact built-in/workspace versions resolve through one evaluator with workspace isolation and archived-version behavior | AC-002, AC-006, AC-009 | Application test | Yes |
| AT-004 | API boundary | Evaluation and simulation contracts expose only typed inputs, exact versions, match state, diagnostics, stable errors, and generated parity | AC-001, AC-004, AC-005, AC-008, AC-009 | API integration test | Yes |
| AT-005 | UI component | Simulation distinguishes valid match, valid non-match, invalid input, and evaluation failure | AC-004, AC-005, AC-008 | UI component test | Yes |
| AT-006 | Browser journey | User simulates a rule with typed inputs and sees why it matched or did not match without mutation or console errors | AC-004, AC-005, AC-009 | Browser automation | Yes |
| AT-007 | Application boundary | A neutral consumer adapter maps present or absent typed context through a binding; optional absence evaluates normally, required absence fails, and Rules returns the exact version with no consumer side effect | AC-009, AC-010, AC-011 | Application test | Yes |

## Out Of Scope

- Consumer context construction, input binding, target selection, enforcement messages, transaction mutation, and execution history.
- Side-effect actions, remote data access, scripts, plugins, webhooks, and automation orchestration.

## Screen flow

| Screen | Required contract |
|---|---|
| Rule simulation | Accept bounded sample inputs from the rule's input contract. |
| Simulation result | Show exact version, match/non-match state, and a safe “why matched”/“why not matched” explanation from node diagnostics. |
| Simulation error | Distinguish invalid input and evaluator failure without exposing secrets or stack traces. |

Required UI quality: input controls are labelled and keyboard reachable, values are validated before submission, result states are distinct, and the surface remains responsive without document overflow.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Domain | Done |
> | Application | Done |
> | Infrastructure | N/A |
> | API | Done |
> | Frontend | Done |
>
> **Gaps vs spec:** N/A.
>
> **Deferred follow-ups:** N/A. Consumer result handling remains outside Rules and is owned by each consumer use case.
>
> **Verification:** AT-001 through AT-007 are mapped to current source and passing domain, application, API, contract, focused frontend, and browser evidence in the sibling sidecar.
>
> **Decisions:** Evaluation returns a positive-assertion Boolean match rather than a consumer business decision. Draft and exact-version simulation distinguish non-match, invalid sample, and evaluator failure. [Rules architecture](../../ARCHITECTURE.md#rules-boundary) owns the pure evaluator, typed boundary, exact-version resolution, and consumer separation.
