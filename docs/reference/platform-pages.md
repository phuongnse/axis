# Platform pages

This page describes the pages that the platform site serves beside Studio. Each is a platform route in
the shell, see [frontend](frontend.md) for the routes and [shell](shell.md) for the frame around them.
Each sits before the site route, so the first segment of its address is a reserved site path.

## Account page

The platform route `/account` is the person's account page, where they change their password, manage
their authenticators and end their sessions. It is the only self-service page: `tenant create` turns off
Keycloak's account console client and removes `manage-account` from the realm's default roles, see
[keeping the realm in step](identity-provider.md#keeping-the-realm-in-step).

### Tabs

The page is titled "Your account" ("Tài khoản của bạn"), from `shell.account.title`, and the account
menu's entry that opens it reads the same. It has two tabs:

- **Security** (`shell.account.tabs.security`). Holds Change password, Authenticators and Sessions, as the
  sections below describe.
- **Notifications** (`shell.account.tabs.notifications`). Holds [Notifications](#notifications).

### Password and authenticators

The page loads `GET /api/me/authenticators`. While that call is loading or has failed, the page shows
neither action, and a failure shows an error message. When `account` is `false`, the person is a user
of a brokered provider, and the page shows the sessions only.

- **Change password and Authenticators.** Above the sessions, two actions start the identity
  provider's own steps, as application-initiated actions through a sign-in with `kc_action`. Change
  password starts `UPDATE_PASSWORD`. Authenticators lists the person's authenticators with Add, which
  starts `CONFIGURE_TOTP`, and for each authenticator a Remove, which is the trash icon named by its tooltip and
  starts `delete_credential:<id>`. Their pages are
  Axis pages, see [theme](identity-provider.md#theme), and Cancel on them returns to the account page
  with nothing changed.
- **The last authenticator stays.** Remove is off on a person's only authenticator, because every
  account needs one, see [accounts](user-administration.md#accounts).
- **Filter.** The Authenticators table has Filter on Name and Added (the day), kept in the address as
  `authenticators.filter`. It narrows the rows the page already holds and sends no request.

### Sessions

The page lists the person's live sessions on the tenant's own hosts from `GET /api/me/sessions`, newest
sign-in first. A signed-out visit shows the sign-in prompt, because the list call is not exempt.

- **Rows.** A row shows Signed in and Last active, in the current locale. A This session `StatusTag` in `neutral` sits in
  the row's Signed in cell and marks the session in use now. The row has no End action, because the person ends it
  with Sign out, which also ends the identity provider's session.
- **Filter.** The sessions table has Filter on Signed in and Last active, each by the day in the person's
  own time zone. The filter is kept in the address as `sessions.filter` and narrows the rows the page
  already holds, with no new request.
- **Ending sessions.** Every other row has End as a row action in the destructive tone (`DELETE /api/me/sessions/{id}`). End all
  other sessions (`DELETE /api/me/sessions`) is disabled when no other session exists. After a `204` or
  a `403`, the page loads the list again, and any other answer shows an error message.
- **Sign in again.** When `endAllowed` is `false`, both kinds of button are disabled, and a message
  with a Sign in again button shows. The button goes to
  `/api/auth/sign-in?returnUrl=%2Faccount&reauthenticate=true`, so the person signs in again and
  returns to `/account`. A `403` means the 5-minute window has just closed, so the reloaded list shows
  this state.
- **Texts.** Its texts are `shell.account.sessions.*` for the section.

### Absence

_(planned for M43)_

The Absence section shows the person's absence from `GET /api/me/absence`, with a From and an Until date
field and a Delegate user picker. The picker is the one that [reassign](task-deadlines.md#reassign) uses.
Save sends the dates and the delegate with `PUT`, and End absence sends `DELETE`. Both buttons are
`AxisButton`s, and the fields are `AxisField`s. A refused request shows its errors in place, with
`placeProblemErrors`. The dates follow [absences](user-administration.md#absences). Its texts are
`shell.account.absence.*`.

### Notifications

The Notifications tab shows the person's [notification preferences](notifications.md#notification-preferences)
from `GET /api/me/notification-preferences`, and saves each change as the person makes it.

- **Loading.** While the preferences load, the tab shows no table. When they fail to load, it shows an error
  `AxisAlert` and no table, as the Security tab does for its own loads.
- **One table.** An `AxisTable` has a row for each source: Tasks, Mentions, Process messages and AI requests,
  for `task`, `mention`, `process` and `authoringRequest`. Each row shows its name and a one-line description
  in `textMuted`.
- **In the application.** The first column is a switch, an `AxisCheckboxField`, for the source's `application`
  channel.
- **Email.** The second column is one choice of Off, Right away or In the daily digest. Off sets `email` to
  `false`. Right away sets `email` to `true` and `digest` to `false`. In the daily digest sets both `email` and
  `digest` to `true`.
- **Accessible names.** Each control is named by its row and column, such as "Mentions: In the application", through
  a label that is visually hidden, see [fields](actions-and-controls.md#fields).
- **Notices to administrators.** A last row shows while `GET /api/me` answers `users: true`. It is locked on in
  the application, with "Always on", and has no email choice. See
  [notification preferences](notifications.md#notification-preferences).
- **Saved at once.** There is no Save button, because each choice stands on its own, and a switch takes effect
  as it is set. A change sends the whole preferences with `PUT` at once.
- **Busy.** The changed control is busy while the save runs, and the other controls are disabled until it ends,
  because every save sends the whole set.
- **Saved.** A `204` is announced to assistive technology as "Saved.", through `Announcement`.
- **Refused.** Any other answer, or a failed request, puts the last value back. It shows "Your change could not
  be saved, so it was put back." in an `AxisAlert`.
- **Daily digest at.** Under the table, "Daily digest at" chooses a whole hour of the day. It is shown on the
  locale's clock, "8:00 AM" in `en` and "08:00" in `vi`, see [shown values](frontend.md#shown-values). It is saved
  at once like the rest, and shows only while at least one source is In the daily digest.
- **Time zone.** Every save sends the browser's time zone as `timeZone`. The help names it in the format of
  `timeZoneText`, such as "(UTC+07:00) Asia/Ho Chi Minh", as Settings' time zone select does, see
  [controls that know what they hold](studio-resource-form.md#controls-that-know-what-they-hold).

Its texts are `shell.account.notifications.*`.

## Users page

The platform route `/users` is the users page, where an administrator manages the tenant's users and
their roles. Who may open it, the rules of each command, its routes and its audit records are in
[user administration](user-administration.md#the-users-page). The page shows names, never translated
labels, and never shows a field marked `sensitive` or record data. Its texts are `shell.users.*`.

The page has two tabs, and People is open by default. People holds [the list](#the-list) and the
[Export data and usage](#export-data-and-usage) (which includes Hosts) and [Model key](#model-key) sections. The [Connected apps](#connected-apps)
tab shows only to an administrator of a tenant that has the setting.

### The list

The page lists the tenant's users from `GET /api/users`, 20 to a page. The server searches, filters,
sorts and pages them, see [the list route](user-administration.md#read-routes).

- **Columns.** Each row has the display name, drawn by `PersonName`: a link to [the user's page](#a-users-roles), followed by "(you)" on the administrator's own row, the email, how the person signs in (the provider's alias
  as the [list route](user-administration.md#read-routes) sends it, or "Account"), the status as a
  `StatusTag` (`invited` is `attention`, `active` is `neutral` and `disabled` is `muted`) and the last sign-in. A header sorts by its column. Columns, in the
  toolbar, chooses which columns show.
- **Search.** The table's own Search box, in the toolbar, looks in the name and the email. A search starts
  on Enter or on the search button and goes back to page 1.
- **Filter.** Filter, in the toolbar, filters by any column: the text of the name, the email and the
  sign-in, the status (`invited`, `active` or `disabled`) and the day of the last sign-in. An account has no
  provider, so the filter "is empty" on Sign-in finds the accounts.
- **Refresh.** Refresh, in the toolbar, loads the list again.
- **Paging.** The pager says "1–20 of N" and has a page size picker.
- **The address.** The view lives in the address as `search`, `filter`, `sort`, `page`, `pageSize` and
  `columns`, so a reload or the Back button keeps it. See [tables](actions-and-controls.md#tables).
- **Refusals.** A `403` shows that the page is for administrators. A `404`, which a preview tenant and
  a tenant without a users client answer, shows the not-found page. Any other failure shows an error
  message.

### Inviting and resending

- **Invite.** The "Invite" button next to the search box opens a form with the email, the display name
  and optional roles. The email and the display name are marked required. The roles sit in a "Roles" group
  with its help, in one section for each active application and one for the
  platform, as on [a user's roles](#a-users-roles). The form gets them from the
  [roles route](user-administration.md#read-routes), called with the signed-in administrator's own id,
  and uses only `applications` and `platformRoles`. When they cannot be loaded, the form says so and
  still invites without roles.
- **Confirming an invitation.** Submitting the form asks "Invite this person?" and names the email, the
  name and the roles. After the confirmation, the page calls the invite route and loads the list again,
  so the new row shows as `invited`. It shows a `403` as "Sign in again to give a platform role." and a
  `409` as "An account already uses this email." Any other failure shows "The invitation could not be
  sent." and loads the list again, because an invitation whose email failed leaves the account as
  `invited`.
- **Resend invitation.** A row that is `invited` and is an account, not a brokered user, has "Resend
  invitation" in its "…" row-actions menu. It asks for confirmation, names the person and the email, calls the resend route,
  shows "The invitation was sent again." and loads the list again, because the resend gives the person
  a new id. It shows a `403` as "This page is for administrators." and a `409` as "This person has a
  password or signs in through a provider, so they cannot get a new invitation."

### Disabling, enabling and resetting an authenticator

- **Which commands.** A row's commands are entries of its "…" row-actions menu when it has two or more. A row with one
  command, such as a person who signs in through a provider, shows it as a button. A row that is
  `active` or `invited` has "Disable", and a row that is `disabled` has "Enable" instead. A row for an
  account, not a brokered user, has "Reset authenticator". Disable also shows on the administrator's own
  row, where the server answers `409`.
- **Confirmation.** Each command asks for confirmation, names the person and the email, calls its route
  and loads the list again. The Disable and Reset confirmations add a line on the effect: the person's
  sessions end, and they cannot sign in until they are enabled, or their next sign-in asks them to add
  an authenticator again.
- **Answers.** A success shows "The person was disabled.", "The person was enabled." or "The
  authenticator was reset." The page shows a `403` on disable as "Sign in again to disable a person."
  and a `409` as "An administrator cannot disable themselves." Any other failure of the three commands,
  including a `403` or a `409` on enable and reset, shows "The change was not made." and loads the list
  again.

### A user's roles

A person's roles, from `GET /api/users/{id}/roles`, show in one section for each active application and
one for the platform, where the administrator gives and removes them. Their texts are
`shell.users.roles.*`.

- **A page of its own.** The platform route `/users/{id}` is the user's page, as
  [opening things](actions-and-controls.md#opening-things) says, and holds these sections. Its title is the
  display name, with User as its line, and its first part is the email as a fact, or "Not set" when the person has none. It loads the name, the email and
  the roles from the roles route, so a reload or a shared link shows it. A `403` shows that the page is for
  administrators, and a `404` shows the not-found page.
- **Applications.** The title of an application's section is plain text. A reader who may use Studio has an Open in
  Studio button on the section's heading, through `OpenInStudioButton`, which opens that application in Studio. The
  platform's section, and the section of an application that is not active, have no button.
- **Tags.** Each role shows the "Given" `StatusTag` in `neutral` when the person has it. A given role that is not
  declared any more has a "Not declared" `StatusTag` in `attention`, and its section lists it even when its
  application is not active.
- **Give and Remove.** A Give or Remove button asks for confirmation, names the role, the place and the
  person, then calls the give-role or remove-role route and loads the roles again. A `403` shows as
  "Sign in again to change a platform role." and a `409` as "An administrator cannot remove their own
  administrator role."
- **What shows.** The roles that an administrator gave, not the platform roles that the provider's
  sign-in mapping gives.

### Export data and usage

In a tenant of a cell, the People tab has three more sections for an administrator:

- **Export data.** Described below.
- **Usage.** The tenant's quotas and its usage of the last 30 days, see [usage](tenant-quotas.md#usage).
- **Hosts.** The hosts that the tenant asked for, and a way to add one, see
  [hosts by a tenant administrator](tenant-lifecycle.md#hosts-by-a-tenant-administrator).

The "Export data" section is a component of its own below the list. It loads `GET /api/me` and renders nothing unless
`tenantExports` is true, see [the current user](security.md#roles-and-the-current-user). Its texts are `shell.users.export.*`.

- **Warning.** The section says that the file holds every value in clear, sensitive ones included, and must be kept
  safe, see [exports by a tenant administrator](tenant-lifecycle.md#exports-by-a-tenant-administrator).
- **Start.** The "Start export…" button opens a confirmation titled "Start the export?" that repeats the warning. Only
  the confirmation's "Start" button sends `POST /api/tenant/exports`. A failed start shows "The export could not be
  started."
- **Following.** While the export is `queued` or `running`, the section says so and reads the export every 2 seconds.
  It leaves the page free to close. A `failed` export shows "The export failed. Start it again." with the Start button.
- **Download.** A `succeeded` export shows when the file expires and a "Download ZIP" button. The button requests the
  export with `Accept: application/zip` and saves the file under the name of `Content-Disposition`. A failed download
  shows "The file could not be downloaded."
- **Local storage.** The section keeps the id of the last export under `axis.tenantExport` in the browser's local
  storage, so a reload shows the same export again. Local storage belongs to each host, so another host of the tenant
  does not show it. A `404` or `410` on reading forgets the id and offers Start again. A `404` or `410` on the
  download forgets it too and shows "The file was deleted after 7 days. Start a new export."

The "Usage" section is a component of its own, below "Export data". It loads `GET /api/tenant/usage` and renders nothing
unless the route answers `200`. That keeps it hidden outside a cell and from a user who is not an administrator. Its texts
are `shell.users.usage.*`.

- **Quotas.** Each quota is a row with its name, a bar and "{used} of {limit}". A quota that has reached its limit also
  shows an "At limit" `StatusTag` in `attention`, so the state is not shown by colour alone. A quota with no limit shows "No limit", with its
  use on a line of its own when the server counts it. A quota whose use the server does not count shows its limit only, with no bar.
- **Days.** A table lists the tenant's rows of the last 30 days, newest first: the database and file sizes, users,
  applications, emails sent, connector calls and work items run. A count the server does not keep shows the em dash. The cell console's Tenant view shows the same table.
- **The plan and the subscription** _(planned for M34)_. The section also shows the plan, the state, the next date and a link to the operator's billing page, see [what a tenant administrator sees](subscriptions.md#what-a-tenant-administrator-sees).

The "Hosts" section is a component of its own, below "Usage". It loads `GET /api/tenant/hosts` and renders nothing unless
the route answers `200`. That keeps it hidden outside a cell, in a preview tenant and from a user who is not an
administrator. Its texts are `shell.users.hosts.*`, and none of them names the tenant, the cell or the customer.

- **List.** A table lists each host that the tenant asked for, sorted by host, with its state: Pending with the date the
  request expires, Verified or Expired. An empty list says that no host has been added yet. The cell console's Tenant
  view draws the same table as `HostRequestTable`, see [which widget](shared-widgets.md#which-widget).
- **Add.** The "Add a host…" button, the section's main action, opens the `HostRequestDialog` that the cell console also
  uses. Its "Add" button sends `POST /api/tenant/hosts` with `{ host }`. A click with the host empty shows what to
  enter under the field and sends nothing. The dialog then shows the TXT and CNAME records to add at the DNS provider and
  when the request expires, with a "Close" button.
- **A refusal.** The dialog stays open with a message in the person's language, chosen by the status: `400` is a bad
  host name, `409` is a host in use and any other refusal, such as a read-only tenant, says that the host could not be
  added. The page never shows the server's English title. The button also shows in a read-only tenant, where the
  read-only banner explains the refusal.
- **After adding.** The section reads `GET /api/tenant/hosts` again, so the new host shows as pending.

### Model key

Where `Ai:AllowTenantKeys` is on, the People tab has a "Model key" section below Hosts, where an administrator sets,
changes and removes the tenant's own model key, see [the tenant's model key](user-administration.md#the-tenants-model-key).
The section is a component of its own. It loads `GET /api/tenant/model-key`. While it loads, the section shows its heading
and says so. A `403` or `404` leaves the section out and is not logged. That keeps it hidden from a user who is not an
administrator, where `Ai:AllowTenantKeys` is off, and for a tenant with no directory row or a preview tenant. Any other
failure shows "The model key could not be loaded." and is logged. Its texts are `shell.users.modelKey.*`.

- **State.** The section says "No key is set." or shows a set key as `••••` followed by its last four characters. A key
  of four characters or fewer shows `••••` alone. The page never holds the whole key after it is saved.
- **Set and change.** The "Set key…" button, which reads "Change key…" once a key is set, opens a dialog with a password
  field. Its "Save" button is the only thing that sends the key, as `PUT /api/tenant/model-key`, and a click with the
  field blank shows what to enter under it and sends nothing. Closing the dialog clears the field. A failed save shows "The key was not saved."
- **Remove.** The "Remove key…" button opens a confirmation titled "Remove the model key?". Only the confirmation's
  "Remove" button sends `DELETE /api/tenant/model-key`. A failed remove shows "The key was not removed."
- **After a change.** The section reads `GET /api/tenant/model-key` again, so it shows what the server stored.
- **The switch** _(planned for M35)_. The section is the AI authoring switch, see
  [AI authoring](user-administration.md#ai-authoring). It shows to an administrator without `Ai:AllowTenantKeys`,
  and it does not depend on `GET /api/tenant/model-key`. Each person sets their own key on the
  [account page](#account-page), not here.

### Application packages

The People tab has an "Application packages" section below Model key, where
an administrator turns the tenant's application packages on or off, see
[application packages](user-administration.md#application-packages). The section is a component of its own, like Model
key. It loads `GET /api/tenant/application-packages` and renders nothing unless the route answers `200`, so a user who is
not an administrator and a preview tenant do not see it. Its texts are `shell.users.applicationPackages.*`.

- **State.** The section shows whether application packages are on.
- **Change.** One button opens a confirmation. Only the confirmation's button sends the `PUT`, and the section then
  reads `GET /api/tenant/application-packages` again, so it shows what the server stored.
- **A refusal.** A quota `403` shows the problem's title, see [refusals](tenant-quotas.md#refusals).

### Package runners

_(planned for M37)_

The People tab has a "Package runners" section below Application packages, where an administrator adds a
[customer-hosted runner](package-runners.md#customer-hosted-runners), sees the tenant's runners and revokes one, see
[package runners](user-administration.md#package-runners). The section is a component of its own, like Model key. It
loads `GET /api/tenant/package-runners` and renders nothing unless the route answers `200`, so a user who is not an
administrator and a preview tenant do not see it. Its texts are `shell.users.packageRunners.*`.

- **The list.** Each runner shows its name, its packages as id and version, and its last call. A runner that never
  took a call shows "Never".
- **Add.** The "Add runner…" button opens a dialog with a name field. Only its "Create" button sends
  `POST /api/tenant/package-runners`. The dialog then shows the client ID and the secret, with "Copy the secret now.
  It is not shown again." The section reads `GET /api/tenant/package-runners` again, so it shows what the server stored.
- **Revoke.** Each row has a "Revoke…" button that opens a confirmation titled "Revoke this runner?". Only its
  "Revoke" button sends `POST /api/tenant/package-runners/{id}/revoke`. The section then reads
  `GET /api/tenant/package-runners` again, so the runner drops out.

### Connected apps

The users page has a "Connected apps" tab where an administrator chooses which outside MCP clients may connect, sees the
apps that people consented to and revokes one, see [outside MCP clients](user-administration.md#outside-mcp-clients) and
[client registration](mcp-server.md#client-registration). The tab reads `GET /api/tenant/outside-clients` and shows only
when the route answers `200`. That keeps it hidden from a user who is not an administrator, for a preview tenant and for a
tenant with no directory row. A `403` or `404` is not logged, and any other failure is logged. Its texts are
`shell.users.connectedApps.*`.

- **Mode.** A choice of "All", "Allow list" or "None" shows the stored mode. Each option has its own help.
- **Filter.** The table of apps with consent has Filter on Name, Client ID, People who consented and Last used
  (the day). The filter is kept in the address as `connectedApps.filter` and narrows the rows the tab already
  holds, with no new request.
- **Client IDs.** Under "Allow list", an editable list of client IDs shows. A typed ID is added with Enter. The tab keeps
  the list when another mode is chosen and sends it under every mode, because the server stores it under every mode.
- **Trusted domains.** Under "All", an editable list of the domains from which the identity provider may read an MCP
  client's metadata document shows. A typed domain is added with Enter. The tab keeps the list when another mode is chosen
  and sends it under every mode, because the server stores it under every mode.
- **Save.** The "Save" button sends the three values with `PUT /api/tenant/outside-clients`, then reads
  `GET /api/tenant/outside-clients` again, so the tab shows what the server stored, with the IDs and the domains trimmed
  and without repeats. It then says "The setting was saved."
- **A failed save.** The tab says "The setting was not saved." and keeps the edited values on screen. A `502` says "The
  setting was stored, but the identity provider did not apply it. Save again to apply it.", because the server keeps the
  stored change when the identity provider fails.
- **Apps with consent.** Below the switch, a table headed "Apps with consent" lists each outside MCP client that people
  consented to, from `GET /api/tenant/connected-apps`: its "Name", its "Client ID", the "People who consented" and
  "Last used". Each person shows the display name and, when there is one, the email. A client with no name and a client
  that was never used show the em dash. With no such client, the table says "No app has consent yet. People give
  consent when they connect an MCP client." A list that does not load says "The apps with consent could not be
  loaded." in place of the rows, and the switch above still works.
- **Revoke.** Each row has a "Revoke…" button that opens a confirmation titled "Revoke the consent?". It names the
  client by its name, or by its client ID when it has no name: "Every person's consent to {client} is removed, and its
  tokens stop working at once. People must consent again to use it." Only the confirmation's "Revoke" button sends
  `POST /api/tenant/connected-apps/{clientId}/revoke`. On `204` the tab reads `GET /api/tenant/connected-apps` again, so
  the client drops out.
- **A failed revoke.** The row stays as it was and the tab says "The consent was not revoked." A `502` says "The
  identity provider did not finish the revoke. Revoke again to finish it.", because the server keeps the stored revoke
  when the identity provider fails and revoking again finishes it.

## Audit page

_(planned for M41)_

The platform route `/audit` is the tenant's audit, where an administrator searches and exports every audit
record of the tenant: data changes, sign-ins, administration, publishes and the commands of an operator.
The rules, the routes and what an item holds are in [the tenant's audit](record-audit.md#the-tenants-audit).
The page shows names and the values that the administrator may read, and never a value of a field that
person may not read. Its texts are `shell.audit.*`.

- **The table.** It lists the audit records from `GET /api/tenant/audit-records`, newest first, 20 to a page,
  in [the table](actions-and-controls.md#tables) that every list uses. The server filters, sorts and pages them.
- **Columns.** Each row has the time, the person, the action, the application, the entity, the record and
  the changes. The person is the display name, else the user id, and "System" for `system`. The changes
  column lists each changed field with its old and new value, or only the field's label when the
  administrator may not read the field, as [the form's history](form-widget.md#history) does. A header
  sorts by time.
- **Filter.** Filter, in the toolbar, finds records by person, application, entity, record, action and
  time. The table has no Search box, because the route takes none.
- **Export.** Export is a secondary action of the toolbar, with CSV and Excel as in
  [the table's export](import-export.md#in-the-spa). It exports the records that the current filter finds.
  A file that stopped at the row limit says so.
- **The address.** The view lives in the address as `filter`, `sort`, `page`, `pageSize` and `columns`, so a
  reload or the Back button keeps it.
- **Empty.** A filter that finds nothing says "No audit records match."
- **Refusals.** A `403` shows that the page is for administrators. Any other failure shows an error
  message.

## Recycle bin page

_(planned for M41)_

The platform route `/recycle-bin` is the recycle bin, where a person restores the records they deleted
and an administrator purges them. Anyone who is signed in may open it, and it lists only the records that the
person may restore. The rules, the routes and the refusals are in [recycle bin](recycle-bin.md). Its texts are
`shell.recycleBin.*`.

- **The table.** It lists the records from `GET /api/tenant/recycle-bin`, oldest deletion first, 20 to a
  page, in [the table](actions-and-controls.md#tables) that every list uses. A row shows the record by its
  display value, or by its id when the person may not read the display field, then its entity, its
  application, who deleted it and when, and when it will be purged. The record has no page of its own while
  it is in the bin, so its name is not a link.
- **Filter.** Filter, in the toolbar, finds records by application, entity, the time of the delete
  and who deleted them. The address holds `filter`, `sort`, `page` and `pageSize`, so a reload or the Back
  button keeps the view.
- **Restore.** Each row has Restore…, which opens a confirmation titled "Restore the record?" that names the
  record: "{record} comes back with its rows and files." A restore may start processes, which a delete cannot
  take back, so it asks first. Only the confirmation's "Restore" button sends the restore. On success the row
  leaves the table and a notice says "{record} restored." A refusal keeps the row and says why in the
  person's language: another record now has the same value in the field, or a record that this one refers to
  is in the recycle bin.
- **Purge.** Purge…, for an administrator only, opens a confirmation titled "Purge the record?" that names it:
  "{record} is removed for good. Its history stays. This cannot be undone." Only the confirmation's "Purge"
  button sends the purge, and the button is a destructive action. A refusal says that a record in the
  recycle bin refers to it.
- **The period.** An administrator sees the setting above the table: how many days a deleted record stays,
  from 1 to 365, with a Save button. It reads and writes the
  [period](recycle-bin.md#the-period). A value outside the range is refused before anything is sent.
  Another person does not see the setting.
- **Empty.** An empty bin says "The recycle bin is empty."
- **Refusals.** A failed load shows an error message.

