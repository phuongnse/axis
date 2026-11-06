# Platform pages

This page describes the pages that the platform site serves beside Studio: the account page, which any
signed-in person opens, and the users page, which an administrator opens. They are platform routes in
the shell, see [frontend](frontend.md) for the routes and [shell](shell.md) for the frame around them.
Both sit before the site route, so `account` and `users` are reserved site paths.

## Account page

The platform route `/account` is the person's account page, where they change their password, manage
their authenticators and end their sessions. It is the only self-service page: `tenant create` turns off
Keycloak's account console client and removes `manage-account` from the realm's default roles, see
[keeping the realm in step](identity-provider.md#keeping-the-realm-in-step).

### Password and authenticators

The page loads `GET /api/me/authenticators`. While that call is loading or has failed, the page shows
neither action, and a failure shows an error message. When `account` is `false`, the person is a user
of a brokered provider, and the page shows the sessions only.

- **Change password and Authenticators.** Above the sessions, two actions start the identity
  provider's own steps, as application-initiated actions through a sign-in with `kc_action`. Change
  password starts `UPDATE_PASSWORD`. Authenticators lists the person's authenticators with Add, which
  starts `CONFIGURE_TOTP`, and Remove for each, which starts `delete_credential:<id>`. Their pages are
  Axis pages, see [theme](identity-provider.md#theme), and Cancel on them returns to the account page
  with nothing changed.
- **The last authenticator stays.** Remove is off on a person's only authenticator, because every
  account needs one, see [accounts](user-administration.md#accounts).

### Sessions

The page lists the person's live sessions on the tenant's own hosts from `GET /api/me/sessions`, newest
sign-in first. A signed-out visit shows the sign-in prompt, because the list call is not exempt.

- **Rows.** A row shows Signed in and Last active, in the current locale. A This session tag marks the
  session in use now. It has no End button, because the person ends it with Sign out, which also ends
  the identity provider's session.
- **Ending sessions.** Every other row has an End button (`DELETE /api/me/sessions/{id}`). End all
  other sessions (`DELETE /api/me/sessions`) is disabled when no other session exists. After a `204` or
  a `403`, the page loads the list again, and any other answer shows an error message.
- **Sign in again.** When `endAllowed` is `false`, both kinds of button are disabled, and a message
  with a Sign in again button shows. The button goes to
  `/api/auth/sign-in?returnUrl=%2Faccount&reauthenticate=true`, so the person signs in again and
  returns to `/account`. A `403` means the 5-minute window has just closed, so the reloaded list shows
  this state.
- **Texts.** Its texts are `shell.account.sessions`, the menu entry, and `shell.account.sessions.*` for
  the page.

## Users page

The platform route `/users` is the users page, where an administrator manages the tenant's users and
their roles. Who may open it, the rules of each command, its routes and its audit records are in
[user administration](user-administration.md#the-users-page). The page shows names, never translated
labels, and never shows a field marked `sensitive` or record data. Its texts are `shell.users.*`.

### The list

The page lists the tenant's users from `GET /api/users`, 20 to a page, and fetches each page from the
server.

- **Columns.** Each row has the display name, the email, how the person signs in (the provider's alias
  as the [list route](user-administration.md#read-routes) sends it, or "Account"), the status as a tag
  (`invited`, `active` or `disabled`) and the last sign-in.
- **Search.** A search box filters by name or email. A search starts on Enter or on the search button
  and goes back to page 1.
- **Refusals.** A `403` shows that the page is for administrators. A `404`, which a preview tenant and
  a tenant without a users client answer, shows the not-found page. Any other failure shows an error
  message.

### Inviting and resending

- **Invite.** The "Invite" button next to the search box opens a form with the email, the display name
  and optional roles. The roles are in one section for each active application and one for the
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
- **Resend invitation.** A row that is `invited` and is an account, not a brokered user, has a "Resend
  invitation" button. It asks for confirmation, names the person and the email, calls the resend route,
  shows "The invitation was sent again." and loads the list again, because the resend gives the person
  a new id. It shows a `403` as "This page is for administrators." and a `409` as "This person has a
  password or signs in through a provider, so they cannot get a new invitation."

### Disabling, enabling and resetting an authenticator

- **Which buttons.** A row that is `active` or `invited` has a "Disable" button, and a row that is
  `disabled` has an "Enable" button instead. A row for an account, not a brokered user, has a "Reset
  authenticator" button. The Disable button also shows on the administrator's own row, where the server
  answers `409`.
- **Confirmation.** Each button asks for confirmation, names the person and the email, calls its route
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

- **A page of its own** _(planned for M31)_. Clicking a row opens the user as a page of its own, as
  [opening things](actions-and-controls.md#opening-things) says, which holds these sections.
- **Tags.** Each role shows the "Given" tag when the person has it. A given role that is not declared
  any more has a "Not declared" tag, and its section lists it even when its application is not active.
- **Give and Remove.** A Give or Remove button asks for confirmation, names the role, the place and the
  person, then calls the give-role or remove-role route and loads the roles again. A `403` shows as
  "Sign in again to change a platform role." and a `409` as "An administrator cannot remove their own
  administrator role."
- **What shows.** The roles that an administrator gave, not the platform roles that the provider's
  sign-in mapping gives.

### Export data and usage

In a tenant of a cell, the users page has two more sections for an administrator:

- **Export data.** Described below.
- **Usage** _(planned for M33)_. The tenant's quotas and its usage of the last 30 days, see
  [usage](tenant-quotas.md#usage).

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
