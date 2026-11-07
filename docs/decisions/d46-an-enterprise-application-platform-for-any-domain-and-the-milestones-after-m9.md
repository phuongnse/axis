# D46. An enterprise application platform for any domain and the milestones after M9

Status: **Agreed**

## Context

By M9 the runtime was strong, but an author still needed JSON, git and the admin tool, and the
runtime was narrow: a page held one widget, processes had no timer, sub-process or loop, and there
were no notifications, files or import. Both samples had one shape, a request then an approval. A
platform that suits one shape, or one domain such as ERP, is not general.

## Decision

- **Axis is an enterprise application platform for any domain.** People build applications of any
  business domain on it. An author creates, configures, tests, publishes and runs an application
  through Studio, without editing JSON or code.
- **Files stay the source of truth,** as [D5](d5-configuration-is-authored-as-files-first.md) says.
  Studio and every designer write the same resources through the authoring API.
- **No domain gets platform code.** Samples only prove the platform. Built-in modules serve real
  businesses as configuration that Axis maintains, see
  [D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md).
- **Visual designers are in scope:** the page and form designers and the process designer. Custom
  widgets come after them, see [D8](d8-custom-ui-widgets-are-packaged-react-components.md).
- **Hardening follows the model.** The FAPI 2.0 profile, ASVS verification and the load tests come
  once the model is stable, because their results mean something only then.
- **Migrating live business data between installations is out of scope.** Hosted operations are in
  scope as [D117](d117-small-businesses-first-and-the-milestones-in-three-phases.md) and
  [D118](d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) agree.
- **One format, changed in place, until the first production release.** Each resource kind has one
  format. A change edits it in place, with no second version of the kind, no compatibility code and
  no `formatVersion` bump, and the same pull request updates the schema, the compiler, the samples,
  the built-in modules, the scenarios and the tests. A development or test database whose stored
  releases use an older format is reset, not upgraded, and no doc or milestone name numbers a
  second version. What follows the first production release is
  [D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md)'s choice.
- **The milestones follow what an author does:** create an application, then its data and UI, logic
  and workflow, permissions, try it out, validate and test, publish and operate. Each ends with an
  end-to-end journey. Their list and order are in the [roadmap](../roadmap.md#milestones).
- **Samples of new shapes prove that it is general:** a service desk, with task deadlines, rework
  loops and notifications, then asset management, with dashboards, many-to-many, files and import.
  They need no platform code. The extensibility milestones extend purchase requests instead, see
  [D94](d94-purchase-requests-prove-the-extensibility-milestone.md), and phase 1 proves its work on
  the built-in modules wherever it can, see
  [D117](d117-small-businesses-first-and-the-milestones-in-three-phases.md).

## Consequences

- Studio has to cover every resource kind, so no author needs a file editor.
- One format means no compatibility code to carry, and development and test databases are reset
  after a format change.
- Each new sample shape tests the platform's generality, and a gap it finds is fixed in the
  platform, not in the sample.
