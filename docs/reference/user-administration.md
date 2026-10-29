# User administration

Detailed reference for the accounts of [Axis's identity provider](identity-provider.md), the roles that an
administrator gives, and the users page where both are managed.
[D100](../decisions/d100-identity-providers-per-application-and-sign-in-per-site.md) agrees it. M17 builds it, so
what is not built yet is marked _(planned for M17)_. The account rules are built: every realm that `tenant create` makes
holds them. The users page's list and the server commands that disable, enable and reset an authenticator are built. The invitations,
a user's details, the commands' buttons and the roles are not built yet. The conventions of [architecture.md](../architecture.md)
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

The server's list route, the page's list and the server routes of three commands are built. A user's details and the buttons that call the
commands are _(planned for M17)_.

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
- **A user:** opening a row shows the person's roles, for each application and for the platform, and the commands
  below. A field marked `sensitive` and record data are never shown.
- **Commands:** invite, resend an invitation, disable, enable, reset an account's authenticator, give a role and remove
  a role. Each command asks for confirmation, and each writes an [audit record](#audit). Disable, enable and reset an
  authenticator are built as server routes, which the user's details will call. The others are _(planned for M17)_.
  - `POST /api/users/{id}/disable`, `POST /api/users/{id}/enable` and `POST /api/users/{id}/reset-authenticator` answer
    `204` and take no body. They take a browser session only, so a service account's bearer token gets `401`.
  - A request is checked in this order: signed-in user (`401`), `Users:AdministratorRole` (`403`), a tenant with a users
    client (`404`, as for the list), for disable a login at the identity provider in the last 5 minutes (`403`), for disable
    not oneself (`409`), then the realm user. A user that the realm does not have answers `404`, and so does a service account.
    The checks come before the lookup, so a refused caller learns nothing about which ids exist.
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

- _(planned for M17)_ **Invitation:** an administrator invites a person by email and display name, and may give roles at once. The realm
  creates the account as `invited` and sends an email through the tenant's
  [mail settings](tenant-setting-commands.md#mail-settings), with a link that lasts 72 hours. Following it, the person
  sets a password and adds an authenticator app, and the account becomes `active`. An administrator can resend an
  invitation, which ends the old link.
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
  today. Until the tenant's mail settings work, a reset request sends nothing.
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

_(planned for M17)_

- **Application roles:** an administrator gives a person a role that an application's active release declares. Given
  roles add to the roles that the provider's `roles` mapping gives, see
  [users](identity-provider.md#users).
- **Platform roles:** an administrator gives the author, publisher, operator and administrator roles, as the values of
  `Studio:AuthorRole`, `Studio:PublisherRole`, `Console:OperatorRole` and `Users:AdministratorRole`.
- **Stored by Axis:** given roles are rows of the tenant database, each with the user, the application or the
  platform, and the role, so the realm holds no Axis role. A row for a role that the active release no longer
  declares stays and gives nothing, and the page marks it.
- **At once:** a change also rewrites the roles of the person's live sessions, so a removed role stops working at the
  next request.

## The first administrator

_(planned for M17)_

- `tenant create --administrator <email>` invites the first account with the administrator role, see
  [tenant create](administration.md#tenant-create).
- A tenant whose platform sign-in uses a brokered provider can instead give the role through that provider's claims,
  see [platform sign-in](identity-provider.md#platform-sign-in).

## Rights of the server

The list route and the disable, enable and reset authenticator routes are built. Inviting accounts is _(planned for M17)_.

- The server reads the realm's users through the realm's users client `axis-users`, with the rights `view-users` and
  `query-users`, see [tenant realm](host-settings.md#tenant-realm). The client also holds `manage-users`, which disable, enable
  and reset an authenticator need.
- The server changes accounts through the realm's users client, which has rights over the realm's users only. It
  cannot change the realm's providers, clients or settings. Its secret is stored encrypted in the tenant directory,
  see [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step).

## Audit

`user.disabled`, `user.enabled` and `user.authenticatorReset` are built. The others are _(planned for M17)_.

- Each command writes one audit record in the tenant database, with the administrator as actor and the user it acts
  on: `user.invited`, `user.invitationResent`, `user.disabled`, `user.enabled`, `user.authenticatorReset`,
  `user.roleGiven` and `user.roleRemoved`. A role record names the application or the platform, and the role. No
  record holds a password, a code or an invitation link.
