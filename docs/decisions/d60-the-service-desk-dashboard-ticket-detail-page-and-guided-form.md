# D60. The service desk dashboard, ticket detail page and guided form

Status: **Agreed**

## Context

Widget trees, the detail, chart and KPI widgets, delete and wizard steps need a sample that uses
them, and a proof that Studio alone can build them. The service desk of
[D56](d56-a-service-desk-as-the-third-sample-application.md) is the sample that a journey builds
only through Studio.

## Decision

The service desk sample adds:

- **a dashboard** for the lead, with KPIs and a chart above All tickets, in columns;
- **a ticket detail page** that My tickets and All tickets open with a `ticket` page parameter;
- **deleting a draft,** allowed only to its requester;
- **a two-step ticket form,** with a resolution that is required only when the ticket is resolved.

The Studio-only journey builds them too, and another journey uses the sample at phone width. The
detail is in [the service desk](../product-brief.md#third-application-service-desk).

## Consequences

- The committed sample still equals what Studio builds, at the cost of a longer journey.
