# User administration

This page describes the accounts of [Axis's identity provider](identity-provider.md), the roles that an
administrator gives people, and the users page where an administrator manages both. It covers the page's commands and
routes, the account rules of every tenant realm, and the audit records that each change writes.

## The administrator role

The administrator role decides who may use the users page. It is an installation-level role, not a `role` resource,
like the author and the operator roles.

- **The setting.** `Users:AdministratorRole` names the roles claim value, see
  [users settings](host-settings.md#users-settings). With no value, nobody may use the users page.
- **One tenant.** The users page is a platform route, `/users`, on each tenant's hosts, and shows only that tenant. A
  site's own host does not serve it. A preview tenant has no users page, and `GET /api/me` answers `users: false` there.
- **In the shell.** The app switcher and the home page's tools offer "Users" ("Invite people and give them roles.") to
  a person who holds the role, after Studio and the operator console, see
  [app switcher and preferences](shell.md#app-switcher-and-preferences). `GET /api/me` adds a boolean `users`.
- **Application policies play no part.** The role gives the users page and nothing in an application.
- **The control plane reads it.** `GET /api/cell/tenants/{id}/administrators` lists the enabled holders with an email, see
  [routes](tenant-lifecycle.md#routes).

## The users page

The users page lists every user of the tenant and holds the commands an administrator runs on them: invite, resend an
invitation, disable, enable, reset an account's authenticator, give a role and remove a role. Each command asks for
confirmation first and writes an [audit record](#audit). What the page shows, and what it says for each answer, is in
[users page](platform-pages.md#users-page).

### The list

The list holds the people who have signed in and the accounts that are invited, searched by name or email and paged,
from [the list route](#read-routes).

### Inviting and resending

An administrator invites a person and can send an account's invitation again, by the rules of [accounts](#accounts),
from the form and the button of [inviting and resending](platform-pages.md#inviting-and-resending).

### Disabling, enabling and resetting an authenticator

An administrator disables and enables a person, see [disabled users](#disabled-users), and resets an account's
authenticator, see [accounts](#accounts), from the buttons of
[disabling, enabling and resetting](platform-pages.md#disabling-enabling-and-resetting-an-authenticator).

### A user's roles

An administrator gives and removes a person's roles, see [roles](#roles), in the sections of
[a user's roles](platform-pages.md#a-users-roles).

### Export data and usage

_(planned for M33)_

In a tenant of a cell, the users page also shows the tenant's exports and usage, see
[export data and usage](platform-pages.md#export-data-and-usage).

### Read routes

Three routes read users and their roles. When the identity provider fails, each answers `502` with a fixed title, and
the server's log names the step and the status code, never a body or a secret.

- **The list route.** `GET /api/users?search=&page=&pageSize=` answers `{ items, totalCount }` for the tenant's realm
  to an administrator. `page` starts at 1 and defaults to 1. `pageSize` is 1 to 50 and defaults to 50, and a value
  outside these answers `400`. A service account is not listed. The route answers `401` with nobody signed in, `403`
  without `Users:AdministratorRole`, and `404` on a preview tenant and on a tenant that has no users client, such as one
  that signs in through an outside identity provider. Each item has these fields:

  | Field            | Value                                                                                                                       |
  | ---------------- | --------------------------------------------------------------------------------------------------------------------------- |
  | `id`             | The realm user's id                                                                                                         |
  | `displayName`    | The first and last name, or the username when the user has neither                                                          |
  | `email`          | The user's email                                                                                                            |
  | `provider`       | `null` for an account, otherwise the alias of the user's first federated identity                                           |
  | `status`         | `disabled` when the realm user is disabled, `invited` when the user has no `axis.user_profiles` row, and `active` otherwise |
  | `lastSignedInAt` | From the `axis.user_profiles` row, and `null` without it                                                                    |

- **The roles route.** `GET /api/users/{id}/roles` answers `{ given, applications, platformRoles }` to an
  administrator.
  - `given` holds the person's given roles, each as `{ applicationId, role, declared }`, with a `null` `applicationId`
    for a platform role. `declared` is `false` for an application role that the application's active release does not
    declare, and for a role of an application that has no active release. A platform role is declared when it is one
    of the four kinds.
  - `applications` holds every active application as `{ id, name, roles }`, with the role names its active release
    declares. `platformRoles` holds the four kinds, even when a host setting has no value.
  - The route checks in the order of the list route (`401`, `403`, `404` for a preview tenant or a tenant without a
    users client), then answers `404` for a user that the realm does not have and for a service account. It only
    reads, so it accepts any signed-in caller who holds `Users:AdministratorRole`, not only a browser session.
- **The authenticators route.** `GET /api/me/authenticators` answers
  `{ account, authenticators: [{ id, label, createdAt }] }` to the signed-in person about themselves.
  - `authenticators` holds the person's authenticator apps, the `otp` credentials of the realm, oldest first, and never
    a password or another credential type. `label` is the realm's `userLabel`, `null` when empty, and `createdAt` is an
    ISO timestamp, or `null` when the realm has no date.
  - `account` is `false`, with an empty list, for a user of a brokered provider, a realm user that is missing, a service
    account, a preview tenant and a tenant without a users client.
  - The route takes a session only, so a signed-out caller and a service account's bearer token get `401`. It needs no
    administrator role and no recent login at the identity provider, because it only reads.

### Command routes

Each command is a route that answers to an administrator with a browser session only, so a service account's bearer
token gets `401`.

| Route                                      | Body                                                                                                   | Answer                                   |
| ------------------------------------------ | ------------------------------------------------------------------------------------------------------ | ---------------------------------------- |
| `POST /api/users/invite`                   | `{ "email": "...", "displayName": "...", "roles": [{ "applicationId": "<uuid>", "role": "<name>" }] }` | `201` with `{ "id": "<realm user id>" }` |
| `POST /api/users/{id}/resend-invitation`   | None                                                                                                   | `204`                                    |
| `POST /api/users/{id}/disable`             | None                                                                                                   | `204`                                    |
| `POST /api/users/{id}/enable`              | None                                                                                                   | `204`                                    |
| `POST /api/users/{id}/reset-authenticator` | None                                                                                                   | `204`                                    |
| `POST /api/users/{id}/give-role`           | `{ "applicationId": "<uuid>", "role": "<name>" }`, or `{ "role": "<kind>" }` for a platform role       | `204`                                    |
| `POST /api/users/{id}/remove-role`         | As for give-role                                                                                       | `204`                                    |

A platform role's kind is `author`, `publisher`, `operator` or `administrator`.

- **The order of the checks.** A request is checked in this order: a signed-in user (`401`),
  `Users:AdministratorRole` (`403`), a tenant with a users client (`404`, as for the list), for disable a login at the
  identity provider in the last 5 minutes (`403`), for disable not oneself (`409`), then the realm user. A user that the
  realm does not have answers `404`, and so does a service account. The checks come before the lookup, so a refused
  caller learns nothing about which ids exist.
- **Give and remove a role.** These check in that order up to the realm user (`401`, `403`, `404`), and only then read
  the body, so a refused caller learns nothing about the body. They answer `400` with a fixed title for a body that is
  missing or malformed, a blank role, a body without `applicationId` whose role is not one of the four kinds, an
  application with no active release, and, for give, a role that the active release does not declare. Remove needs no
  declaration, so an administrator can clear a row for a role that a later release dropped. For a platform role, the
  checks after the body are a login at the identity provider in the last 5 minutes (`403`) and, for removing the
  administrator role from oneself, `409`. A change to an application role needs no recent login. Giving a role the
  person holds, or removing one they do not hold, answers `204`, changes nothing and writes no audit record.
- **Invite.** Invite checks `401`, `403` and `404` as above, then reads the body. `roles` is optional, and each role is
  as in the give-role route. It answers `400` with a fixed title for a malformed body, a blank display name, an email
  that is not an address, and a role that the give-role route refuses. The email is trimmed and lower-cased. A platform
  role then needs a login at the identity provider in the last 5 minutes (`403`), and an email that an account already
  uses answers `409`. A user of a brokered provider does not use the email. Invite creates the realm user, commits the
  given roles and the audit records, and only then emails the link, so a failed email answers `502` and the account
  stays `invited`, and the administrator resends the invitation. The whole display name becomes the user's first name,
  and the last name stays empty. The link leads to the tenant's home, at the scheme and host of the request.
- **Resend an invitation.** Resend checks in the same order up to the realm user (`404`, also for a service account),
  and answers `409` for a user of a brokered provider, for a person who has signed in, and for a user with a password,
  even one who never signed in to Axis. It needs no recent login, because it gives no new right.
- **Enable and reset an authenticator.** Neither needs a recent login. For a user of a brokered provider, reset an
  authenticator answers `409` and changes nothing.
- **Quotas in a cell.** In a cell, the invite and enable routes also refuse a change that would pass the tenant's
  `users` quota, see [refusals](tenant-quotas.md#refusals). Invite answers the quota `403` after `401`, `403` and `404`
  and before it reads the body, so a malformed body gets the quota `403` too. Enable of a disabled user answers it after
  the realm user lookup. Enabling a user who is already enabled is never refused, and the other commands are not
  checked. A refused command changes nothing in the realm. When the identity provider fails while counting, the route
  answers `502`.
- **Failures of the identity provider.** The realm steps run first. When the identity provider fails, the route
  answers `502` with a fixed title, and the server's log names the step and the status code, never a body or a secret.
  A command that stopped partway can run again.
- **Fresh sign-in.** Giving or removing a platform role, and disabling a person, need a session whose `auth_time` is
  no more than 5 minutes old, as ending sessions does, see
  [session lifetimes and re-authentication](security.md#session-lifetimes-and-re-authentication).
- **Not on oneself.** An administrator cannot disable themselves or remove their own administrator role, so a tenant
  never loses its last administrator from the page.

## Accounts

Every realm that `tenant create` makes holds the account rules below: an invitation, a second step, passwords, failed
sign-ins, the reset link and no self-registration. A second run of the command brings an existing realm in step. The
development `axis` realm asks its development users for a code, because each one has an authenticator, and it keeps
Keycloak's other defaults.

- **Invitation.** An administrator invites a person by email and display name, and may give roles at once. The realm
  creates the account as `invited` and sends an email through the tenant's
  [mail settings](tenant-setting-commands.md#mail-settings), with a link that lasts 72 hours. Following it, the person
  sets a password and adds an authenticator app, and the account becomes `active` after the first sign-in. The routes
  are in [command routes](#command-routes).
- **Resending an invitation.** The identity provider cannot revoke a link it issued, so a resend deletes the realm user
  and creates it again with a new id, which ends the old link. The given roles move to the new id. If the resend stops
  between the delete and the create, the person has no account and the administrator invites them again.
- **Username.** An account's username is its email, and it signs in with its email. The realm allows two users to share
  an email, because a person who signs in through two brokered providers becomes two users, and Keycloak then looks
  accounts up by username only. The sign-in form asks for a "Username". A brokered user's username is the provider's
  alias, a dot and the subject, see [users](identity-provider.md#users).
- **No self-registration.** The realm shows no registration link, and nobody creates an account without an invitation.
- **A second step.** Every sign-in with an account asks for a 6-digit code from the authenticator app, and an account
  with no authenticator yet must add one before it gets in. The realm requires the OTP form of its browser flow. A
  person who signs in through a brokered provider follows that provider's MFA policy instead.
- **Passwords.** A password has at least 12 and at most 128 characters, with no rules on which characters, and is not
  on the realm's list of breached passwords, a file that the identity provider holds, see
  [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step).
- **Password reset.** "Forgot password?" sends a link by email that lasts 30 minutes. The 30 minutes apply to every
  link that a person requests, and the password reset is the only such link. The realm sends it through the tenant's
  [mail settings](tenant-setting-commands.md#mail-settings), or through the host's `Email` settings when the tenant has
  none, see [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step). With no mail settings at all, a
  reset request sends nothing.
- **Failed sign-ins.** After 5 failed sign-ins in a row the account waits before the next try, starting at 1 minute and
  growing by 1 minute with each further 5 failures, up to 15 minutes. This is Keycloak's own temporary lockout, which
  cannot double the wait. The sign-in page shows the same message whether the account is locked, the password is wrong
  or the account does not exist.
- **Reset authenticator.** A reset removes the account's authenticator, so its next sign-in asks the person to add one
  again, and ends the person's sessions. A user of a brokered provider has no authenticator in the realm, so the reset
  does not apply to it.
- **Self-service.** A person changes their own password and adds or removes their own authenticators on Axis's
  [account page](platform-pages.md#account-page), in place of Keycloak's account console. A user of a brokered provider
  has neither in the realm, so the page offers neither.
- **Terms.** When the tenant sets terms, a person accepts them after the first sign-in and again on each new version,
  see [accepting the terms](legal.md#accepting-the-terms).

## Disabled users

An administrator disables a person to stop their access at once, and enables them to give it back.

- **Disable.** Disabling a person, an account or a user of a brokered provider, stops every sign-in of theirs to the
  tenant and ends their Axis sessions and their realm sessions at once.
- **Enable.** Enabling them lets them sign in again. Their roles stay as they were.

## Roles

An administrator gives two kinds of roles: the roles that an application declares, and the platform roles. Axis
stores both and applies them at the person's next request.

- **Application roles.** An administrator gives a person a role that an application's active release declares. Given
  roles add to the roles that the provider's `roles` mapping gives, see [users](identity-provider.md#users).
- **Platform roles.** An administrator gives the author, publisher, operator and administrator roles. A row holds the
  kind, such as `operator`. Each request maps the kind to the value of `Studio:AuthorRole`, `Studio:PublisherRole`,
  `Console:OperatorRole` or `Users:AdministratorRole`, and the name joins the roles from sign-in. A kind whose setting
  has no value gives nothing.
- **Stored by Axis.** Given roles are rows of the tenant database, each with the user, the application or the platform,
  and the role, so the realm holds no Axis role. A row for a role that the active release does not declare stays and
  gives nothing, and the page marks it.
- **At the next request.** A session reads the person's given roles at every request, in the statement that moves its
  last request time, and keeps them for that request only. A change applies at the person's next request, on every
  replica, and the person does not sign in again.
- **On a preview tenant.** The preview has no users page and its own table stays empty. A session on it reads the given
  roles, application and platform, of its main tenant at every request, from the main tenant's database. Removing a
  role on the main tenant takes it away on the preview at the person's next request. When the main tenant is not
  served, the session reads none.

## The first administrator

A new tenant gets its first administrator from `tenant create --administrator <email>`, see
[tenant create](administration.md#tenant-create), because nobody holds the role to invite one from the users page.

- **The step.** The command's `administrator` step creates the invited account in the tenant's realm and gives it the
  `administrator` platform role. It then has the realm email the invitation link, as the invite route does, and sets
  the tenant `active`.
- **The records.** The tenant database gets one `user.invited` record and one `user.roleGiven` record for the platform
  role. Their actor is the command, `tenant create`, and has no display name. The account has no display name either,
  so the users list shows its email. No record holds the link.
- **The link.** The emailed link names no address to return to. The person sets a password and an authenticator, and
  then signs in on one of the tenant's hosts.
- **A rerun.** A rerun after a failed invitation finds the account by its email, so there is never a second account or
  a second `user.invited` record. It gives the role and writes the records only when the first run did not. It sends
  the email again only when the account has no password yet.
- **Through a brokered provider.** A tenant whose platform sign-in uses a brokered provider can instead give the role
  through that provider's claims, see [platform sign-in](identity-provider.md#platform-sign-in).

## Rights of the server

The server reads and changes the realm's users through the realm's users client, `axis-users`, and has rights over
nothing else in the realm.

- **Its rights.** The client holds `view-users` and `query-users` to read the realm's users, and `manage-users`,
  which disable, enable and reset an authenticator need, see [tenant realm](host-settings.md#tenant-realm).
- **Its limits.** The client has rights over the realm's users only. It cannot change the realm's providers, clients or
  settings. Its secret is stored encrypted in the tenant directory, see
  [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step).

## Audit

Each command writes one audit record in the tenant database, with the administrator as actor and the user it acts on.

| Action                    | Command                                                                          |
| ------------------------- | -------------------------------------------------------------------------------- |
| `user.invited`            | Invite, and `tenant create --administrator`                                      |
| `user.invitationResent`   | Resend an invitation                                                             |
| `user.disabled`           | Disable                                                                          |
| `user.enabled`            | Enable                                                                           |
| `user.authenticatorReset` | Reset an authenticator                                                           |
| `user.roleGiven`          | Give a role, each role given at invite time, and `tenant create --administrator` |
| `user.roleRemoved`        | Remove a role                                                                    |

- **Details.** `details` holds `userId` and, for an invitation, `email`. A resend writes the new user id in `userId`
  and the old one in `previousUserId`, because the id changes.
- **Role records.** A role record names the application in its application id, or, for a platform role, has no
  application and says `platform: true` in its details. Its details hold the role, the kind for a platform role.
- **Not a change.** Giving a role the person holds, or removing one they do not hold, writes no record.
- **The actor of `tenant create`.** The records of `tenant create --administrator` have the command as actor.
- **Never in a record.** No record holds a password, a code or an invitation link.
