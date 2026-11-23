# Notifications

This page describes how a process tells people what happened: the `notify` operation and the
placeholders of its texts, in-app notifications with their bell, emails, the user profiles that give
the addresses and locales, and the mail settings.

## Notify operation

A `notify` operation step sends one message to its recipients, in the application, by email or both.
It is a step of the [process resource](processes.md), and it looks like this:

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

- **`to`** is a non-empty list (`AXC0266` at `/steps/{i}/to`). Each recipient has exactly one of these
  (`AXC0267` at `/steps/{i}/to/{j}`):
  - `user`, an expression that gives a user id, as a task's `user` assignee;
  - `role`, a declared role name, compared exactly (`AXC0268` at `/steps/{i}/to/{j}/role` for an
    unknown one). A role whose file failed to load is not reported again;
  - `email`, an expression that gives an address, for the `email` channel only.
- **Recipient expressions.** `user` and `email` are `text` expressions with the scope of a `when`. A
  problem is reported with its expression code at `/steps/{i}/to/{j}/user` or
  `/steps/{i}/to/{j}/email`.
- **`channels`** lists `inApp`, `email` or both. Any other channel is `AXC0004`, and an empty list is `AXC0269` at `/steps/{i}/channels`. A
  channel named twice is a schema violation (`AXC0004`).
