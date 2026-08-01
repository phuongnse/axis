# Manage Rule Bindings

> **Navigation**: [docs/use-cases/rules/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Let a workspace manage reusable connections between one exact published Rule version and any consumer target without making the Rule definition depend on that consumer.

## Primary actor

- Signed-in workspace user for binding lifecycle and usage discovery
- A consumer module for typed context construction and evaluation

## Trigger

- A workspace needs to connect one exact published Rule version to a reusable consumer target.
- A user needs to inspect, change, disable, or remove one application of a Rule without changing the Rule definition.

## Main flow

1. User opens the Rules collection and opens binding configuration or usage in a managed dialog.
2. Rules accepts the exact published definition key and version, generic target type/id, use case or trigger, typed input mappings, priority, enabled state, and failure behavior.
3. Rules validates workspace ownership, published-version eligibility, mapping shape and literals, target identity, and the caller's revision.
4. Rules persists the binding independently and returns its stable ID and revision.
5. A consumer stores only that binding ID in its own configuration and implements a typed `IRuleContextAdapter<TConsumerContext>`.
6. At runtime the consumer supplies typed context to Rules; the binding resolves explicit sources into declared inputs, Rules returns deterministic explainable outputs, and the consumer owns the business response.
7. User can inspect all bindings for an exact version, update or disable one binding, or delete a binding without deleting or changing the Rule definition.

## Alternate / error flows

- Unknown, draft, archived, or stale exact version: reject creation without substituting another version.
- Missing required mapping, unknown mapping key, invalid literal, incompatible type, or unsupported mapping kind: reject without persistence.
- Stale binding revision or another workspace's binding: reject without overwrite or resource disclosure.
- Disabled binding: fail with a stable disabled-binding result; it is not silently evaluated.
- Binding deletion removes only the connection; the definition and other bindings remain discoverable.

## Acceptance Criteria

- **AC-001** A binding references exactly one published Rule version and cannot be created from a draft or silently resolve a newer version.
- **AC-002** A binding owns only generic target type/id, use case or trigger, typed context/literal input mappings, priority, enabled state, failure behavior, revision, and audit fields.
- **AC-003** Rules validates required mappings, optional omissions, mapping keys, literal types, cardinality, and allowed values against the referenced version.
- **AC-004** Multiple independent bindings may reference one Rule version; changing or deleting one binding does not mutate or delete the definition or other bindings.
- **AC-005** Updating a binding uses optimistic concurrency and changes only binding-owned fields; changing the binding never rewrites the immutable Rule version.
- **AC-006** Consumers persist binding IDs and use the public typed context adapter; Rules has no dependency on Object, Workflow, or another consumer domain type.
- **AC-007** Binding evaluation maps explicit consumer sources into rule inputs and returns deterministic, typed, explainable outputs; consumers own authorization, transactions, and result handling.
- **AC-008** Users can discover usage for an exact version from the Rules collection dialog, including target, trigger, priority, enabled state, and binding ID.
- **AC-009** Binding lifecycle and usage are workspace-isolated and generated REST/OpenAPI contracts remain in parity with the SPA client.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Domain boundary | Binding creation, exact-version identity, target neutrality, mapping invariants, and stale revision rejection | AC-001, AC-002, AC-003, AC-005 | Domain test | Yes |
| AT-002 | Application boundary | Binding create/update/delete and exact-version usage discovery are workspace-isolated and independent of definition lifecycle | AC-001, AC-004, AC-005, AC-009 | Application test | Yes |
| AT-003 | Application boundary | Neutral consumer typed context maps through a binding into the pure evaluator with deterministic result forwarding | AC-006, AC-007 | Application test | Yes |
| AT-004 | Infrastructure boundary | Bindings persist with JSON mappings, concurrency, and target/version indexes through the Rules migration | AC-001, AC-002, AC-009 | Infrastructure integration test | Yes |
| AT-005 | API boundary | CRUD and exact-version usage endpoints expose generated DTO parity and retain the definition after binding deletion | AC-001, AC-004, AC-008, AC-009 | API integration test | Yes |
| AT-006 | Application/Infrastructure boundaries | Business Objects persists only binding IDs and validates references through Rules Contracts | AC-004, AC-006 | Application test + Architecture test | Yes |
| AT-007 | UI component | Rules and Object collection dialogs create/configure, inspect, and remove binding usage without leaving grid state | AC-008, AC-009 | UI component test | Yes |

## Out Of Scope

- Consumer target schema, consumer authorization policy, record validation transactions, side effects, workflow execution, and event orchestration.
- Editing an immutable Rule version through a binding or creating compatibility aliases for retired consumer-specific fields.

## Screen flow

| Surface | Required contract |
|---|---|
| Rules collection | Keep one primary `DataTable`; open binding usage and lifecycle actions in managed dialogs. |
| Binding configuration | Use `Dialog` and `Form` controls for exact version, generic target, input-source mapping, priority, enabled state, and failure behavior. Context, target data, and fixed values are sources for declared inputs rather than ambient Rule behavior. |
| Usage | Show a read-only list for the exact version with loading, empty, error, enabled, and disabled states. |
| Destructive action | Use `AlertDialog` for binding deletion and refresh only affected definition usage and consumer data. |

Required UI quality: use existing shadcn/Tailwind primitives, preserve filters, sorting, pagination, and selected row state, return focus to the invoking grid control, and do not add CRUD pages, drawers, or side panels.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Domain | Done |
> | Application | Done |
> | Infrastructure | Done |
> | API | Done |
> | Frontend | Partial |
>
> **Gaps vs spec:** Rules usage discovery and Object-side binding creation are implemented; a dedicated full binding edit/delete dialog and focused frontend binding component test remain partial.
>
> **Deferred follow-ups:** Add a dedicated binding edit form in the Rules collection after the current usage dialog slice; retain the existing Object-side creation path and backend CRUD contracts.
>
> **Verification:** Rules domain, application, infrastructure, API, architecture, and focused frontend checks are recorded in the sibling evidence sidecar and must pass before review.
>
> **Decisions:** Bindings are first-class Rules data. They reference exact published versions, keep consumer targets opaque, allow omitted optional inputs, and do not create foreign keys into consumer modules. The pre-production product phase uses clean cutover with no compatibility paths.
