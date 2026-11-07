# D81. Asset management as the fourth sample application

Status: **Agreed**

## Context

The data modeling milestone adds files, money, labelled enum values, many-to-many, references from
child rows, import and export, and dashboards. The existing samples are two approvals and a service
desk, and none of them has the shape of a register.
[D46](d46-an-enterprise-application-platform-for-any-domain-and-the-milestones-after-m9.md) named asset management
as the sample for it.

## Decision

- **Asset management is the fourth sample,** in `samples/apps/asset-management`. An asset manager
  keeps a register of the company's assets, who holds them and their maintenance. Its shape is a
  register with a dashboard, not a request and an approval.
- **It uses every new kind of field:** files on child rows, money with a currency field, labelled
  status values, a `references` field, and child rows that point to another entity.
- **It imports and exports.** An asset manager imports assets from a CSV file, and an auditor
  exports the register.
- **It has its own roles,** `asset-manager`, `employee` and `auditor`, where the auditor reads and
  exports everything and writes nothing.
- **A Playwright journey builds it through Studio,** as the service desk journey does in
  [D56](d56-a-service-desk-as-the-third-sample-application.md), compares it with the committed
  folder, runs its scenarios from Studio, publishes it, and imports and exports on the site.

Its data, flow, security, pages, scenarios and journey are in the
[product brief](../product-brief.md#fourth-application-asset-management).

## Consequences

- Building it through Studio proves that an author can use every new field type without JSON.
- Its warranty reminder goes to a role, so it is sent in-app only, because email to a role is
  refused, see [notifications](../reference/notifications.md).
