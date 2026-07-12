---
name: axis-use-case-spec
description: Create or repair Axis product use-case contracts before implementation. Use when purpose, actor, flow, acceptance criteria, Acceptance Test Matrix, UI contract, decisions, status, or ownership is missing or ambiguous.
---

# Axis Use Case Spec

## Goal

Produce a testable product contract without inventing behavior or mixing implementation evidence into the spec.

## Hard gates

Follow [reference.md](../reference.md).
- Do not delegate implementation while blocking product decisions remain open.
- Do not invent behavior, identifiers, endpoints, tables, authorization, or integrations.
- An unapproved new product domain stops for user scope.

## Inputs

- User request, domain/slug candidate, and known decisions.
- Related use cases, code, tests, and product vocabulary found through `rg`.
- Blocking decisions only the user can supply.

## Workflow

1. Locate the owner under [docs/use-cases/README.md](../../../docs/use-cases/README.md); create a file/domain only within approved scope and never create placeholders.
2. Establish purpose, actor, trigger, flows, boundaries, decisions, and failure behavior from the source priority in [AGENTS.md](../../../AGENTS.md).
3. Author ACs and the Acceptance Test Matrix through [reference.md](./reference.md); unresolved expected behavior remains an open decision, not a required test.
4. Define implementation-agnostic Screen flow and Required UI quality when the journey needs them. This workflow **Delegates** diagram/document hygiene to `$axis-doc-hygiene` without moving product ownership.
5. Mark layer status honestly; exact evidence paths and Axis commands belong only in the sibling evidence sidecar when implementation exists.
6. Run `python scripts/axis.py check use-case-docs` plus link checks when links/anchors changed, then return readiness and open decisions to the caller.

## Output

Report owner/spec, readiness, resolved/open decisions, AC/AT scope, visual/doc changes, checks, and next owner.
