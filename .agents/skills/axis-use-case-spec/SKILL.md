---
name: axis-use-case-spec
description: Create or repair Axis product use-case contracts before implementation. Use when purpose, actor, user outcome, journey, flow, acceptance criteria, Acceptance Test Matrix, client experience contract, decisions, status, or ownership is missing or ambiguous.
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
2. Establish purpose, actor, trigger, flows, boundaries, decisions, failure behavior, applicable enterprise-production outcomes, and any evidence-backed compatibility constraint from the source priority in [AGENTS.md](../../../AGENTS.md). A separately scoped capability may remain out of scope, but a production concern required by the implemented slice is a blocking decision rather than a deferred follow-up. When no supported production consumer/data requires compatibility, state the clean replacement and do not invent an overlap period.
3. Author ACs and the Acceptance Test Matrix through [reference.md](./reference.md); unresolved expected behavior remains an open decision, not a required test.
4. For a client journey, apply [docs/playbooks/client-experience.md](../../../docs/playbooks/client-experience.md) and define the implementation-agnostic user outcome, entry, primary path, alternate/recovery paths, exit, modes, visible state model, Screen flow, and Required UI quality. Resolve ordinary UX choices from the contract and current product patterns; keep only behavior-changing ambiguity as an open product decision. This workflow **Delegates** diagram/document hygiene to `$axis-doc-hygiene` without moving product ownership.
5. Mark layer status honestly; exact evidence paths and Axis commands belong only in the sibling evidence sidecar when implementation exists.
6. Run `python scripts/axis.py check use-case-docs` plus link checks when links/anchors changed, then return readiness and open decisions to the caller.

## Output

Report owner/spec, readiness, experience contract, resolved/open product decisions, AC/AT scope, visual/doc changes, checks, and next owner.
