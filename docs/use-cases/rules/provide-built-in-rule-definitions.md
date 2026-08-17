# Provide Built-in Rule Definitions

> **Navigation**: [docs/use-cases/rules/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Provide a code-owned catalog of reusable built-in rules using the same semantic definition model as workspace rules. Built-in origin and immutability are catalog capabilities, not a second Rule type.

## Primary actor

- Authorized consumer discovering a built-in Rule definition

## Supporting actors

- The Rules composition root validates and loads the code-owned catalog.

## Preconditions

- The deployed built-in catalog and its versioned language contract are available during application composition.

## Trigger

- The application starts and needs the built-in rule catalog.
- A consumer discovers a built-in rule version to bind to its own inputs.

## Success guarantee

- The consumer resolves the exact immutable built-in definition and evaluates it through the same public semantic model as Workspace Rules.

## Minimal guarantee

- Invalid catalog content fails construction, and unknown versions or mutation attempts never substitute, shadow, or change a built-in definition.

## Main flow

1. Rules loads the code-owned built-in catalog at composition-root startup.
2. Each catalog entry exposes the same key, description, inputs, canonical positive assertion, outputs, language version, and current Boolean-match capability used by workspace definitions.
3. The catalog marks the source as built-in and the available actions as read-only.
4. Consumers discover a built-in definition, bind its inputs to their own values, and store their own exact version/reference snapshot.
5. Rules resolves and evaluates the exact built-in version through the shared evaluator.

## Alternate / error flows

- Invalid built-in definition or documentation: fail catalog construction before runtime use.
- Unknown built-in key or version: return not found without substituting another version.
- Consumer cannot bind required inputs: reject the binding in the consumer module without mutating the rule.
- Attempt to edit, archive, or shadow a built-in key: reject without mutation.

## Acceptance Criteria

- **AC-001** Every built-in definition has a stable key, immutable positive version, description, typed inputs, canonical logic, typed outputs, and supported language version; the current catalog uses one scalar Boolean output.
- **AC-002** Built-in definitions use the same `RuleDefinition` semantic model, evaluator, API detail shape, and read-only renderer as workspace definitions.
- **AC-003** Built-in definitions declare no consumer context key, target scope, applicability profile, validation/decision outcome, or side effect.
- **AC-004** Built-in inputs, operators, functions, conditions, and documentation are normalized and validated at startup.
- **AC-005** Unknown or invalid built-in definitions fail startup/catalog construction rather than reaching runtime evaluation.
- **AC-006** Built-in definitions are immutable and cannot be edited, archived, or shadowed by a workspace key.
- **AC-007** Consumers bind inputs and own applied snapshots; Rules only exposes the public definition and evaluator contracts.
- **AC-008** Every built-in uses positive assertion polarity: Required matches a present non-blank value; range, precision, length, pattern, format, and selection-count rules match values that satisfy their declared constraints.
- **AC-009** Required accepts an absent value as evaluable input and returns non-match; absence is not an evaluator failure. Other required runtime inputs remain validation failures when absent.
- **AC-010** A code-owned built-in version exposes `publishedBySubject: null`; Axis does not fabricate a Human or Service subject for catalog provenance, while Workspace-authored versions require their real publishing subject.
- **AC-011** A code-owned built-in row uses explicit `System` current-state actor presentation without fabricating a Human or Service subject; unavailable code-package timestamps and concurrency revision remain explicit `N/A` in the same trailing metadata columns used by Workspace Rules.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Domain boundary | Catalog returns one normalized built-in definition model and a valid/invalid behavior matrix proves positive assertion polarity for every built-in | AC-001, AC-004, AC-005, AC-008 | Domain test | Yes |
| AT-002 | Application boundary | Exact built-in versions resolve through the shared evaluator; Required returns match for present non-blank, non-match for blank or absent, while malformed input remains an error | AC-002, AC-007, AC-009 | Application test | Yes |
| AT-003 | API boundary | Catalog/detail responses expose built-in metadata plus the same semantic fields as workspace definitions, including nullable built-in publisher provenance, with generated parity | AC-001, AC-002, AC-006, AC-010 | API integration test | Yes |
| AT-004 | Application boundary | No consumer module depends on a built-in-specific Rules type or internal implementation | AC-002, AC-007 | Architecture test | Yes |
| AT-005 | UI component | Catalog and detail use the same renderer; built-in origin only removes mutation actions, and the shared metadata columns distinguish explicit System provenance from unavailable values | AC-002, AC-003, AC-006, AC-011 | UI component test | Yes |

## Out Of Scope

- Workspace-authored lifecycle, owned by [manage-workspace-rule-definitions.md](./manage-workspace-rule-definitions.md).
- Consumer input binding and field applicability, owned by the consumer use case.
- Runtime transaction enforcement and execution history.

## Screen flow

| Screen | Required contract |
|---|---|
| Rules catalog | Show built-in and workspace definitions in one collection and one semantic row shape. |
| Rule detail | Use [Detail Sections](../../foundations/data-display/detail-sections.md) and one compact semantic `Inputs -> Logic -> Outputs` sequence for both sources. Sequence markers, connectors, labels, and alignment use one visual grammar. Read-only expressions are static semantic content; explicit authoring help opens the canonical expression guide, and operators expose the same reference behavior there. |
| Consumer binding | Consumer selects a built-in version and maps its inputs to consumer-owned values. |

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
> **Deferred follow-ups:** N/A. Consumer input binding remains owned by each consumer use case.
>
> **Verification:** AT-001 through AT-005 are mapped to current domain, application, API, architecture, and focused frontend evidence in the sibling sidecar; the recorded suites pass at the current checkpoint.
>
> **Decisions:** Built-in and Workspace definitions expose one positive-assertion semantic contract; built-in origin removes mutation actions without creating a second Rule type. Built-in publication is code-owned provenance and therefore has no fabricated Human or Service publishing subject. [Rules architecture](../../ARCHITECTURE.md#rules-boundary) owns catalog validation, shared evaluation, and the clean replacement of retired polarity surfaces.
