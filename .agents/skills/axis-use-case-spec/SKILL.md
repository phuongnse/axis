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
2. Establish one primary actor goal, supporting actors, preconditions, trigger, success and minimal guarantees, flows, boundaries, decisions, failure behavior, applicable enterprise-production outcomes, and any evidence-backed compatibility constraint from the source priority in [AGENTS.md](../../../AGENTS.md). Split another independently valuable actor goal into its own use case; title conjunctions and alternate actors are review signals, not mechanical proof that a split is required. A separately scoped capability may remain out of scope, but a production concern required by the implemented slice is a blocking decision rather than a deferred follow-up. When no supported production consumer/data requires compatibility, state the clean replacement and do not invent an overlap period.
3. Keep the narrative implementation-agnostic: describe observable behavior and guarantees, not internal databases, caches, queues, tables, frameworks, storage formats, or coordination mechanics. Preserve a named technology only when interoperability with that technology is itself part of the product contract. Link shared invariants and technical realizations to their single architecture, stack, playbook, or foundation owner instead of copying them locally.
4. Author cohesive ACs and the high-level Acceptance Test Matrix through [reference.md](./reference.md); unresolved expected behavior remains an open decision, not a required test.
5. For a client journey, apply [docs/playbooks/client-experience.md](../../../docs/playbooks/client-experience.md) and define the implementation-agnostic user outcome, entry, primary path, alternate/recovery paths, exit, modes, visible state model, Screen flow, and Required UI quality. Resolve ordinary UX choices from the contract and current product patterns; keep only behavior-changing ambiguity as an open product decision. This workflow **Delegates** diagram/document hygiene to `$axis-doc-hygiene` without moving product ownership.
6. For a high-risk security or privacy protocol, carry a threat-model record from `$axis-design-gate`: assets, entry points, trust boundaries, abuse cases, mitigations, and proving tests. Promote only durable boundaries and mitigations to their architecture or product owner; keep review provenance transient.
7. Mark layer status honestly; exact evidence paths and Axis commands belong only in the sibling evidence sidecar when implementation exists.
8. Run `python scripts/axis.py check use-case-docs` plus link checks when links/anchors changed, then return readiness and open decisions to the caller.

## Output

Report owner/spec, readiness, experience contract, resolved/open product decisions, AC/AT scope, visual/doc changes, checks, and next owner.
