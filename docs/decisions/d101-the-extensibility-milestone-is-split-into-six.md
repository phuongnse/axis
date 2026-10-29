# D101. The extensibility milestone is split into six

Status: **Agreed**

The owner asked on 2026-10-28 for M18, Extensibility and operator console, to
be split into smaller milestones. It changes the milestone list of
[D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)
and the single journey of
[D94](d94-purchase-requests-prove-the-extensibility-milestone.md). The
designs of D88 to D93 do not change, only the milestone that builds each one.
M17 does not change. See the [roadmap](../roadmap.md#milestones).

- **Six milestones:**
  - M18 Carried-over verification: the work carried over from M16, unchanged.
  - M19 Connector OAuth2 and outbound webhooks:
    [D91](d91-connector-oauth2-and-structured-payloads.md) and
    [D92](d92-outbound-webhooks.md), and raising an event from an inbound
    endpoint call.
  - M20 Modules with processes, pages and policies:
    [D89](d89-modules-that-hold-processes-pages-and-policies-and-depend-on-other-modules.md).
  - M21 Package runners: [D90](d90-extension-packages-run-out-of-process.md).
  - M22 Custom widgets and the published SDK:
    [D88](d88-custom-widgets-in-the-designer-palette-and-a-published-sdk.md).
  - M23 Operator console: [D93](d93-an-operator-web-console.md).
- **Verification on its own:** M18 holds no new feature. Its load test, its
  dependency scan and its FAPI 2.0 and ASVS checks no longer wait for the
  extensibility work to finish. Each of M19 to M23 keeps the
  [ASVS](../reference/asvs.md) rows and the load test met for the routes it
  adds.
- **One journey each:** each of M19 to M23 ends with its own step of the
  purchase request journey of D94, so each milestone still ends with an
  end-to-end journey, as D46 requires. M18 ends with the asset management
  scenarios and the first run of the load test.
- **Order:** M18, M19, M20 and M21 can run in parallel. M22 waits for M20,
  because its sample widget shows the budgets of the `department-budgets`
  module. M23 waits for M19, because the console shows webhook deliveries and
  its journey redelivers a webhook's dead letter. The readiness check of
  [D68](d68-issues-of-several-milestones-run-at-once.md) still decides when
  each one is broken down.

_Why:_ M18 held six designs and the work carried over from M16, so one
milestone would stay open while most of its parts were long done, and its docs
sweep would touch every reference file at once. The verification work was
already carried over once. A milestone of its own, with nothing else in it,
lets it close without waiting for the rest. A milestone per design gives each
one a "Done when" that one journey step checks, and a docs sweep that touches
only its own files. The cost is six docs sweeps instead of one, and marks
between milestones that the readiness check must get right.
