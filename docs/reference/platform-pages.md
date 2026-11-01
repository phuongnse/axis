# Platform pages

Detailed reference for the pages that the platform site serves beside Studio: the sessions screen, which any signed-in
person opens, and the users page, which an administrator opens. They are platform routes in the shell, see
[frontend](frontend.md) for the routes and [shell](shell.md) for the frame around them. Both sit before the site route,
so their paths are reserved site paths.

- **Sessions screen.** The platform route `/account` lists the person's live
  sessions on the tenant's own hosts from `GET /api/me/sessions`, newest sign-in first. It sits beside
  `/studio`, before the site route, so `account` is a reserved site path. A
  signed-out visit shows the sign-in prompt, because the list call is not exempt.
  - A row shows **Signed in** and **Last active**, in the current locale. A
    **This session** tag marks the session in use now. It has no **End**
    button, because the person ends it with **Sign out**, which also ends the
    identity provider's session.
  - Every other row has an **End** button (`DELETE /api/me/sessions/{id}`).
    **End all other sessions** (`DELETE /api/me/sessions`) is disabled when no
    other session exists. After a `204` or a `403`, the screen loads the list
    again. Any other answer shows an error message.
  - When `endAllowed` is `false`, both kinds of button are disabled, and a
    message with a **Sign in again** button shows. The button goes to
    `/api/auth/sign-in?returnUrl=%2Faccount&reauthenticate=true`, so the person
    logs in again and returns to `/account`. A `403` means the 5-minute window
    just closed, so the reloaded list shows this state.
  - Its texts are `shell.account.sessions`, the menu entry, and
    `shell.account.sessions.*` for the screen.
- **Users page.** The platform route `/users` lists the tenant's users from
  `GET /api/users`, 20 to a page, and sits beside `/account`, before the site route,
  so `users` is a reserved site path. Its texts are `shell.users.*`.
  - The table shows the display name, the email, how the person signs in (the
    provider's alias, or **Account**), the status as a tag and the last sign-in.
    A search box filters by name or email. A search starts on Enter or on the
    search button and goes back to page 1. Each page is fetched from the server.
  - A `403` shows that the page is for administrators. A `404`, which a preview
    tenant answers, shows the not-found page. Any other failure shows an error message.
  - Clicking a row opens a side panel with the person's roles from
    `GET /api/users/{id}/roles`, with a Give or Remove button for each role. The button
    asks for confirmation and calls the give-role or remove-role route, see
    [the users page](user-administration.md#the-users-page). Its texts are
    `shell.users.roles.*`.
  - Each row has buttons for its commands: Resend invitation, Disable or Enable,
    and Reset authenticator. Each asks for confirmation and calls its route, see
    [the users page](user-administration.md#the-users-page).

## Account page

_(planned for M31)_ The sessions screen at `/account` becomes the person's account page, which replaces Keycloak's
account console, as [D128](../decisions/d128-rules-from-the-studio-review.md) agrees.

- **Change password and Authenticators.** Above the sessions, two actions start the identity provider's own steps, as
  application-initiated actions through a sign-in with `kc_action`: Change password starts `UPDATE_PASSWORD`, and
  Authenticators lists the person's authenticators with Add, which starts `CONFIGURE_TOTP`, and Remove for each, which
  starts `delete_credential:<id>`. Their pages are Axis pages, see [theme](identity-provider.md#theme), and Cancel on
  them returns to the account page with nothing changed.
- **The last authenticator stays.** Remove is off on a person's only authenticator, because every account needs one,
  see [accounts](user-administration.md#accounts).
- **No account console.** `tenant create` turns off Keycloak's account console client and removes `manage-account`
  from the realm's default roles, so the account page is the only self-service, see
  [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step).
