# D94. Purchase requests prove the extensibility milestone

Status: **Agreed**

## Context

The extensibility milestones extend the platform's reach into code and operations: modules with a
dependency, custom widgets, connectors with OAuth2, packages out of process, webhooks and the
operator console. [D46](d46-an-enterprise-application-platform-for-any-domain-and-the-milestones-after-m9.md) asks
each milestone to extend a sample of a new shape, but these milestones add no new kind of
application. Their journey needs packages that the operator installs and modules that are not edited
in Studio, see
[D89](d89-modules-that-hold-processes-pages-and-policies-and-depend-on-other-modules.md), so the
samples that Studio builds, the service desk and asset management, cannot hold it.

## Decision

- **The extensibility milestones extend the purchase request sample,** which already has a
  connector, an extension package, line items and an approval process. No new sample is added.
- **One journey, a step for each milestone.** Each milestone that
  [D101](d101-the-extensibility-milestone-is-split-into-six.md) splits out ends with its own step: a
  module with a dependency, a packaged widget that the author drags onto a page in Studio, which is
  the only Studio step, a connector with OAuth2 and a structured payload, a package out of process,
  and a webhook whose dead letter the platform operator redelivers from the console.

The journey is in the [product brief](../product-brief.md#extensibility), and the sample module in
[modules and templates](../reference/modules-and-templates.md).

## Consequences

- This is the one exception to D46's rule of a new shape for each milestone.
- The purchase request sample grows with every extensibility milestone, and its journey with it.

## Alternatives

- **A new sample for extensibility.** It would add a sample and a journey that prove nothing the
  purchase request sample cannot.
