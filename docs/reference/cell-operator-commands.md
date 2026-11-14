# Cell operator commands

This page describes the two `Axis.Admin` commands that give a cell its operator: `cell operator invite`
and `cell operator client add`. They run only in a cell, see [administration](administration.md) for
the other commands of the host.

## Cell operator invite

`cell operator invite <email>` gives a new cell its first operator, see
[cell console sign-in](cell-console.md#sign-in). It runs only in a cell, where `Cell:Id` is set, and
changes nothing outside one.

- **Realm.** It makes the realm `Cell:OperatorRealm`, or brings an existing one up to date. The realm
  has the account rules of a tenant's realm: the password policy, the lockout, the Axis login theme
  and an authenticator code at every sign-in. It has no brokered identity provider, so the command
  removes any that it finds. It makes no client. It has the realm role `Cell:OperatorRole`.
- **Account.** It finds the account with the email or creates it with the required actions to set a
  password and an authenticator, and gives it the role.
- **Invitation.** The realm emails the link through the host's `Email` settings. An account that has
  set a password gets no new email. The link names no client, so the realm's built-in `account`
  client handles it. Without `Email` settings the realm has no mail server, so the `invitation` step
  fails and the run is repeated once mail is set up.
- **Settings.** `ConnectionStrings:PlatformSchema`, the `IdentityProvider` settings and the `Email`
  settings. It needs no `Platform:SecretKey`, no `Admin:ConnectionString` and no `Tenancy:Source`
  beyond the cell's.
- **Output.** Each line starts with the realm name, then the step `realm`, `account` or `invitation`,
  then `ok` or `failed: <message>`. The `invitation` line says `skipped` when the account has a
  password. A failed `audit` line also starts with the realm name. No line holds the email.
- **Exit codes.** `0` when every step succeeded, `1` when a step failed, and `2` for a bad email, a
  missing `Cell:Id` or a missing setting, with nothing changed.
- **Audit.** Each run writes one platform audit record with no tenant, before any change. It lists
  `realm: <name>` and `role: <name>` and never the email.

## Cell operator client add

`cell operator client add <name>` makes the client that a machine caller, such as the control plane,
uses to call the cell, see [machine callers](cell-console.md#sign-in). It runs only in a cell, where
`Cell:Id` is set, and changes nothing outside one. Run `cell operator invite` first, because this
command makes neither the realm nor the role.

- **Client.** A confidential client of `Cell:OperatorRealm` with the name as its client id. It signs
  in with the client credentials grant only, and its service account holds `Cell:OperatorRole`. A
  second run keeps the client, resets its settings and gives the role again if it was removed. A
  client that already has the name is taken over this way.
- **Secret.** The command makes the secret when it makes the client, prints it once and never
  stores it. A second run prints no secret and keeps the one the client has. An operator who lost
  the secret makes a new one in the realm's admin console.
- **Name.** Lowercase letters and digits joined by single hyphens, at most 64 characters. The
  command refuses `Cell:ConsoleClientId` and the built-in clients of a realm: `account`,
  `account-console`, `admin-cli`, `broker`, `realm-management` and `security-admin-console`.
- **Settings.** `ConnectionStrings:PlatformSchema` and the `IdentityProvider` settings. It needs no
  `Email` settings.
- **Output.** `<realm> client ok` or `<realm> client failed: <message>`. A missing realm or role
  fails the step and tells the operator to run `cell operator invite` first. When the run makes the
  client, the next line is `<realm> client-secret <secret>`. A failed `audit` line also starts
  with the realm name.
- **Exit codes.** `0` when the step succeeded, `1` when it failed, and `2` for a missing or invalid
  name, a refused name, a missing `Cell:Id` or a missing setting, with nothing changed.
- **Audit.** Each run writes one platform audit record with no tenant, before any change. It lists
  `realm: <name>`, `role: <name>` and `client: <name>` and never the secret.