- **`kind`**. It is `info`, `warning` or `action`, and `info` when it is left out. Any other value is a schema violation (`AXC0004`) at `/steps/{i}/kind`. An `action` notification waits in Needs action until it is done, see [in-app notifications](#in-app-notifications). A task escalation's `notify` takes it too.
- **No role by email.** A `role` recipient with the `email` channel is `AXC0270` at
  `/steps/{i}/to/{j}/role`, because Axis has no directory to expand a role into addresses.
- **`message`.** `message.subject` and `message.body` are text keys, checked as a label is at
  `/steps/{i}/message/subject` and `/steps/{i}/message/body`. `values` maps each placeholder name to an
  expression with the scope of a `when`, and a problem is reported at
  `/steps/{i}/message/values/{name}`. A value may have any type, because it is formatted by its type,
  so only its syntax, names and rules are checked.
- **`attachments`** _(planned for M40)_ is optional, and it needs the `email` channel. It is a list of files that
  the email carries. An entry is `{ "field": "<file field>" }`, which attaches the file that a `file` field of the
  subject record holds, or `{ "template": "<documentTemplate>", "format": "pdf" }`, which attaches a document made
  for this email, see [document templates](document-templates.md#document-step). An entry whose field is `null`
  attaches nothing. An in-app notification carries no attachment.
- **`open`** is optional. It names a page of the application and maps its page parameters, as a
  navigate action does (`AXC0232` to `AXC0234`, at `/steps/{i}/open`). The page is a `navigate page`
  edge in the reference index, and the in-app notification opens it.
- **The step's transaction.** The step stores the notification and stages its emails in its own
  transaction, then goes to `next`, as [Running a step](process-execution.md#running-a-step) describes.
  A step that rolls back sends nothing. Its history output is `{ "next", "notificationId" }`.
- **Other places.** A process without a subject may notify too. A task escalation's `notify` has the
  same shape, without `next`, see [task deadlines](task-deadlines.md#escalations-and-deadline).
- **In Studio.** Studio's operation panel edits the step without the JSON view, see the
  [`notify` panel](process-designer.md).

## Placeholders

A text value may hold named placeholders, such as `"Ticket {number} is resolved: {resolution}"`, which
the notification fills from its `values`.

- **Names.** A name is ASCII letters and digits. `{{` and `}}` write a brace. A name and a `values` key
  match exactly, letter case included.
- **Every name has a value.** Each placeholder of a message's texts needs a key in `values`
  (`AXC0271`), and each key of `values` must appear in the subject or the body (`AXC0272`), in every
  locale that has both texts. Each mismatch is reported at `/steps/{i}/message`, naming the locale and
  the name.
- **Braces.** A lone or invalid brace is `AXC0273` at `/steps/{i}/message`. A locale with one is
  reported for that brace only.
- **No sensitive values.** A value whose expression reads a field marked `sensitive` is `AXC0282` at
  `/steps/{i}/message/values/{key}`, naming the field. It covers a field of the subject, a field reached
  through a path such as `assignee.salary`, one read inside an aggregate and one passed to a rule, so a
  notification never carries a sensitive value. Only `values` is checked, not a page parameter or a
  recipient expression, and the check uses the release being compiled.

Values are formatted for the reader when the notification is read. A built-in table gives the formats,
because the backend runs with invariant globalization and cannot load the data of a locale. A locale
whose language is `vi` uses `vi`, and every other locale uses `en`.

| Value type           | `en`                   | `vi`               |
| -------------------- | ---------------------- | ------------------ |
| `integer`, `decimal` | `1,234.5`              | `1.234,5`          |
| `date`               | `Jan 1, 2027`          | `01/01/2027`       |
| `date-time`          | `Jan 1, 2027, 1:00 AM` | `01/01/2027 01:00` |

Every digit of a number is kept, so formatting never rounds. A `date-time` shows in the time zone of
the release the notification was stored with, so `2026-12-31T18:00:00Z` in an `Asia/Ho_Chi_Minh`
application shows on 1 January 2027 at 01:00. A `date` shows as a local date with no shift. Text, enum,
boolean, id and reference values show as written. The locale is the user's profile locale, even when
the release has no texts in it. Without a profile locale, it is the locale of the texts that were
chosen.

## In-app notifications

An in-app notification is a message that a user reads in the application, under the bell of its site.
It is stored once for each recipient, read by each user on their own, and deleted after a retention
period.

- **Storage.** `axis.notifications` holds one row per notification and recipient: the application, the
  release, the recipient kind and recipient, the text keys, the raw evaluated values with the type of
  each (`text`, `integer`, `decimal`, `boolean`, `date`, `date-time`, `enum` or `reference`), the
  declared name of the page to open with its raw parameters, and the time. All the rows of one step run
  share the notification's id. A step with only the `email` channel stores no row.
- **Recipients.** A role recipient is one row for the role. A `user` expression that gives `null` or
  blank text adds no row, and two recipients of one kind and name add one row.
- **Values.** The values are stored as an event payload is, with dates as ISO text, and are formatted
  when the notification is read, because a role row reaches users with different locales. A
  notification stored before the types were kept has none, so its numbers are still formatted and its
  text shows as written.
- **Who sees one.** A user sees the notifications addressed to their user id, and those addressed to a
  role they hold that the active release declares, checked when read, as a role task is. They do not
  see a source they turned off in the application, see [notification preferences](#notification-preferences).
- **Read state.** `axis.notification_reads` holds one row per notification and user who read it.
- **Source.** Each row keeps its source. It is `process` for a `notify` step or a task escalation, `quota` for a quota notice, and `authoringRequest` for an AI request notice.
- **Kind**. Each row keeps its kind. A `notify` step or a task escalation gives the kind its step gives, see [the `notify` operation](#notify-operation). A quota notice is a `warning`. A notification stored before kinds existed reads as `info`, except a quota notice, which reads as `warning`.
- **Read is not done**. Reading a notification marks nothing done. An `info` or `warning` notification is always done. A process's `action` notification is done only when it is cleared, because Axis cannot tell whether its action was taken. An AI request's `action` notification is also done by its request's state, or by a newer notice of the same request, see [tenant notices](#tenant-notices).
- **Clearing**. A clear sets `cleared_at` on the caller's read row, and sets `read_at` too when it is missing. A cleared notification is no longer listed or counted for that caller. Other recipients see no change, and nothing is deleted before the retention period ends.
- **Kept for.** The worker deletes the notifications whose `created_at` is older than
  `Notifications:RetentionDays`, a [worker setting](process-execution.md#worker-settings) of 90 days
  by default, by its own clock. It deletes their `axis.notification_reads` rows in the same
  transaction, when the tenant becomes ready and then every 24 hours. Nobody else deletes a
  notification.
- **Speed.** The list answers within 300 ms for a user with 1,000 notifications, to the user and to a
  role they hold, half of them read, and its count reads through the notification index. This is the
  Reads objective of [performance](performance.md#objectives). `TaskInboxPerformanceTests` fails when
  the best of three calls takes 300 ms or more.

### Notification routes

The routes list a user's notifications of one application and mark them read. They need a session
(`401`), and the POSTs follow the CSRF rule.

| Route                                                               | What it does                                                                                                                                |
| ------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| `GET /api/apps/{app}/notifications`                                 | Lists the user's notifications of an application, see [notification routes](#notification-routes)                                           |
| `POST /api/apps/{app}/notifications/{id}/read`                      | Marks one notification read, see [notification routes](#notification-routes)                                                                |
| `POST /api/apps/{app}/notifications/read-all`                       | Marks every notification the user sees read, see [notification routes](#notification-routes)                                                |
| `POST /api/apps/{app}/notifications/{id}/clear` | Clears one notification for the caller, see [notification routes](#notification-routes)                                                     |
| `POST /api/apps/{app}/notifications/clear-all` | Clears the notifications the user sees now, except the `action` notifications not yet done, see [notification routes](#notification-routes) |

- **List.** `GET /api/apps/{app}/notifications?page=&pageSize=&unread=` lists the user's
  notifications, newest first. It answers `items`, `page`, `pageSize`, `totalCount` and `unreadCount`.
  With `unread=true` the items and `totalCount` are the unread ones only, and `unreadCount` always
  counts every unread one. A notification sent to the user and to a role the user holds is listed once.
  A bad `page`, `pageSize` or `unread` (`true` or `false`) is a `400` that lists every bad parameter.
- **Needs action**. `needsAction=true` lists only the `action` notifications that are not yet done. `totalCount` follows that filter. `needsAction` takes `true` or `false`, and any other value is a `400`, as `unread` is.
- **Item.** An item has `id`, `releaseId`, `subject`, `body`, `open`, `read` and `createdAt`. `open` is
  `{ "page", "parameters" }` with the stored raw parameters, or null when the notification opens no
  page.
- **Item fields**. An item also has `source`, the row's source (`process`, `quota` or `authoringRequest`), `kind`, the row's kind, and `done`. `done` is true once the notification is done, see [in-app notifications](#in-app-notifications).
- **Texts.** Texts come from the notification's release, in the user's profile locale when that release
  has texts for it, otherwise its first locale in ordinal order, as for [email](#email). Each `{name}`
  is filled with the stored value formatted as [placeholders](#placeholders) says: a number, a date and
  a date-time in the user's locale, a string as it is, a boolean as JSON writes it and null as empty
  text. A text key the locale does not have shows as the key.
- **Mark read.** `POST /api/apps/{app}/notifications/{id}/read` marks one read, and
  `POST /api/apps/{app}/notifications/read-all` marks every notification the user sees now read. Both
  write read rows for the caller only, so another recipient's unread count does not change. Both answer
  `204` with no body, and so does marking a notification that is already read. A notification the user
  does not see, or that does not exist, is a `404`.
- **Clear**. Both routes answer `204` with no body. `clear-all` clears every notification the user sees now, except the `action` notifications not yet done. An id the user does not see, or that does not exist, is a `404`.

### Tenant notices

A tenant notice is an in-app notification, in `axis.notifications`, that Axis stores for the
tenant's administrators, or for one person. It has no application and no release, uses platform text
keys and the recipient kind `platformRole` or `platformUser`, and no application lists it.

| Route                                                    | What it does                                                                                                                 |
| -------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `GET /api/notifications`                                 | Lists the tenant notices of the user, see [tenant notices](#tenant-notices)                                                  |
| `POST /api/notifications/{id}/read`                      | Marks one tenant notice read, see [tenant notices](#tenant-notices)                                                          |
| `POST /api/notifications/read-all`                       | Marks every tenant notice the user sees read, see [tenant notices](#tenant-notices)                                          |
| `POST /api/notifications/{id}/clear` | Clears one tenant notice for the user, see [tenant notices](#tenant-notices)                                                 |
| `POST /api/notifications/clear-all` | Clears the tenant notices the user sees now, except the `action` notices not yet done, see [tenant notices](#tenant-notices) |

- **Routes.** `GET /api/notifications?page=&pageSize=&unread=&needsAction=`,
  `POST /api/notifications/{id}/read`, `POST /api/notifications/read-all`,
  `POST /api/notifications/{id}/clear` and `POST /api/notifications/clear-all` list tenant notices,
  mark them read and clear them. They answer in the
  same shape as the [application routes](#notification-routes), with `releaseId` always null and
  `open` null except for an AI request notice. They are served on the tenant's platform host only, need a session (`401`), and the POSTs
  follow the CSRF rule.
- **Who sees one.** A user sees a notice for the `administrator` kind only while they hold the role
  that `Users:AdministratorRole` names. Anyone else sees only the notices sent to them, see To one
  person. Without such a notice they get an empty list with zero counts and a `404` when marking a
  notice read or clearing it, and read-all and clear-all write nothing for them.
- **To one person**. A tenant notice may go to one person. Its recipient kind is `platformUser`, and its recipient is the person's user id. Only that user sees it, and only on the platform host. An administrator does not see another person's notice.
- **Texts.** The texts are platform texts in the user's profile locale, matched by the whole tag
  ignoring letter case, with `en` as the fallback, so `vi-VN` shows `en`. Numbers are formatted in the
  profile locale, and a `date-time` shows in UTC, because a notice has no release time zone.
- **AI request notices** _(planned for M35)_. The author of an [authoring request](authoring-requests.md) gets four notices. Each has the source `authoringRequest`, and each is stored in the same commit as the move of the request that it names:
  - **Plan ready** on a move to `awaitingApproval`. The kind is `action`. The text key `notices.authoringPlanReady` gives the subject "Plan ready to approve" and the body "Read the plan for your request in {application} and approve it or ask for another."
  - **Draft ready** on a move to `ready` when the request did not ask for auto-accept, or after the agent's accept is `refused`. The kind is `action`. The text key `notices.authoringDraftReady` gives "Draft ready to accept" and "The draft for your request in {application} passed its checks and review. Accept or drop it."
  - **Draft accepted** when the agent's accept succeeds. The kind is `info`. The text key `notices.authoringAccepted` gives "Draft accepted" and "AI accepted the draft for your request in {application}. It goes live when someone publishes it."
  - **Request stopped** on a move to `failed`. The kind is `warning`. The text key `notices.authoringFailed` gives "Request stopped" and "Your request in {application} stopped before it was done. Open it to see why."

  The values are `application`, the application's name, and `request`. The states are in [authoring requests](authoring-requests.md#states).
- **Preferences and email** _(planned for M43)_. AI request notices follow the person's `authoringRequest` preference. With `application` off they are not in the bell or its counts. With `email` on, the commit that stores the notice also stages one email in `axis.email_outbox` to the person's profile address. The email has the notice's subject and body in platform texts of the profile locale, as Texts above says, and a link to the request in Studio on the tenant's platform host. It is sent as [email](#email) says, now or in the digest. A person with no profile email gets none.
- **Read and done**. A notice is read when its person opens it from the bell or marks it read. An `action` notice is done when its request leaves the state it waits on, whether it is read or not. Plan ready waits on `awaitingApproval`, and draft ready waits on `ready`. It is also done once a newer notice of the same request goes to the same person, because a request can come back to that state. A done notice is not under `needsAction=true`, and `clear-all` clears it.
- **Open**. An AI request notice opens its request in Studio, with `{ "page": "studio", "parameters": { "app", "view", "request" } }`. The bell opens `/studio?app=<name>&view=requests&request=<id>`, see [AI authoring in Studio](ai-authoring-in-studio.md#the-request-panel).

### The bell

A bell in the header shows the signed-in user's unread count and opens their notifications.

- **Application sites.** On an application site the bell shows the unread count for that application,
  and no count when nothing is unread. `GET /api/sites/{path}` gives the address of the notification
  routes as `notificationsPath`.
- **Platform site.** The platform site shows the bell when `GET /api/me` answers `users: true`, and it
  reads the [tenant notices](#tenant-notices) from `/api/notifications`. Anyone else, and a visitor
  who is not signed in, gets no bell.
- **Everyone on the platform site** _(planned for M35)_. Every signed-in person on the platform site has the bell. Each person sees the notifications addressed to them, and an administrator also sees the tenant notices.
- **Name.** The bell is a button with the accessible name "Notifications, 2 unread", or "Notifications"
  with no count. A polite live region announces a change of the count.
- **Icon button** _(planned for M35)_. The bell is an icon button, and its unread count is drawn by `CountBadge`, the [shared widget](shared-widgets.md#which-widget) for a count. Its accessible name is the one given in Name above.
- **Panel.** The bell opens a panel that lists the notifications newest first, 10 to a page, each with
  its subject, body and time. Unread rows are marked, and a Mark all read button sits at the top.
- **Popover** _(planned for M35)_. The bell opens a popover list of the notifications, newest first. Its filters are All, Unread and Needs action, and Needs action lists the `action` notifications not yet done. Two buttons sit on it: Mark all read and Clear all.
- **Rows.** A row opens its page, if it has one, with its parameters and marks the notification read.
  A row without a page, such as a quota notice, only marks it read.
- **Row details** _(planned for M35)_. Each row shows its subject, its body and how long ago it arrived, such as "5 minutes ago", in the person's locale. An unread row is marked, and each row has a Clear button. A screen reader names the row by its subject. Its description reads "Unread" and "Needs action" when they apply, then the body and the full date and time.
- **Reloads.** The count reloads when the site loads, on every page navigation, when the panel opens
  and after any mark read. There is no timer. A failure to read the list hides the bell and never
  shows the sign-in prompt.

- **Live reloads.** _(planned for M43)_ The bell also reloads when a `notifications` event arrives, see [live pages](frontend.md#live-pages).

## Task notices

_(planned for M43)_

A person hears in the application and by email when a task is given to them, moved to them by an
[absence](user-administration.md#absences), or reassigned to them.

- **Claim-mode tasks.** A role task that waits to be claimed notifies the role in the application only.
- **Chosen holders.** A task that [sharing role tasks](task-api.md#sharing-role-tasks) gives to a holder, by
  `fewestOpen` or `turn`, notifies that holder.
- **Email links.** The email lists one link for each outcome. Each link opens the
  [task page](task-inbox-widget.md#deciding-from-an-email) with that outcome chosen.

## Notification preferences

A person chooses on the account page's [Notifications tab](platform-pages.md#notifications). They choose how each kind of notice reaches them, and whether its emails come together in a daily digest. A choice only narrows the channels that a notice would use. It never adds one.

| Route | What it does |
| --- | --- |
| `GET /api/me/notification-preferences` | Reads the signed-in person's preferences |
| `PUT /api/me/notification-preferences` | Replaces the signed-in person's preferences |

- **Sources.** A source is a kind of notice. `task` is the [task notices](#task-notices), `mention` is the [mentions](comments.md#mentions), `process` is the message of a `notify` step, see [the notify operation](#notify-operation), and `authoringRequest` is the notices of the person's own AI requests, see [tenant notices](#tenant-notices).
- **Body.** `PUT` takes the four sources, `digestHour` and `timeZone`. Each source has `channels`, with `application` and `email` set to `true` or `false`, and `digest`, set to `true` or `false`. For example:

  ```json
  {
    "sources": {
      "task": { "channels": { "application": true, "email": true }, "digest": true },
      "mention": { "channels": { "application": true, "email": false }, "digest": false },
      "process": { "channels": { "application": true, "email": true }, "digest": false },
      "authoringRequest": { "channels": { "application": true, "email": false }, "digest": false }
    },
    "digestHour": 8,
    "timeZone": "Europe/Berlin"
  }
  ```

- **Defaults.** With no saved choice, each source has both channels and no digest, `digestHour` is 8 and `timeZone` is `UTC`.
- **Own preferences only.** The routes read and write the signed-in person's preferences. No route names another person.
- **Digest hour and time zone.** `digestHour` is a whole hour from 0 to 23. `timeZone` is an IANA time zone name. The account page sends the browser's time zone, because a tenant has no time zone of its own, as [absences](user-administration.md#absences) do.
- **Refusals.** A bad value is a `400` keyed by its path, such as `/digestHour` for an hour outside 0 to 23, or `/timeZone` for a name that is not an IANA time zone.
- **In-app off.** A source with the `application` channel off does not show in the person's bell list or its counts, for the notifications sent to them and to a role they hold. Other people still see them. A notification they do not see is not there for marking read either: marking it alone is a `404`, and read-all skips it. It shows again when they turn the channel on, until the retention period deletes it.
- **Email.** A `notify` step or a task escalation stages no email for a `user` recipient whose `process` source has `email` off. An `email` recipient, a bare address, is still sent. Otherwise an email is sent as [email](#email) says.
- **Digest.** A `notify` step or a task escalation does not send right away to a `user` recipient whose `process` source has `email` and `digest` on. Its email waits in `axis.email_outbox`, `pending`, with the person's user id in `digest_user_id`. The person has one queued `notification.email` item for the digest, due at the next `digestHour` in `timeZone`, and it is queued only when no row of theirs waits yet. At that hour the worker sends one digest email to the person, which lists the subject and link of each waiting email, oldest first, and then marks all of them `sent`. An hour that does not exist on a daylight saving day sends at the next valid local time, and an hour that happens twice sends once, at its first instant, as [schedule triggers](process-events.md#schedules) do. The digest's subject and opening line are platform texts in the profile locale, with `en` as the fallback. Its `Message-ID` is that of the newest email it lists, so a repeat after a crash carries the same one. One digest takes one slot of the tenant's `emailsPerDay` quota. A failed digest is retried and becomes a dead letter as any email does, and while it is one, the person's later digest emails wait. An operator who redelivers it sends the digest again. An operator who discards it gives that digest up: the waiting rows stay `pending`, the failure count is cleared and the person's next item is queued at their next `digestHour`, so their waiting emails go out in that digest. An email staged while a digest is sent gets the next item. A change of `digestHour`, `timeZone` or `digest` applies from the next digest, since the queued item keeps its time. A bare address and a person without the digest still get their email right away.
- **Notices to administrators.** A tenant notice to the administrators always shows in the application, whatever the preferences say, and is never emailed. See [tenant notices](#tenant-notices).
- **Storage.** `axis.notification_preferences` holds one row for each person who has saved a choice. `user_id` is the primary key, `sources` (`jsonb`) holds the four sources in the shape of the body, and `digest_hour`, `time_zone` and `updated_at` hold the rest. A new source needs no migration.

## Email

A `notify` step or a task escalation with the `email` channel stages one email for each address in its
own transaction, and the worker sends it later over SMTP, at least once.

- **Outbox.** For each email recipient, the step's transaction renders the subject and body from the
  pinned release's texts in the recipient's locale, and stores them in `axis.email_outbox` with the
  address and a stable `Message-ID`. It queues one `notification.email` work item per row, except for a row that waits for a person's [digest](#notification-preferences), which shares the person's one item. The
  `Message-ID` is `<{row id}@{host}>`, with the tenant's first host, or `<{row id}@axis.invalid>` when
  the tenant has no host. A step run stages at most one email per address, ignoring letter case, with
  the locale of the first recipient that gave it. The row also keeps the raw placeholder values of the
  step run in `values`, so a test scenario can check them. A role recipient gets no email.
- **HTML.** _(planned for M40)_ A message may have `message.htmlBody`, a text key whose text is an HTML body,
  beside the plain `body`. The worker then sends one `multipart/alternative` email that holds both, so a reader
  that shows no HTML still has the text. An email with no `htmlBody` stays plain text.
- **Placeholders in HTML.** _(planned for M40)_ The value of a placeholder is HTML-encoded where it is written
  into the HTML body, so a value never adds a tag, a script or a link. The template's own HTML is the author's.
- **Cleaning.** _(planned for M40)_ The step cleans the HTML body with an allow list of tags, attributes and URL
  schemes before it stores it in `axis.email_outbox`. Script, event attributes, forms and `javascript:` links are
  dropped, so what the worker sends is what was cleaned.
- **Logo.** _(planned for M40)_ When the site that the email's link names has a logo, the HTML body shows it as
  an image that the email carries inline, so it shows without a request to Axis's server.
- **Attachments.** _(planned for M40)_ The worker reads each attachment's bytes when it sends, from the file's
  storage, or asks the [document runner](document-templates.md#the-document-runner) for a document that the step
  named. The outbox row holds the file ids and the template names, never the bytes.
- **Size limit.** _(planned for M40)_ An email with its attachments may be at most `Email:MaxSize`. One over it
  fails the send like a failed send, and becomes a dead letter after `Worker:DeliveryAttempts`, with a message
  that names the limit and no file name. The step does not know the size of a document until it is made.
- **Escalations.** A task escalation stages its emails in its own transaction, so one that rolls back,
  such as one whose `notify` expression fails, stages none. Its rows name the task step in `step`, and
  share the `notificationId` of the escalation's history row. The sending, the failure count and the
  dead letter are the same as for a `notify` step, and a dead letter names the task's process
  instance.
- **Sending.** The worker sends each email as plain text over SMTP, outside any transaction, with the
  stored subject, body and `Message-ID`, using the [mail settings](#mail-settings) of the tenant. One
  transaction then marks the row `sent`, deletes the item and checks the claim. A row that is already
  `sent` has its item deleted and nothing is sent. The SMTP client gives up after half of the item's
  lease, so a send ends before another worker can claim the item. With no `Email:Smtp:Host`, the item
  is made due again after one lease and no failure is counted.
- **Failures.** A failed send is counted in `axis.delivery_failures` with the exception type only, and
  the item is tried again with the backoff of [dead letters](process-events.md#dead-letters). After
  `Worker:DeliveryAttempts` failures the email becomes an `open` dead letter of kind
  `notification.email`, the row stays `pending` and nothing more is sent until an operator redelivers
  it, which queues the same row again.
- **Quota.** In a cell the worker takes a slot of the tenant's
  [emails a day](tenant-quotas.md#emails-a-day) just before the send. With no slot left the row stays
  `pending` and the item is due at the next 00:00 UTC.
- **At least once.** A crash after the send and before the mark sends again. The same `Message-ID`
  lets a receiver drop the repeat.
- **Locale.** The recipient's profile locale when the release has texts for it, otherwise the first
  locale of the release in ordinal order.
- **Body.** Plain text, with the page link as an absolute URL on the tenant's first host when `open`
  is set. The link is on its own line after a blank line. It is `https://{host}/{site}/{page}`, the
  address form of the routing of [frontend.md](frontend.md), with the page name in lower case and the
  page parameters as query values. The site is the first site whose navigation lists the page,
  otherwise the application's first site. An email has no link when the tenant has no host or the
  application has no site, so in a test scenario, where the tenant has no host, emails have no link.
- **Logs.** A `user` recipient without a profile or an email gets no email. The step logs it at
  Information level with the step and the user id, never the address. An escalation logs it with the
  task and the escalation index. A `user` recipient whose `process` email is off gets no email and no
  log, see [notification preferences](#notification-preferences).
- **Scenarios.** In a test scenario, `advanceTime` runs the `notification.email` items with no mail
  host, so each stays queued and nothing is sent. Each scenario user has a profile with the address
  `<user>@scenario.invalid`, so a `user` recipient gets an email. An `expectNotification` step checks
  the recipient, the channel and the values of a notification. It reads in-app notifications from
  `axis.notifications` and emails from `axis.email_outbox`, pending or not. See
  [test scenarios](test-scenarios.md).

## User profiles

A user profile gives Axis a user's display name, email address and locale, which notifications and a
few pickers need. Each sign-in writes the user's profile to `axis.user_profiles` in the tenant
database: the user id, the display name, the email, the locale, the first sign-in and the last
sign-in. A later sign-in updates it.

- **Claims.** The email and locale come from the claims the tenant names in `Oidc:Claims:Email` and
  `Oidc:Claims:Locale`, by default `email` and `locale`. A missing claim leaves the value null. Sign-in
  asks the identity provider for the `email` scope, so providers other than Keycloak put the email in
  the ID token.
- **Failures.** A failure to write the profile does not refuse the sign-in. The server logs a warning
  that does not include the email.
- **Not a directory.** The profiles serve email addresses, locales, the user picker of a
  [reassign](task-deadlines.md#reassign), the search route of a
  [`userId` field](resource-files.md#entity-field-types-and-constraints) and the actor names of the
  history views, and no other route lists them.
- **Roles.** _(planned for M43)_ Each sign-in also keeps the application roles it carried, for
  [sharing role tasks](task-api.md#sharing-role-tasks).
- **Delegates.** _(planned for M43)_ The profiles also serve the delegate picker of an
  [absence](user-administration.md#absences).

## Mail settings

The mail settings tell the worker which SMTP server sends a tenant's emails and as whom. The host's
settings apply to every tenant that does not override them.

| Setting               | Default | Meaning                                                                                                                                                                                                                                                                                                                                                                                       |
| --------------------- | ------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Email:Smtp:Host`     | none    | The SMTP server. Without it, no email is sent, the items stay queued and the worker logs a warning once per tenant.                                                                                                                                                                                                                                                                         |
| `Email:Smtp:Port`     | `587`   | The SMTP port. A value that is not a whole number from 1 to 65535 stops the worker at start.                                                                                                                                                                                                                                                                                                 |
| `Email:Smtp:UseTls`   | `true`  | Use STARTTLS, or TLS on port 465. A value other than `true` or `false` stops the worker at start. Only a worker that runs in Development sends email with it `false`. Elsewhere the worker sends nothing for that tenant, the items stay queued, no failure is counted, and the worker logs a warning once per tenant that names the setting. `tenant email set --tls false` is refused too, see [tenant setting commands](tenant-setting-commands.md#mail-settings). |
| `Email:Smtp:UserName` | none    | The SMTP user.                                                                                                                                                                                                                                                                                                                                                                                |
| `Email:Smtp:Password` | none    | The SMTP password, from the environment.                                                                                                                                                                                                                                                                                                                                                      |
| `Email:From`          | none    | The sender address. Required when a host is set.                                                                                                                                                                                                                                                                                                                                              |

How long in-app notifications are kept is the `Notifications:RetentionDays`
[worker setting](process-execution.md#worker-settings).

A tenant may override `Email` in its `Tenants` block. The block replaces the host's settings as a
whole, and keys are never merged one by one, so a tenant that sets only its own host never sends the
host's user name and password to that server. A host with no `From` is refused at start, naming the
setting. Under `Tenancy:Source=directory` a tenant overrides `Email` in the tenant directory, where the
password is encrypted as the OIDC client secret is. The
[`tenant email set`](tenant-setting-commands.md#mail-settings) command sets it, and a tenant without it
uses the host's settings.

MailKit sends the email. The integration tests run Mailpit as the mail server, and so do Docker
Compose and `scripts/dev.sh`, see [development](../development.md).
