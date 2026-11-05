# D101. The extensibility milestone is split into six

Status: **Agreed**

## Context

The extensibility milestone held six designs and the verification work carried over from the
hardening milestone. As one milestone it would stay open while most of its parts were long done, and
its docs sweep would touch every reference file at once. The verification work had already been
carried over once.

## Decision

- **The carried-over verification is a milestone of its own,** with no new feature, so it closes
  without waiting for the extensibility work.
- **Each extensibility design is a milestone of its own:** connector OAuth2 and outbound webhooks,
  modules that ship working features, package runners, custom widgets and the published SDK, and the
  operator console. Their names, scope and phases are in the [roadmap](../roadmap.md#milestones).
- **Each ends with its own step of the purchase request journey** of
  [D94](d94-purchase-requests-prove-the-extensibility-milestone.md), so each still ends with an
  end-to-end journey.
- **Each keeps the [ASVS](../reference/asvs.md) rows and the load test met** for the routes it adds.
- **A milestone waits only for what its journey needs.** Custom widgets wait for the modules
  milestone, because the sample widget shows the budgets of the `department-budgets` module, and the
  operator console waits for webhooks, because its journey redelivers a webhook's dead letter. The
  readiness check of [D68](d68-issues-of-several-milestones-run-at-once.md) decides when each one is
  broken down.

The designs themselves do not change with the split.

## Consequences

- Each design has a "Done when" that one journey step checks, and a docs sweep that touches only its
  own files.
- There are more docs sweeps, and more marks between milestones that the readiness check must get
  right.
