# D117. Small businesses first, and the milestones in three phases

Status: **Agreed**

## Context

The planned milestones grew from the platform's own logic: each one made sense after the one before,
and nothing said where to stop. A first group of customers says where to stop. Small businesses need
ready applications they can change, little operations and a hosted service. They do not ask for
conformance runs, image signatures or alert rules, which are what a large or regulated customer's
procurement asks for.

## Decision

- **Axis serves individuals and small and medium businesses first,** and large organizations, banks
  and government later. Who they are and what they need is in the
  [product brief](../product-brief.md#who-axis-serves-first).
- **What sets Axis apart is its built-in modules:** a business changes them anywhere in Studio, keeps
  their upgrades, and their contract holds on every write, as
  [D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md) agrees.
- **The planned milestones are ordered in three phases,** by who needs them:
  - **Phase 1, the first small businesses:** what they need to start from a built-in application,
    change it to fit and use Axis as a service, with the first built-in modules, `organization`
    and `approvals`.
  - **Phase 2, growth:** module upgrades, more built-in modules, and the extension work for
    developers and partners. It starts when every milestone of phase 1 is broken down.
  - **Phase 3, large organizations and government:** what only they ask for, such as conformance
    runs and certification, signed images and SBOMs for their own registries, and alert rules and a
    hardening checklist for their own operations teams. It starts when the owner names a first large
    or regulated customer.
- **A milestone that mixes the phases is split, and each part keeps its design.** Phase 1 builds the
  customization layer, which already holds only the changes by element name, and phase 2 the module
  upgrades that merge it. Phase 1 builds the first two built-in modules, and phase 2 the rest. Phase 1
  keeps the security operations a small business needs, and phase 3 takes the rest. No planned work
  is dropped.
- **Only the current phase is broken down.** The readiness check of
  [D68](d68-issues-of-several-milestones-run-at-once.md) runs for the milestones of the current phase,
  and an issue of a later phase is filed only when the owner asks, because autopilot takes every
  open issue.

Which milestone is in which phase, and their order, is in the [roadmap](../roadmap.md#milestones).

## Consequences

- The parts that move keep their designs, so they can be broken down when their phase starts.
- A business cannot get a new version of a built-in module that it has a layer over until module
  upgrades are built.
- A large or regulated customer that comes early waits for phase 3.
