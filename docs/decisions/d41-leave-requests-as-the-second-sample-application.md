# D41. Leave requests as the second sample application

Status: **Agreed**

## Context

Purchase requests prove that the platform can run one application. A second application shows
whether the platform is reused, or whether the first one only works because the platform was shaped
around it.

## Decision

- **Leave requests is the second sample,** in `samples/apps/leave-requests`. An employee asks for
  days off, and the department manager approves, returns or rejects the request.
- **It is smaller than purchase requests and of the same shape,** a request then an approval. It
  uses tasks and policies as purchase requests do, takes its departments from the `organization`
  module, see [D124](d124-the-organization-and-approvals-modules.md), and has no finance step,
  connector or extension package.
- **It needs no platform code.** A platform change that it needs is a gap to raise, not to hide in
  the sample.
- **It reuses the realm roles `employee` and `department-manager`** of purchase requests, so the
  realm needs no new role.
- **It runs beside purchase requests** wherever the samples run, with its own test scenarios and one
  Playwright journey.

Its data, flow, security, pages and scenarios are in the
[product brief](../product-brief.md#second-application-leave-requests).

## Consequences

- Reuse of the engines and of a module is proven without a package or a connector.
- The sample proves nothing about other shapes of application, which the later samples cover, see
  [D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md).
