# Notifications

Detailed reference for the `notify` operation, in-app and email
notifications, user profiles and the mail settings. The conventions of
[architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_
are not built yet, and Dn refers to [decisions.md](../decisions.md).

The compile checks of the notify operation and of placeholders are built. The
rest of this file is _(planned for M14)_. The design follows
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

- `to` is a non-empty list (`AXC0244` at `/steps/{i}/to`). Each recipient has
  exactly one of (`AXC0245` at `/steps/{i}/to/{j}`):
  - `user`, an expression that gives a user id, as a task's `user` assignee;
  - `role`, a declared role name, compared exactly (`AXC0246` at
    `/steps/{i}/to/{j}/role` for an unknown one). A role whose file failed to
    load is not reported again;
  - `email`, an expression that gives an address. It is for the `email`
    channel only.
- `user` and `email` are `text` expressions with the scope of a `when`. A
  problem is reported with its expression code at `/steps/{i}/to/{j}/user` or
  `/steps/{i}/to/{j}/email`.
- `channels` is a non-empty list of `inApp` and `email` (`AXC0247` at
  `/steps/{i}/channels`). A channel named twice is a schema violation
  (`AXC0004`).
- A `role` recipient with the `email` channel is `AXC0248` at
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
- Each placeholder of a message's texts needs a key in `values` (`AXC0249`),
  and each key of `values` must appear in the subject or the body (`AXC0250`),
  in every locale that has both texts. Each mismatch is reported at
  `/steps/{i}/message`, naming the locale and the name.
- A lone or invalid brace is `AXC0251` at `/steps/{i}/message`. A locale with
  one is reported for that brace only.
- _(Planned for M14)_ A value whose expression reads a field marked
  `sensitive`, directly or through a path, is a new diagnostic. A notification
  never carries a sensitive value.
- _(Planned for M14)_ Values are formatted in the recipient's locale: numbers
  with its separators, enum values as written, and dates and times in the
  application's time zone.

## In-app notifications

- **Storage.** `axis.notifications` holds one row per notification and
  recipient: the application, the release, the recipient kind and recipient,
  the text keys, the formatted values, the page to open and the time. A role
  recipient is one row for the role.
- **Who sees one.** A user sees the notifications addressed to their user id,
  and those addressed to a role they hold that the active release declares,
  checked when read, as a role task is.
- **Read state.** `axis.notification_reads` holds one row per notification
  and user who read it.
- **Routes.**
  - `GET /api/apps/{app}/notifications?page=&pageSize=&unread=` lists the
    user's notifications, newest first, with `unreadCount`. Texts come from the
    notification's release.
  - `POST /api/apps/{app}/notifications/{id}/read` marks one read.
  - `POST /api/apps/{app}/notifications/read-all` marks all read.
  - Nobody deletes a notification. The routes need a session, and follow the
    CSRF rule.
- **Kept for.** `Notifications:RetentionDays`, default 90. The worker deletes
  older rows once a day.
- **Site shell.** A bell in the site header shows the unread count. It opens
  the list. A row opens its page, if it has one, and marks it read.

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
  `locale`. A missing claim leaves the value null.
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
| `Notifications:RetentionDays` | `90` | How long in-app notifications are kept. |

A tenant may override `Email` in its `Tenants` block, or in the tenant
directory under `Tenancy:Source=directory`, where the password is encrypted as
the OIDC client secret is. MailKit sends the email. Docker Compose,
`scripts/dev.sh` and the integration tests run Mailpit as the mail server.
