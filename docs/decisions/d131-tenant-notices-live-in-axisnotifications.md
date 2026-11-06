# D131. Tenant notices live in axis.notifications

Status: **Agreed**

## Context

A tenant's administrators need to hear about the tenant as a whole, for example when the worker holds
emails past the [emails a day](../reference/tenant-quotas.md#emails-a-day) quota. No application sends
such a notice, while every notification of [D73](d73-in-app-and-email-notifications.md) belongs to an
application.

## Decision

- **A tenant notice is a row of `axis.notifications`** with no application and no release, so no
  application lists it.
- **Its texts are platform texts,** not texts of a release.
- **Its recipient is a platform role kind,** such as `administrator`, which the server resolves to the
  role that `Users:AdministratorRole` names when the notice is read.
- **Platform routes and a bell on the platform site** list the notices and mark them read.

The detail is in [in-app notifications](../reference/notifications.md#in-app-notifications).

## Consequences

- One table, one retention and one read-mark model serve every in-app notice, with no fake
  application.
- Changing `Users:AdministratorRole` needs no rewrite of stored notices.
