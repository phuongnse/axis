# User administration

Detailed reference for the accounts of [Axis's identity provider](identity-provider.md), the roles that an
administrator gives, and the users page where both are managed.
[D100](../decisions/d100-identity-providers-per-application-and-sign-in-per-site.md) agrees it. None of it is built
yet: M17 builds it, so every section is _(planned for M17)_. The conventions of [architecture.md](../architecture.md)
apply.

## The administrator role

_(planned for M17)_

- **An installation-level role,** not a `role` resource, like the author and the operator roles. The setting
  `Users:AdministratorRole` names the roles claim value, see [users settings](host-settings.md#users-settings). With no
  value, nobody may use the users page.
- **One tenant:** the users page is a platform route, `/users`, on each tenant's hosts, and shows only that tenant. A
  site's own host does not serve it. A preview tenant has no users page.
- **In the shell:** the app switcher and the home page's welcome band offer "Users" ("Invite people and give them
  roles.") to a person who holds the role, after Studio and the operator console, see
  [app switcher and preferences](shell.md#app-switcher-and-preferences). `GET /api/me` adds a boolean `users`.
- **Application policies play no part.** The role gives the users page and nothing in an application.

## The users page

_(planned for M17)_

- **The list:** every user of the tenant, the people who have signed in and the accounts that are invited. Each row
  has the display name, the email, how the person signs in (the provider's label, or "Account"), the status
  (`invited`, `active` or `disabled`) and the last sign-in. It searches by name or email, and pages.
- **A user:** opening a row shows the person's roles, for each application and for the platform, and the commands
  below. A field marked `sensitive` and record data are never shown.
- **Commands:** invite, resend an invitation, disable, enable, reset an account's authenticator, give a role and remove
  a role. Each command asks for confirmation, and each writes an [audit record](#audit).
- **Fresh sign-in:** giving or removing a platform role, and disabling a person, need a session whose `auth_time` is no
  more than 5 minutes old, as ending sessions does, see
  [session lifetimes and re-authentication](security.md#session-lifetimes-and-re-authentication).
- **Not on oneself:** an administrator cannot disable themselves or remove their own administrator role, so a tenant
  never loses its last administrator from the page.

## Accounts

_(planned for M17)_

- **Invitation:** an administrator invites a person by email and display name, and may give roles at once. The realm
  creates the account as `invited` and sends an email through the tenant's
  [mail settings](tenant-setting-commands.md#mail-settings), with a link that lasts 72 hours. Following it, the person
  sets a password and adds an authenticator app, and the account becomes `active`. An administrator can resend an
  invitation, which ends the old link.
- **No self-registration:** nobody creates an account without an invitation.
- **A second step:** every sign-in with an account asks for a 6-digit code from the authenticator app. A person who
  signs in through a brokered provider follows that provider's MFA policy instead, as
  [D99](../decisions/d99-axis-ships-its-own-identity-provider.md) agrees.
- **Passwords:** at least 12 and at most 128 characters, with no rules on which characters, and not on the realm's list
  of breached passwords. "Forgot password?" sends a link by email that lasts 30 minutes.
- **Failed sign-ins:** after 5 failed sign-ins in a row the account waits before the next try, starting at 1 minute
  and doubling up to 15 minutes. The sign-in page never says whether the account exists.
- **Reset authenticator:** removes the account's authenticator, so its next sign-in asks the person to add one again,
  and ends the person's sessions.

## Disabled users

_(planned for M17)_

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

_(planned for M17)_

- The server changes accounts through the realm's users client, which has rights over the realm's users only. It
  cannot change the realm's providers, clients or settings. Its secret is stored encrypted in the tenant directory,
  see [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step).

## Audit

_(planned for M17)_

- Each command writes one audit record in the tenant database, with the administrator as actor and the user it acts
  on: `user.invited`, `user.invitationResent`, `user.disabled`, `user.enabled`, `user.authenticatorReset`,
  `user.roleGiven` and `user.roleRemoved`. A role record names the application or the platform, and the role. No
  record holds a password, a code or an invitation link.
