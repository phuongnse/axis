# Notifications

Detailed reference for the `notify` operation, in-app and email
notifications, user profiles and the mail settings. The conventions of
[architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_
are not built yet, and Dn refers to [decisions.md](../decisions.md).

The [user profiles](#user-profiles) section, the compile checks of the notify
operation and of placeholders, the run of the notify step and of a task
escalation's `notify`, and the storage of in-app notifications, the routes that
list them and mark them read, and their
retention are built, and so is the bell in the site shell. Email is
_(planned for M14)_, as is everything else in this file that says so. The design follows
[D73](../decisions/d73-in-app-and-email-notifications.md). Dates follow the
application time zone of
[D75](../decisions/d75-the-application-time-zone.md). New compile problems are
listed as new diagnostics.

## Notify operation

```json
{
  "name": "tellRequester",
  "type": "operation",
  "operation": "notify",
  "to": [{ "user": "requester" }],
  "channels": ["inApp", "email"],
  "message": {
    "subject": { "textKey": "ticket.resolved.subject" },
    "body": { "textKey": "ticket.resolved.body" },
    "values": { "number": "number", "resolution": "resolution" }
  },
  "open": { "page": "TicketDetail", "parameters": { "ticket": "id" } },
  "next": "confirm"
}
```

- `to` is a non-empty list (`AXC0266` at `/steps/{i}/to`). Each recipient has
  exactly one of (`AXC0267` at `/steps/{i}/to/{j}`):
  - `user`, an expression that gives a user id, as a task's `user` assignee;
  - `role`, a declared role name, compared exactly (`AXC0268` at
    `/steps/{i}/to/{j}/role` for an unknown one). A role whose file failed to
    load is not reported again;
  - `email`, an expression that gives an address. It is for the `email`
    channel only.
- `user` and `email` are `text` expressions with the scope of a `when`. A
  problem is reported with its expression code at `/steps/{i}/to/{j}/user` or
  `/steps/{i}/to/{j}/email`.
- `channels` is a non-empty list of `inApp` and `email` (`AXC0269` at
  `/steps/{i}/channels`). A channel named twice is a schema violation
  (`AXC0004`).
- A `role` recipient with the `email` channel is `AXC0270` at
  `/steps/{i}/to/{j}/role`. Axis has no directory to expand a role into
  addresses.
- `message.subject` and `message.body` are text keys, checked as a label is at
  `/steps/{i}/message/subject` and `/steps/{i}/message/body`. `values` maps
  each placeholder name to an expression with the scope of a `when`. A problem
  is reported at `/steps/{i}/message/values/{name}`. A value may have any type,
  because it is formatted by its type, so only its syntax, names and rules are
  checked.
- `open` is optional. It names a page of the application and maps its page
  parameters, as a navigate action does (`AXC0232` to `AXC0234`, at
  `/steps/{i}/open`). The page is a `navigate page` edge in the reference
  index. The in-app notification opens it.
- The step stores the notification and stages its emails in its own
  transaction, then goes to `next`. A step that rolls back sends nothing. Its
  history output is `{ "next", "notificationId" }`.
- A process without a subject may notify too. A task escalation's `notify`
  has the same shape, without `next`.

## Placeholders

A text value may hold named placeholders, such as
`"Ticket {number} is resolved: {resolution}"`.

- A name is ASCII letters and digits. `{{` and `}}` write a brace. A name and a
  `values` key match exactly, letter case included.
- Each placeholder of a message's texts needs a key in `values` (`AXC0271`),
  and each key of `values` must appear in the subject or the body (`AXC0272`),
  in every locale that has both texts. Each mismatch is reported at
  `/steps/{i}/message`, naming the locale and the name.
- A lone or invalid brace is `AXC0273` at `/steps/{i}/message`. A locale with
  one is reported for that brace only.
- A value whose expression reads a field marked `sensitive` is `AXC0282` at
  `/steps/{i}/message/values/{key}`, naming the field. It covers a field of the
  subject, a field reached through a path such as `assignee.salary`, one read
  inside an aggregate and one passed to a rule. A notification never carries a
  sensitive value. Only `values` is checked, not a page parameter or a
  recipient expression. The check uses the release being compiled.
- Values are formatted for the reader when the notification is read. A built-in
  table gives the formats, because the backend runs with invariant
  globalization and cannot load the data of a locale. A locale whose language
  is `vi` uses `vi`, and every other locale uses `en`.

  | Value type | `en` | `vi` |
  | ---------- | ---- | ---- |
  | `integer`, `decimal` | `1,234.5` | `1.234,5` |
  | `date` | `Jan 1, 2027` | `01/01/2027` |
  | `date-time` | `Jan 1, 2027, 1:00 AM` | `01/01/2027 01:00` |

  Every digit of a number is kept, so formatting never rounds. A `date-time`
  shows in the time zone of the release the notification was stored with, so
  `2026-12-31T18:00:00Z` in an `Asia/Ho_Chi_Minh` application shows on
  1 January 2027 at 01:00. A `date` shows as a local date with no shift. Text,
  enum, boolean, id and reference values show as written. The locale is the
  user's profile locale, even when the release has no texts in it. Without a
  profile locale, it is the locale of the texts that were chosen.

## In-app notifications

- **Storage.** `axis.notifications` holds one row per notification and
  recipient: the application, the release, the recipient kind and recipient,
  the text keys, the raw evaluated values with the type of each (`text`,
  `integer`, `decimal`, `boolean`, `date`, `date-time`, `enum` or `reference`),
  the declared name of the page to open with its raw parameters, and the time. All the rows of one step run
  share the notification's id. A role recipient is one row for the role. A
  `user` expression that gives `null` or blank text adds no row, and two
  recipients of one kind and name add one row. The values are stored as an
  event payload is, with dates as ISO text, and are formatted when the
  notification is read, because a role row reaches users with different
  locales. A notification stored before the types were kept has none, so its
  numbers are still formatted and its text shows as written. A step with only the `email` channel stores no row.
- **Who sees one.** A user sees the notifications addressed to their user id,
  and those addressed to a role they hold that the active release declares,
  checked when read, as a role task is.
- **Read state.** `axis.notification_reads` holds one row per notification
  and user who read it.
- **Routes.**
  - `GET /api/apps/{app}/notifications?page=&pageSize=&unread=` lists the
    user's notifications, newest first. It answers `items`, `page`, `pageSize`,
    `totalCount` and `unreadCount`. With `unread=true` the items and
    `totalCount` are the unread ones only, and `unreadCount` always counts
    every unread one. An item has `id`, `releaseId`, `subject`, `body`, `open`,
    `read` and `createdAt`. `open` is `{ "page", "parameters" }` with the
    stored raw parameters, or null when the notification opens no page. A
    notification sent to the user and to a role the user holds is listed once.
    A bad `page`, `pageSize` or `unread` (`true` or `false`) is a 400 that
    lists every bad parameter.
  - Texts come from the notification's release, in the user's profile locale
    when that release has texts for it, otherwise its first locale in ordinal
    order, as for [email](#email). Each `{name}` is filled with the stored value
    formatted as [placeholders](#placeholders) says: a number, a date and a
    date-time in the user's locale, a string as it is, a boolean as JSON writes
    it and null as empty text. A text key the locale does not have shows as the
    key.
  - `POST /api/apps/{app}/notifications/{id}/read` marks one read, and
    `POST /api/apps/{app}/notifications/read-all` marks every notification the
    user sees now read. Both write read rows for the caller only, so another
    recipient's unread count does not change. Both answer 204 with no body, and
    so does marking a notification that is already read. A notification the
    user does not see, or that does not exist, is a 404.
  - Nobody deletes a notification. The routes need a session (401), and the
    POSTs follow the CSRF rule.
- **Kept for.** `Notifications:RetentionDays`, default 90, a whole number of at
  least 1. The worker reads and checks it at start. It deletes the notifications
  whose `created_at` is older than that by its own clock, with their
  `axis.notification_reads` rows in one transaction, when the tenant becomes
  ready and then every 24 hours.
- **Site shell.** A bell in the header of an application site shows the
  signed-in user's unread count for that application, and no count when
  nothing is unread. The platform site has no bell. `GET /api/sites/{path}`
  gives the address of the notification routes as `notificationsPath`.
  - The bell is a button with the accessible name "Notifications, 2 unread", or
    "Notifications" with no count. A polite live region announces a change of
    the count.
  - The bell opens a panel that lists the notifications newest first, 10 to a
    page, each with its subject, body and time. Unread rows are marked. A Mark
    all read button sits at the top.
  - A row opens its page, if it has one, with its parameters and marks the
    notification read. A row without a page only marks it read.
  - The count reloads when the site loads, on every page navigation, when the
    panel opens and after any mark read. There is no timer. A failure to read
    the list hides the bell and never starts sign-in.

## Email

- **Outbox.** For each email recipient, the step's transaction renders the
  subject and body from the pinned release's texts in the recipient's locale,
  and stores them in `axis.email_outbox` with the address and a stable
  `Message-ID`. It queues one `notification.email` work item per row.
- **Sending.** The item sends the email over SMTP, outside any transaction,
  then marks the row `sent`. A failure is counted and tried again with the
  backoff and limit of [dead letters](process-events.md#dead-letters), then
  becomes a dead letter of kind `notification.email`.
- **At least once.** A crash after the send and before the mark sends again.
  The same `Message-ID` lets a receiver drop the repeat.
- **Locale.** The recipient's profile locale when the release has texts for
  it, otherwise the first locale of the release in ordinal order.
- **Body.** Plain text, with the page link as an absolute URL on the tenant's
  host when `open` is set.
- **Logs.** A `user` recipient without a profile or an email gets no email.
  The worker logs it at Information level with the step and the user id,
  never the address.
- **Scenarios.** In a test scenario, email is stubbed. An `expectNotification`
  step checks the recipient, the channel and the values of a notification.

## User profiles

Each sign-in writes the user's profile to `axis.user_profiles` in the tenant
database: the user id, the display name, the email, the locale, the first
sign-in and the last sign-in. A later sign-in updates it.

- The email and locale come from the claims the tenant names in
  `Oidc:Claims:Email` and `Oidc:Claims:Locale`, by default `email` and
  `locale`. A missing claim leaves the value null. Sign-in asks the identity
  provider for the `email` scope, so providers other than Keycloak put the
  email in the ID token.
- A failure to write the profile does not refuse the sign-in. The server logs a
  warning that does not include the email.
- The profiles are not a directory. They serve email addresses, locales and the
  user picker of a [reassign](task-deadlines.md#reassign), and no other route
  lists them.

## Mail settings

| Setting | Default | Meaning |
| --- | --- | --- |
| `Email:Smtp:Host` | none | The SMTP server. Without it, no email is sent and the worker logs a warning once per tenant. |
| `Email:Smtp:Port` | `587` | The SMTP port. |
| `Email:Smtp:UseTls` | `true` | Use STARTTLS, or TLS on port 465. |
| `Email:Smtp:UserName` | none | The SMTP user. |
| `Email:Smtp:Password` | none | The SMTP password, from the environment. |
| `Email:From` | none | The sender address. Required when a host is set. |
| `Notifications:RetentionDays` | `90` | How long in-app notifications are kept. A value that is not a whole number of at least 1 stops the worker at start. |

A tenant may override `Email` in its `Tenants` block, or in the tenant
directory under `Tenancy:Source=directory`, where the password is encrypted as
the OIDC client secret is. MailKit sends the email. Docker Compose,
`scripts/dev.sh` and the integration tests run Mailpit as the mail server.
