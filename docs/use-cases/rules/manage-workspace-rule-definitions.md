# Manage Rule Definitions

> **Navigation**: [docs/use-cases/rules/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Let a signed-in workspace user create, validate, test, version, activate, deactivate, and inspect reusable rules without first creating a consumer target.

## Primary actor

- Signed-in workspace user

## Trigger

- User needs reusable deterministic logic that is not supplied by the built-in catalog.
- User needs to revise, test, version, activate, deactivate, or inspect an existing definition.

## Main flow

1. User opens the Rules module's single collection route and keeps the current grid filters, sorting, page, and selection.
2. User opens a create dialog and enters definition metadata; Rules creates an inactive workspace draft independent of any target or binding.
3. User declares inputs with stable identities, labels, accepted value types, cardinality, requiredness, and optional allowed values, plus the typed outputs returned to consumers.
4. User authors logic through a safe textual DSL, visual composer, or another supported projection. Every authoring mode uses one versioned language contract and produces the same canonical logic that transforms declared inputs into declared outputs.
5. Rules parses and validates the projection, persists only the canonical AST and its language version, and regenerates syntax and natural-language explanations from that AST.
6. User opens a test dialog, supplies typed samples, and receives deterministic typed outputs plus a safe explanation without mutating rule or consumer state.
7. User versions a valid draft; Rules creates an immutable, server-numbered snapshot of metadata, input definitions, canonical logic, output definitions, and language version.
8. User activates an exact immutable version for discovery and new bindings, or deactivates the definition. Neither action rewrites a version or retargets an existing binding.
9. User views definition details, version history, activation state, and current binding usage in dialogs from the same grid.
10. A successful mutation refreshes only the affected row or dialog data; closing returns focus and the unchanged grid state.

## Alternate / error flows

- Duplicate definition identity, duplicate input identity, malformed metadata, or unsupported input contract: reject without persistence.
- Invalid DSL or visual projection: show source-specific diagnostics and keep the last valid canonical AST unchanged.
- Unknown input, incompatible operation, malformed literal, unsupported language version, or complexity overflow: block testing, version creation, and activation.
- Stale draft revision or concurrent lifecycle change: reject without overwriting the current draft, immutable versions, activation, or bindings.
- Deactivation: prevent discovery for new bindings while preserving definition history and existing exact-version bindings for explicit binding lifecycle management.
- Workspace isolation or authorization failure: return a not-found or permission-denied result without disclosing another workspace's definition.
- Destructive dismissal or a supported destructive lifecycle action: require explicit confirmation and preserve recoverable input on cancel or failure.

## Acceptance Criteria

*Happy path*

- **AC-001** A user can create and test an inactive workspace definition without an Object, another consumer target, or a binding.
- **AC-002** A definition owns metadata plus stable typed input and output contracts; input requiredness and optionality never change the meaning of the Inputs section.
- **AC-003** Canonical logic and its language version are the only persisted rule behavior and source of truth; authoring projections do not create alternate semantics.
- **AC-004** Safe textual DSL, visual composition, formatting, syntax diagnostics, and autocomplete are authoring projections generated from the same server-owned language and typed-input contract.
- **AC-005** Testing accepts typed sample inputs and returns deterministic match state plus a safe structured explanation without persistence or side effects.
- **AC-006** Version creation snapshots metadata, input definitions, canonical logic, output definitions, and language version into an immutable server-numbered version.
- **AC-007** Creating or revising a version does not activate it, and activating or deactivating does not mutate any version or exact-version binding.
- **AC-008** Activation makes an exact immutable version discoverable for new bindings; deactivation prevents new bindings while existing bindings remain unchanged and separately enabled or disabled.
- **AC-009** Localized natural-language explanations are generated from the canonical AST or evaluation trace for the requested locale and are never stored as rule logic or localized copies.
- **AC-010** Built-in and workspace definitions use one semantic model and one read-only detail projection; origin and capabilities only change available actions.
- **AC-011** The Rules route remains grid-first, and create, view, edit, author, test, version, activate, deactivate, and usage operations complete in dialogs without CRUD page navigation or drawers.

*Validation & errors*

- **AC-012** Metadata, stable input identities, types, cardinality, allowed values, AST nodes, operations, functions, literals, output contract, and language version are validated before testing, versioning, or activation.
- **AC-013** DSL parsing cannot execute the authored text; parse failure never replaces the last valid canonical AST or becomes runtime behavior.
- **AC-014** Depth, node, input, collection, literal-size, precision, function, and evaluation-work limits are enforced at the earliest reliable boundary.
- **AC-015** Draft and lifecycle mutations require the caller's last-seen revision and reject stale writes without overwrite.
- **AC-016** Immutable versions cannot be edited in place, and lifecycle changes never rewrite historical content.
- **AC-017** Rules cannot execute arbitrary code or JavaScript, access files, network services, secrets, arbitrary databases, nondeterministic time, or randomness, or produce side effects.
- **AC-018** Workspace definitions, drafts, samples, versions, activation, and usage are isolated and authorized without resource disclosure.

*Edge cases*

- **AC-019** Rules owns definitions, versions, activation, authoring language, validation, testing, persistence, and evaluation without importing Object, Workflow, or another consumer's domain types.
- **AC-020** Rules exposes binding usage without copying target-specific fields into a definition; binding lifecycle is owned by [docs/use-cases/rules/manage-rule-bindings.md](./manage-rule-bindings.md).
- **AC-021** Definition, version, and activation mutations are atomic and record workspace, revision, actor, and audit timestamps without introducing evaluator nondeterminism.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Domain boundary | Stable typed inputs, canonical AST, immutable versions, separate activation, limits, and deterministic Boolean semantics preserve all definition invariants | AC-002, AC-003, AC-006, AC-007, AC-008, AC-012, AC-014, AC-016, AC-017, AC-021 | Domain test | Yes |
| AT-002 | Application boundary | Independent definition lifecycle creates, tests, versions, activates, deactivates, audits, and rejects stale or cross-workspace mutations without a consumer target | AC-001, AC-005, AC-007, AC-008, AC-015, AC-018, AC-021 | Application test | Yes |
| AT-003 | Application boundary | One language service parses, validates, formats, autocompletes, and explains DSL and visual projections while persisting only the canonical AST | AC-003, AC-004, AC-009, AC-012, AC-013, AC-014 | Application test | Yes |
| AT-004 | API boundary | Definition lifecycle, language, testing, activation, version history, and usage contracts expose generated frontend parity without consumer fields or localized stored logic | AC-001, AC-004, AC-005, AC-006, AC-008, AC-009, AC-018, AC-020 | API integration test | Yes |
| AT-005 | Application boundary | Rules has no dependency on consumer modules and built-in/workspace definitions use the same public semantic contract | AC-010, AC-019, AC-020 | Architecture test | Yes |
| AT-006 | UI component | One Rules grid opens create, read-only, edit, authoring, testing, version, activation, and usage dialogs and preserves collection state with targeted refresh | AC-004, AC-005, AC-009, AC-010, AC-011, AC-015 | UI component test | Yes |
| AT-007 | Browser journey | User creates, tests, versions, activates, revises, deactivates, and inspects a rule without leaving the grid, losing state, overflowing, or producing console errors | AC-001, AC-005, AC-006, AC-007, AC-008, AC-011, AC-018 | Browser automation | Yes |

## Out Of Scope

- Consumer target schemas, authorization, lifecycle, runtime context construction, result reactions, transactions, and side effects.
- Arbitrary executable expressions, scripts, plugins, webhooks, notifications, workflow orchestration, remote data access, and persisted localized explanations.
- Editing an immutable version or silently retargeting existing bindings when another version is activated.

## Screen flow

| Surface | Required contract |
|---|---|
| Rules collection | Render one primary `DataTable` with definition identity, origin, active version, activation state, usage count, and contextual actions. |
| Create, view, and edit | Consume the managed-dialog record-tab contract with General first, Behavior as the primary business section, Usage for published-version relationships, and optional user-relevant system information last. Behavior presents one compact semantic `Inputs -> Logic -> Outputs` sequence with consistent markers, connectors, and alignment. View expressions are static read-only content; explicit authoring help opens the canonical guide where functions and logical operators use one reference interaction. Create and edit use form controls without changing the sequence. |
| Authoring | Keep every supported authoring projection synchronized through canonical logic; syntax text and visual composition are never separate stored truths. |
| Test | Open a dialog with typed sample inputs and distinct match, non-match, invalid-input, and evaluation-failure states. |
| Versions and activation | Open dialogs for history, version creation, activation, and deactivation; use `AlertDialog` when confirmation is destructive. |
| Usage | Render exact-version bindings in the managed dialog's Usage tab; actionable binding lifecycle remains owned by [docs/use-cases/rules/manage-rule-bindings.md](./manage-rule-bindings.md). |

Required UI quality: use the existing shadcn and Tailwind system only; keep labels, focus, keyboard operation, errors, loading, empty, pending, and success states explicit; return focus to the invoking grid control; preserve URL-backed filter, sort, page, and selected-row state; use `Toast` after successful mutations; refresh only affected definition or usage queries; and do not add create, edit, or detail routes, sheets, drawers, or side panels.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Domain | Partial |
> | Application | Partial |
> | Infrastructure | Partial |
> | API | Partial |
> | Frontend | Partial |
>
> **Gaps vs spec:**
>
> | ID | Gap |
> |---|---|
> | GAP-001 | Stable input identity across label edits, separate activation, safe DSL parsing/autocomplete, generated explanation ownership, complete lifecycle dialogs, usage discovery, and targeted refresh remain incomplete; current definition, immutable-version, canonical-condition, evaluator, persistence, and catalog primitives are reusable. |
>
> **Deferred follow-ups:** N/A. Binding lifecycle and runtime consumption are separate in-scope Rules use cases, not deferred definition behavior.
>
> **Verification:** Existing evidence is recorded in the sibling sidecar; refreshed acceptance evidence is required after the clean refactor.
>
> **Decisions:** No supported production consumer or data requires compatibility for the replaced Rule surfaces, so the current contract uses a clean cutover without lowering production quality. `Inputs -> Logic -> Outputs` is the durable Rule contract; the current language capability remains a bounded positive-assertion Boolean predicate until a use case approves an additional typed-output capability. `true` means the stated assertion is satisfied. Canonical logic is authoritative and every editor or explanation is a projection. Version creation and activation are separate. Activation controls eligibility for new bindings and never retargets existing exact-version bindings. Retired consumer-specific rule models, alternate semantic views, syntax persistence, and compatibility aliases are not retained.
