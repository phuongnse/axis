---
name: axis-use-case-spec
description: Draft or complete Axis docs-first use-case specifications before implementation. Use when a requested feature or use case lacks a docs/use-cases README, acceptance criteria, flow, implementation status, wireframe or diagram inventory, or has unclear product/design decisions that must be resolved before using axis-use-case-implementation.
---

# Axis Use Case Spec

## Goal

Create or tighten the owning use-case spec so implementation can follow spec -> code without invented behavior, blank AC rows, placeholder docs, or missing visual artifacts.

## Workflow

1. Locate or create the owning spec.
   - Read `docs/use-cases/README.md`, the domain `README.md`, and `docs/use-cases/USE_CASE_TEMPLATE.md`.
   - Search existing docs and code with `rg -n "<feature words>" docs/use-cases src tests frontend`.
   - If no use-case folder exists, create `docs/use-cases/{domain}/{slug}/README.md` from the template and add the domain README link.
   - If the domain itself does not exist, stop and ask for scope unless the user explicitly approved a new domain.

2. Establish product boundaries before writing behavior.
   - Capture Purpose, Primary actor, Trigger, Main flow, Alternate/error flows, Acceptance Criteria, Out of scope, and Decisions.
   - Use the source priority from `AGENTS.md`: use-case ACs, then AGENTS, then playbooks, then same-module code.
   - Ask the user for blocking decisions such as permission model, billing behavior, data ownership, API exposure, cross-module effects, or UI journey.
   - Do not invent IDs, events, endpoints, table names, roles, plans, or copy that changes product meaning.

3. Write acceptance criteria for implementation.
   - Group ACs under `Happy path`, `Validation & errors`, `Edge cases`, and `Out of scope`.
   - Keep checkboxes unchecked because use-case checkboxes are spec-only.
   - Include enough validation, isolation, permission, dependency-failure, rollback, and empty-state ACs for the layer that will be implemented.
   - Split oversized work into isolated slices and record the slice boundary in `Decisions` or `Deferred follow-ups`.

4. Define visuals and diagrams.
   - For user-facing screens, use `$axis-visual-artifact` to create or update Excalidraw sources, generated SVG previews, and the `## Wireframes` table.
   - Add `## Screen flow` when the journey has more than three screens, branched happy paths, or non-obvious error screens.
   - Add Mermaid diagrams for non-trivial workflow, sequence, or cross-module behavior; keep local diagrams in the owning README.
   - Use a single `N/A` row when no wireframe or local diagram applies.

5. Mark implementation status honestly.
   - Add the `> **Implementation status**` callout after Out of scope using the template layer table.
   - Set unimplemented layers to `⏳`, non-applicable layers to `N/A`, and partial existing work to `⚠️` with `Gaps vs spec`.
   - Name exact deferred AC bullets under `Deferred follow-ups`, or write `N/A`.
   - Update the domain README Open work only when the new spec changes prioritized work.

6. Route follow-up implementation.
   - Use `$axis-api-contract` for new or changed REST/OpenAPI/API type surfaces.
   - Use `$axis-cross-module-contract` for events, commands, jobs, saga steps, Kafka, RabbitMQ, Wolverine, gRPC, Avro, or proto work.
   - Use `$axis-frontend-feature` for SPA routes, feature folders, forms, data fetching, or UI behavior.
   - Use `$axis-use-case-implementation` only after the owning spec exists and blocking decisions are resolved.

7. Verify the spec.
   - Run `python scripts/axis.py check use-case-docs`.
   - Run `python scripts/axis.py check doc-navigation`.
   - Run `python scripts/axis.py check markdown-links` when Markdown links or anchors changed.
   - Run `python scripts/axis.py generate wireframes` and the visual artifact checklist when Excalidraw or SVG previews changed.

## Output

Report:

```text
Spec created/updated:
- ...

Decisions resolved:
- ...

Open decisions:
- ...

Visual artifacts:
- created/updated/N/A

Next skill:
- $axis-use-case-implementation / $axis-api-contract / $axis-cross-module-contract / $axis-frontend-feature / none yet

Checks:
- command -> pass/fail/not run with reason
```
