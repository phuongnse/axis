# D117. Small businesses first, and the milestones in three phases

Status: **Agreed**

The owner asked on 2026-10-30 for Axis to serve individuals and small and medium businesses first,
and large organizations, banks and government later, and for the planned milestones to be ordered
by that goal, and agreed these points on 2026-10-31. No planned work is dropped. Each part keeps its design and moves to the phase that
needs it. This changes the order and the milestone of parts of
[D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md),
[D101](d101-the-extensibility-milestone-is-split-into-six.md),
[D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md),
[D107](d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md) and
[D110](d110-security-alerts-and-a-hardening-checklist-for-the-operator.md), not their designs. See
the [roadmap](../roadmap.md#milestones).

- **Who Axis serves first:** individuals and small and medium businesses that run their internal
  work on Axis, such as approvals, requests and registers.
  - They have no IT team and no operations team. A business analyst, an office manager or the
    owner of the business sets the application up in Studio.
  - They start from a built-in application and change it to fit, instead of designing one from
    nothing.
  - They do not install software. They use Axis as a service, _(planned for M31)_.
  - Large organizations, banks and government come in phase 3. What only they ask for waits for
    them: conformance runs and certification, signed images and SBOMs for their own registries,
    and alert rules and a hardening checklist for their own operations teams.
- **What sets Axis apart:** built-in modules that a business changes anywhere in Studio, that keep
  their upgrades, and whose contract still holds on every write, as D102 agrees. Phase 1 ends when
  this holds for the first modules, `organization` and `approvals`.
- **Three phases:**
  - **Phase 1, the first small businesses:** M19, M20 and M27, which are running, then M24, M25
    and M26, then M23, M28 and M31.
  - **Phase 2, growth:** M29 Module upgrades, M30 More built-in modules, M21 Package runners and
    M22 Custom widgets and the published SDK.
  - **Phase 3, large organizations and government:** M32 Assurance for large and regulated
    customers.
- **M25 keeps the layer, and M29 takes upgrades:**
  - M25 builds the layer under `customizations/<module id>/`, compilation that applies it and
    checks the contract, and Studio that writes it and marks what it changed.
  - M29 Module upgrades builds module migrations, the build check that refuses an element gone
    without a migration, the three-way merge with its conflicts in Studio, and eject.
  - A layer from M25 already holds only the changes, by element name, so no business copies a
    module and the upgrade path D102 promises stays open. D102's rule that no production release
    comes before the layers of M25 still holds.
  - M29 must be done before Axis ships a new version of a built-in module to a business that has
    a layer over it.
- **M26 builds the first two modules, and M30 the rest:**
  - M26 builds `organization` and `approvals` and the built-in application that uses them. The
    `departments` sample module and the `request-approval` template become them. Its journey does
    not change.
  - M30 More built-in modules builds `service-desk` and `assets`, from the service desk and the
    asset management samples, and the asset management scenarios for import and attachments that
    M18 carried over.
- **M28 keeps what small businesses need, and M32 takes the rest:**
  - M28 keeps the users page's disable, enable and reset authenticator buttons, keeping the
    realm's redirect addresses in step with `tenant host add`, `remove` and `move`, the Trivy
    scan of the images in CI, the hourly driver's issues for high and critical Dependabot alerts,
    the weekly CI run of the load test and the final query limits.
  - M32 Assurance for large and regulated customers takes the release workflow that signs the
    images with cosign and publishes their CycloneDX SBOMs, from D107, the alert rules, the
    hardening checklist and the startup warnings of D110, the FAPI 2.0 conformance run, the final
    ASVS sign-off and the first SAMM rating.
- **M27 stays whole in phase 1:** D109's reason holds for small businesses too. Vietnam's personal
  data rules apply to every business, and `organization` holds personal data of employees. M26
  still waits for M27.
- **M31 Hosted Axis:** Axis runs one installation that hosts the tenants of many small customers,
  and the owner operates it. This replaces D46's point that Axis-hosted SaaS operations are out of
  scope. Its own decision designs it before the readiness check of
  [D68](d68-issues-of-several-milestones-run-at-once.md): how a customer gets a tenant, whether
  sign-up is self-service, and what the platform operator watches. Until then its roadmap row
  states the goal only. Migrating live business data between installations stays out of scope.
  - [D118](d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) designs it, renames it M31 Axis SaaS, and adds M33 Sign-up and trials and
    M34 Plans and billing to phase 1.
  - [D119](d119-ai-authoring-in-studio.md) adds M35 AI changes an application to phase 1 and
    M36 AI builds applications and modules to phase 2.
- **Order inside phase 1:**
  - M19, M20 and M27 finish first.
  - M24, M25 and M26 follow M20, one after the other, and M26 waits for M27, as before.
  - M23 waits for M19, as D101 agrees. M28 can run beside any of them.
  - M31 runs beside M24 to M26 once its decision is agreed.
- **Planning by phase:**
  - The local session runs the readiness check and breaks down only the milestones of the
    current phase, unless the owner asks for another.
  - Autopilot takes every open issue, so an issue of a later phase is filed only when the owner
    asks for it.
  - Phase 2 starts when every milestone of phase 1 is broken down. Phase 3 starts when the owner
    names a first large or regulated customer.
- **Samples:** they keep proving the platform as D46 and D94 say. A new milestone may still extend
  a sample, but phase 1 proves its work on the built-in modules wherever it can.

_Why:_ the planned milestones grew from the platform's own logic: each one made sense after the
one before, and nothing said where to stop. A first group of customers says where to stop. Small
businesses need ready applications they can change, little operations and a hosted service. They
do not ask for conformance runs, image signatures or alert rules, and those are what a large or
regulated customer's procurement asks for. Built-in modules with contracts are what sets Axis
apart, so they stay at the front, but only the parts that the first businesses use: the layer
without the merge, and two modules instead of four. The parts that move keep their designs, so
they can be broken down when their phase starts. The cost: a business cannot get a new module
version while M29 is not built, and a large or regulated customer that comes early waits for M32.
