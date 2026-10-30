# User administration

Detailed reference for the accounts of [Axis's identity provider](identity-provider.md), the roles that an
administrator gives, and the users page where both are managed.
[D100](../decisions/d100-identity-providers-per-application-and-sign-in-per-site.md) agrees it. M17 builds it, so
what is not built yet is marked _(planned for M17)_. The account rules are built: every realm that `tenant create` makes
holds them. The users page's list and the server commands that disable, enable and reset an authenticator are built, and so are
the server routes that give and remove an application or a platform role. A user's roles panel with the Give and Remove
buttons is built too, and so are the Invite button and the Resend invitation button. The server routes that invite an account and resend an invitation are built, and so is `tenant create --administrator`.
The buttons of disable, enable and reset an authenticator are not built yet. The conventions of [architecture.md](../architecture.md)
apply.

## The administrator role

The setting, the shell entry and the users page's list are built.

- **An installation-level role,** not a `role` resource, like the author and the operator roles. The setting
  `Users:AdministratorRole` names the roles claim value, see [users settings](host-settings.md#users-settings). With no
  value, nobody may use the users page.
- **One tenant:** the users page is a platform route, `/users`, on each tenant's hosts, and shows only that tenant. A
  site's own host does not serve it. A preview tenant has no users page, and `GET /api/me` answers `users: false` there.
- **In the shell:** the app switcher and the home page's welcome band offer "Users" ("Invite people and give them
  roles.") to a person who holds the role, after Studio and the operator console, see
  [app switcher and preferences](shell.md#app-switcher-and-preferences). `GET /api/me` adds a boolean `users`.
- **Application policies play no part.** The role gives the users page and nothing in an application.

## The users page

The server's list route, the page's list, a user's roles panel with its Give and Remove buttons, and the server routes of
seven commands are built. The invite and resend an invitation buttons are built. The buttons of disable, enable and reset an authenticator are _(planned for M17)_.

- **The list route:** `GET /api/users?search=&page=&pageSize=` answers `{ items, totalCount }` for the tenant's realm to an
  administrator. `page` starts at 1 and defaults to 1. `pageSize` is 1 to 50 and defaults to 50. A value outside these
  answers `400`. Each item has `id`, `displayName`, `email`, `provider`, `status` and `lastSignedInAt`. `displayName` is
  the first and last name, or the username when the user has neither. `provider` is `null` for an account, otherwise
  the alias of the user's first federated identity. `status` is `disabled` when the realm user is disabled, `invited`
  when the user has no `axis.user_profiles` row, and `active` otherwise. `lastSignedInAt` comes from that row and is
  `null` without it. A service account is not listed. The route answers `401` with nobody signed in, `403` without
  `Users:AdministratorRole`, and `404` on a preview tenant and on a tenant that has no users client, such as one that
  signs in through an outside identity provider. When the identity provider fails, it answers `502` with a fixed
  title. The server's log names the step and the status code, never a body or a secret.
- **The list:** every user of the tenant, the people who have signed in and the accounts that are invited. Each row
  has the display name, the email, how the person signs in (the provider's alias as the route sends it, or "Account"), the status
  (`invited`, `active` or `disabled`) and the last sign-in. It searches by name or email, and pages. The page asks the
  route for 20 users at a time. A search starts on Enter or on the search button and goes back to page 1.
- **Invite:** the "Invite" button next to the search box opens a form with the email, the display name and optional roles.
  The roles are in one section for each active application and one for the platform, as in the roles panel. The form gets
  them from the roles route, called with the signed-in administrator's own id, and uses only `applications` and
  `platformRoles`. When they cannot be loaded, the form says so and still invites without roles. Submitting the form asks
  "Invite this person?" and names the email, the name and the roles. After the confirmation, the page calls the invite route
  and loads the list again, so the new row shows as `invited`. It shows a `403` as "Sign in again to give a platform
  role." and a `409` as "An account already uses this email." Any other failure shows "The invitation could not be sent."
  and loads the list again, because an invitation whose email failed leaves the account as `invited`.
- **Resend invitation:** a row that is `invited` and an account, not a brokered user, has a "Resend invitation" button. It
  asks for confirmation, names the person and the email, calls the resend route, shows "The invitation was sent again." and
  loads the list again, because the resend gives the person a new id. It shows a `403` as "This page is for
  administrators." and a `409` as "This person has a password or signs in through a provider, so they cannot get a new
  invitation."
- **The roles route:** `GET /api/users/{id}/roles` answers `{ given, applications, platformRoles }` to an administrator.
  `given` holds the person's given roles, each as `{ applicationId, role, declared }`, with a `null` `applicationId` for a
  platform role. `declared` is `false` for an application role that the application's active release no longer declares,
  and for a role of an application that has no active release. A platform role is declared when it is one of the four
  kinds. `applications` holds every active application as `{ id, name, roles }`, with the role names its active release
  declares. `platformRoles` holds the four kinds, even when a host setting has no value. The route checks in the order of
  the list route (`401`, `403`, `404` for a preview tenant or a tenant without a users client), then answers `404` for a user
  that the realm does not have and for a service account. When the identity provider fails, it answers `502` with a fixed title.
  It only reads, so it accepts any signed-in caller who holds `Users:AdministratorRole`, not only a browser session.
- **A user:** clicking a row opens a side panel with the person's roles, one section for each active application and one
  for the platform. Each role shows the "Given" tag when the person has it. A given role that is not declared any more
  has a "Not declared" tag, and its section lists it even when its application is not active. A Give or Remove button
  asks for confirmation, names the role, the place and the person, and then calls the give-role or remove-role route and
  loads the roles again. The panel shows a `403` as "Sign in again to change a platform role." and a `409` as
  "An administrator cannot remove their own administrator role." The panel shows names, not translated labels. It shows
  what an administrator gave, not the platform roles that the provider's sign-in mapping gives. A field marked
  `sensitive` and record data are never shown.
- **Commands:** invite, resend an invitation, disable, enable, reset an account's authenticator, give a role and remove
  a role. Each command asks for confirmation, and each writes an [audit record](#audit). Disable, enable, reset an
  authenticator, give and remove an application or a platform role, invite and resend an invitation are built as server routes.
  The roles panel calls give and remove, and the users page calls invite and resend an invitation. The buttons of disable, enable and reset an authenticator are _(planned for M17)_.
  - `POST /api/users/{id}/disable`, `POST /api/users/{id}/enable` and `POST /api/users/{id}/reset-authenticator` answer
    `204` and take no body. They take a browser session only, so a service account's bearer token gets `401`.
  - A request is checked in this order: signed-in user (`401`), `Users:AdministratorRole` (`403`), a tenant with a users
    client (`404`, as for the list), for disable a login at the identity provider in the last 5 minutes (`403`), for disable
    not oneself (`409`), then the realm user. A user that the realm does not have answers `404`, and so does a service account.
    The checks come before the lookup, so a refused caller learns nothing about which ids exist.
  - `POST /api/users/{id}/give-role` and `POST /api/users/{id}/remove-role` take the JSON body
    `{ "applicationId": "<uuid>", "role": "<name>" }` for an application role, or `{ "role": "<kind>" }` for a platform
    role, where the kind is `author`, `publisher`, `operator` or `administrator`. They answer `204` and take a browser
    session only. They check in the order above up to the realm user (`401`, `403`, `404`), and only then read the body,
    so a refused caller learns nothing about the body. They answer `400` with a fixed title for a body that is missing or
    malformed, a blank role, a body without `applicationId` whose role is not one of the four kinds, an application with
    no active release, and, for give, a role that the active release does not declare. Remove needs no declaration, so
    an administrator can clear a row for a role that a later release dropped. For a platform role, the checks after the
    body are a login at the identity provider in the last 5 minutes (`403`) and, for remove of the administrator role on
    oneself, `409`. Giving a role the person holds, or removing one they do not hold, answers `204`, changes nothing and
    writes no audit record. A change to an application role needs no recent login.
  - `POST /api/users/invite` takes the JSON body `{ "email": "...", "displayName": "...", "roles": [{ "applicationId": "<uuid>",
    "role": "<name>" }] }`, where `roles` is optional and each role is as in the give-role route. It answers `201` with
    `{ "id": "<realm user id>" }`. `POST /api/users/{id}/resend-invitation` takes no body and answers `204`. Both take a
    browser session only. Invite checks in the order of the routes above (`401`, `403`, `404`), then reads the body and answers
    `400` with a fixed title for a malformed body, a blank display name, an email that is not an address, and a role that the
    give-role route refuses. The email is trimmed and lower-cased. A platform role then needs a login at the identity provider
    in the last 5 minutes (`403`), and an email that an account already uses answers `409`. A user of a brokered provider does
    not use the email. Invite creates the realm user, commits the given roles and the audit records, and only then emails
    the link, so a failed email answers `502` and the account stays `invited`. The administrator resends the invitation then.
    The whole display name is the user's first name, and the last name stays empty. The link leads to the tenant's home, the
    scheme and host of the request. Resend checks in the same order up to the realm user (`404`, also for a service account)
    and answers `409` for a user of a brokered provider, for a person who has signed in, and for a user with a password, even
    one who never signed in to Axis. It needs no recent login, because it gives no new right.
  - Enable and reset an authenticator need no recent login. For a user of a brokered provider, reset an authenticator
    answers `409` and changes nothing.
  - The realm steps run first. When the identity provider fails, the route answers `502` with a fixed title, and the
    server's log names the step and the status code, never a body or a secret. A command that stopped partway can run again.
- **Fresh sign-in:** giving or removing a platform role, and disabling a person, need a session whose `auth_time` is no
  more than 5 minutes old, as ending sessions does, see
  [session lifetimes and re-authentication](security.md#session-lifetimes-and-re-authentication).
- **Not on oneself:** an administrator cannot disable themselves or remove their own administrator role, so a tenant
  never loses its last administrator from the page.

## Accounts

Every realm that `tenant create` makes holds the rules for a second step, passwords, failed sign-ins, the reset link and
no self-registration, and a second run of the command brings an existing realm in step. The development `axis` realm
asks its development users for a code, because each one has an authenticator, and it keeps Keycloak's other defaults.

- **Invitation:** an administrator invites a person by email and display name, and may give roles at once. The realm
  creates the account as `invited` and sends an email through the tenant's
  [mail settings](tenant-setting-commands.md#mail-settings), with a link that lasts 72 hours. Following it, the person
  sets a password and adds an authenticator app, and the account becomes `active` after the first sign-in. An administrator can resend an
  invitation. The identity provider cannot revoke a link it issued, so the resend deletes the realm user and creates it
  again with a new id, which ends the old link. The given roles move to the new id. If the resend stops between the delete and
  the create, the person has no account and the administrator invites them again. The routes are built, see
  [the users page](#the-users-page), and so are their buttons.
- **Username:** an account's username is its email, and it signs in with its email. The realm allows two users to
  share an email, because a person who signs in through two brokered providers becomes two users, and Keycloak then
  looks accounts up by username only. The sign-in form asks for a "Username". A brokered user's username is the
  provider's alias, a dot and the subject, see [users](identity-provider.md#users).
- **No self-registration:** the realm shows no registration link, and nobody creates an account without an invitation.
- **A second step:** every sign-in with an account asks for a 6-digit code from the authenticator app, and an account
  with no authenticator yet must add one before it gets in. The realm requires the OTP form of its browser flow. A person who
  signs in through a brokered provider follows that provider's MFA policy instead, as
  [D99](../decisions/d99-axis-ships-its-own-identity-provider.md) agrees.
- **Passwords:** at least 12 and at most 128 characters, with no rules on which characters, and not on the realm's list
  of breached passwords, a file that the identity provider holds, see
  [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step). "Forgot password?" sends a link by email
  that lasts 30 minutes. The 30 minutes apply to every link that a person requests, and password reset is the only one
  today. The realm sends the link through the tenant's [mail settings](tenant-setting-commands.md#mail-settings), or through
  the host's `Email` settings when the tenant has none, see
  [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step). With no mail settings at all, a reset request sends nothing.
- **Failed sign-ins:** after 5 failed sign-ins in a row the account waits before the next try, starting at 1 minute
  and growing by 1 minute with each further 5 failures, up to 15 minutes. This is Keycloak's own temporary lockout, which
  cannot double the wait. The sign-in page shows the same message whether the account is locked, the password is wrong
  or the account does not exist.
- **Reset authenticator:** removes the account's authenticator, so its next sign-in asks the person to add one again,
  and ends the person's sessions. A user of a brokered provider has no authenticator in the realm, so the reset does not apply to it.

## Disabled users

The server routes are built.

- Disabling a person, an account or a user of a brokered provider, stops every sign-in of theirs to the tenant and
  ends their Axis sessions and their realm sessions at once.
- Enabling them lets them sign in again. Their roles stay as they were.

## Roles

- **Application roles:** an administrator gives a person a role that an application's active release declares. Given
  roles add to the roles that the provider's `roles` mapping gives, see
  [users](identity-provider.md#users).
- **Platform roles:** an administrator gives the author, publisher, operator and administrator roles. A row holds the
  kind, such as `operator`. Each request maps the kind to the value of `Studio:AuthorRole`, `Studio:PublisherRole`,
  `Console:OperatorRole` or `Users:AdministratorRole`, and the name joins the roles from sign-in. A kind whose setting
  has no value gives nothing.
- **Stored by Axis:** given roles are rows of the tenant database, each with the user, the application or the
  platform, and the role, so the realm holds no Axis role. A row for a role that the active release no longer
  declares stays and gives nothing, and the page marks it.
- **At the next request:** a session reads the person's given roles at every request, in the statement that moves its
  last request time, and keeps them for that request only. A change applies at the person's next request, on every
  replica, and the person does not sign in again.
- **On a preview tenant:** the preview has no users page and its own table stays empty. A session on it reads the given
  roles, application and platform, of its main tenant at every request, from the main tenant's database. Removing a role
  on the main tenant takes it away on the preview at the person's next request. When the main tenant is not served, the
  session reads none.

## The first administrator

The first administrator is built.

- `tenant create --administrator <email>` invites the first account with the administrator role, see
  [tenant create](administration.md#tenant-create). Its `administrator` step creates the invited account in the
  tenant's realm and gives it the `administrator` platform role. It then has the realm email the invitation link, as the
  invite route of the users page does, and sets the tenant `active`.
- The tenant database gets one `user.invited` record and one `user.roleGiven` record for the platform role. Their
  actor is the command, `tenant create`, and has no display name. The account has no display name either, so the users
  list shows its email. No record holds the link.
- The emailed link names no address to return to, because the realm's `axis` client has no return addresses yet. The
  person can set a password and an authenticator, and cannot yet sign in to Axis until the realm learns the tenant's
  addresses.
- A rerun after a failed invitation finds the account by its email, so there is never a second account or a second
  `user.invited` record. It gives the role and writes the records only when the first run did not. It sends the email
  again only when the account has no password yet.
- A tenant whose platform sign-in uses a brokered provider can instead give the role through that provider's claims,
  see [platform sign-in](identity-provider.md#platform-sign-in).

## Rights of the server

The list route, the disable, enable and reset authenticator routes, the give and remove role routes and the invite and resend routes are built.

- The server reads the realm's users through the realm's users client `axis-users`, with the rights `view-users` and
  `query-users`, see [tenant realm](host-settings.md#tenant-realm). The client also holds `manage-users`, which disable, enable
  and reset an authenticator need.
- The server changes accounts through the realm's users client, which has rights over the realm's users only. It
  cannot change the realm's providers, clients or settings. Its secret is stored encrypted in the tenant directory,
  see [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step).

## Audit

All the actions below are built.

- Each command writes one audit record in the tenant database, with the administrator as actor and the user it acts
  on: `user.invited`, `user.invitationResent`, `user.disabled`, `user.enabled`, `user.authenticatorReset`,
  `user.roleGiven` and `user.roleRemoved`. `details` holds `userId` and, for an invitation, `email`. A resend writes the new user
  id in `userId` and the old one in `previousUserId`, because the id changes. A role given at invite time writes a `user.roleGiven`
  record too, and so does `tenant create --administrator`, whose actor is the command. A role record names the application in its application id, or for a platform role has no application and says
  `platform: true` in its details, and holds the role (the kind, for a platform role) in its details. Giving a role the person holds, or removing one they do
  not hold, is not a change and writes no record. No
  record holds a password, a code or an invitation link.
