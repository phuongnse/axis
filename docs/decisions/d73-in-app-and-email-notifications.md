# D73. In-app and email notifications

Status: **Agreed**

M14 lets a process tell people what happened, in the site and by email. The
owner agreed this on 2026-10-25. It is built in M14, except the email channel of
an escalation's `notify`, which is _(planned for M18)_. See [notifications](../reference/notifications.md). It replaces the "Axis has no
user directory" reason of the audit actor point of
[D23](d23-sign-in-through-oidc-and-the-bff-session.md): Axis keeps a profile of
each user who signs in.

- **`notify` operation:** `{ "type": "operation", "operation": "notify",
  "to", "channels", "message", "next" }`.
  - `to` lists recipients, each `{ "user": <expression> }`,
    `{ "role": <role name> }` or `{ "email": <expression> }`.
  - `channels` is `inApp`, `email` or both.
  - `message` has a `subject` and a `body`, each a text key, and `values`,
    which map placeholder names to expressions.
  - It may name a page to open, with page parameters, as navigate actions do.
  - A task escalation's `notify` has the same shape, without `next`
    ([D71](d71-task-deadlines-escalation-claim-and-reassign.md)).
- **Placeholders:** a text may hold named `{placeholders}`. The compiler checks
  that each placeholder has a value and each value a placeholder, in every
  locale. A value that reads a `sensitive` field is a compile error. Both are
  new diagnostics.
- **In-app:** the step's transaction stores the notification. A role recipient
  is checked when the notification is read, as a role task is. Each user marks
  a notification read, or all of them. Nobody deletes one. Notifications are
  kept for 90 days, a host setting.
  - The site shell shows a bell with the unread count and the list.
- **Email:** the step's transaction renders the message and stores it in an
  outbox. A `notification.email` work item sends it outside any transaction,
  with the backoff and limit of
  [D72](d72-a-delivery-limit-and-dead-letters.md), then a dead letter.
  - Each email has a stable `Message-ID`, so a receiver can drop a repeat.
    Delivery is at least once.
  - Email to a role is a compile error in M14, because Axis has no directory
    to expand a role. Roles get in-app notifications only.
- **User profiles:** each sign-in writes the user's id, display name, email
  and locale to a profile in the tenant database, from claims the tenant
  names. A `user` recipient's email and locale come from it. A user with no
  profile or no email gets no email, which is logged.
  - The profiles are not a directory. No route lists them, except the user
    picker of a reassign, which needs the `reassign` grant.
- **Language and time:** an email uses the recipient's locale when the release
  has texts for it. Dates show in the application's time zone
  ([D75](d75-the-application-time-zone.md)).
- **Settings:** SMTP is a host setting, and a tenant may override it in the
  tenant directory, with its password encrypted as the OIDC client secret is.
- **Tests:** test scenarios gain an `expectNotification` step, and email is
  stubbed in scenarios. Docker Compose and integration tests run Mailpit as the
  mail server.
- **Library:** MailKit (MIT) sends email. It adds to the stack of
  [D3](d3-technology-stack.md).
- **Service desk:** the requester gets an in-app and email notification when
  their ticket is resolved, and the lead gets the reminders of
  [D70](d70-processes-without-a-subject-scheduled-jobs-and-delete-triggers.md)
  and [D71](d71-task-deadlines-escalation-claim-and-reassign.md).
- **Effect on earlier decisions:** D23 links here where this decision changes
  it.

_Why:_ see [human work](../domain/knowledge.md#human-work). People must hear
about work that waits for them or about their own requests, or processes stall.
Staging email in the step's transaction and sending it after the commit means a
rolled back step sends nothing, and a crash after the commit still sends. A
profile kept at sign-in gives an email address and a locale without a user
directory. Checking placeholders and refusing sensitive values at compile time
keeps a message from breaking or leaking data at run time.
