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

The platform route `/users` lists the tenant's users from `GET /api/users`, 20 to a page, for an
administrator. What an administrator may do there is in
[the users page](user-administration.md#the-users-page). Its texts are `shell.users.*`.

- **Table.** The table shows the display name, the email, how the person signs in (the provider's
  alias, or Account), the status as a tag and the last sign-in. Each page is fetched from the server.
- **Search.** A search box filters by name or email. A search starts on Enter or on the search button
  and goes back to page 1.
- **Roles.** Clicking a row opens a side panel with the person's roles from
  `GET /api/users/{id}/roles`, with a Give or Remove button for each role. The button asks for
  confirmation and calls the give-role or remove-role route. Its texts are `shell.users.roles.*`.
- **Commands.** Each row has buttons for its commands: Resend invitation, Disable or Enable, and Reset
  authenticator. Each asks for confirmation and calls its route.
- **Refusals.** A `403` shows that the page is for administrators. A `404`, which a preview tenant
  answers, shows the not-found page. Any other failure shows an error message.
