# D46. A general-purpose low-code platform and the milestones after M9

Status: **Agreed**

Axis aims to be a general-purpose low-code platform. People build applications
of any business domain on it, not one domain such as ERP. This decision records
the direction, the scope changes and the milestones after M9. See the
[roadmap](../roadmap.md#direction-after-m9) for the milestone table.
The owner confirmed these points on issue #702 on 2026-10-19.

- **Direction:**
  - An author creates, configures, tests, publishes and runs an application
    through Studio, without editing JSON or code.
  - Files stay the source of truth
    ([D5](d5-configuration-is-authored-as-files-first.md)). Studio and
    every designer write the same resources through the authoring API.
  - No domain gets platform code. Samples are only the means to prove the
    platform.
- **Scope:**
  - Visual page and form designers, and a process designer, move into scope.
  - Custom widgets stay after the designers
    ([D8](d8-custom-ui-widgets-are-packaged-react-components.md)).
  - FAPI 2.0 and ASVS verification move to the Hardening milestone, M16.
  - Still out of scope: self-service SaaS provisioning, Axis-hosted SaaS
    operations, and migrating live business data between installations.
- **One format, changed in place:**
  - Until the first production release, each resource kind has one format.
  - A change edits that format in place. There is no second version of a kind,
    no compatibility code and no `formatVersion` bump.
  - The same pull request updates the schema, the compiler, the samples, the
    scenarios and the tests.
  - A development or test database whose stored releases use an older format
    is reset, not upgraded.
  - Docs and milestone names never number a second version.
- **Milestones after M9:**
  - They replace the earlier single Hardening milestone. They follow the vertical
    slice: create an application, then its data and UI, logic and workflow,
    permissions, try it out, validate and test, publish and operate.
  - Each milestone ends with an end-to-end journey.
  - M10 Publishing from Studio, M11 Authoring without JSON, M12 Pages, layouts
    and more widgets, M13 Visual page and form designer, M14 Workflow,
    automation and notifications, M15 Data modeling and data management, M16
    Hardening, and M17 Extensibility and operator console.
  - M11 and M12 can run in parallel, and so can M14 and M15.
  - The "Done when" text of each milestone is in the
    [roadmap](../roadmap.md#milestones).
- **Proving it is general:**
  - From M11 on, each milestone extends a sample of a different shape: a
    service desk, with SLAs, rework loops and notifications, then asset
    management, with dashboards, many-to-many, files and import.
    [D94](d94-purchase-requests-prove-the-extensibility-milestone.md) makes
    M17 the exception: it extends the purchase request sample.
  - These samples need no platform code.
- **How it is planned:**
  - Rolling wave stays as it is. M10 was broken into issues first. A later
    milestone is broken down when the one before it is nearly done.
  - Each milestone starts with docs issues that record its design as
    decisions, as M9 did.
- **Effect on earlier decisions:** D8 and D38 link here where this decision
  changes their scope. M10 plans publishing and activation from Studio, so
  [D47](d47-publishing-from-studio.md) replaces the "Studio never
  activates a release" point of D38.

_Why:_ the runtime is strong, but authors still need JSON, git and the admin
CLI. The runtime is also narrow for many kinds of applications. A page holds
one widget, `startProcess` is the only action, processes have no timer,
sub-process or loop, and there are no notifications, files, indexes or
import and export. Both samples have one shape, "request, then approval". A
platform that only suits that shape is not general. Hardening moves to M16
because load tests and ASVS numbers mean something only once the model is
stable. Only what a shared Studio server needs moves into M10: the key ring,
forwarded headers, audit of security events and CI scanning. Keeping one
format until the first production release means no compatibility code to
carry. The cost is that development and test databases are reset after a
format change. M11 and M12 run in parallel because Studio editors can build on
today's runtime while the page model grows. The cost is that some editors
change again in M12.
