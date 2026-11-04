# D131. Tenant notices live in axis.notifications

Status: **Proposed**

A tenant's administrators need to hear about the tenant as a whole, for example when the worker holds emails past the
`emailsPerDay` quota ([tenant quotas](../reference/tenant-quotas.md#emails-a-day)). No application sends such a
notice. This decision proposes that it is an in-app notification of the platform:

- **The row.** A tenant notice is a row of `axis.notifications` with no application and no release, so
  `application_id` and `release_id` are null. An application never lists it.
- **The texts.** Its text keys are platform texts, such as `notices.emailsHeld.subject`, which the platform ships in
  English and Vietnamese. They are not texts of a release.
- **The recipient.** Its recipient kind is `platformRole` and its recipient is a platform role kind. The first kind is
  `administrator`, which the server resolves to `Users:AdministratorRole` when someone reads the notice. The row never
  holds the configured role name, so a change of that setting needs no rewrite.
- **The routes.** `GET /api/notifications`, `POST /api/notifications/{id}/read` and
  `POST /api/notifications/read-all` list and mark read the notices for the administrators, see the
  [reference](../reference/notifications.md#in-app-notifications).
- **The bell.** The platform site gets a bell that lists these notices, as an application site's bell lists the
  notifications of that application.

The row, the routes and the bell are built, while the decision is still proposed.

_Why:_ one table, one retention and one read-mark model serve every in-app notice, and a notice for the tenant needs no
fake application.
