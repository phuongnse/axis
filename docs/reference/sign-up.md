# Sign-up

Detailed reference for how a person signs up for Axis SaaS and gets a tenant of their own, served by
the regional [control plane](control-plane.md). It follows
[D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) and
[D126](../decisions/d126-sign-up-trials-and-the-control-plane.md). Everything in this file is
_(planned for M33)_. The operator can still create a tenant by hand through the
[provisioning API](tenant-lifecycle.md#creating-a-tenant). The conventions of
[architecture.md](../architecture.md) apply.

## The page

- **Where.** `https://account.<region domain>/sign-up`, on the regional control plane. Axis's public
  site links each region's page, so the person picks the region by the link they follow, and the form
  never sends their data to another region.
- **No third party.** The page loads no script, font or image from another host. Its
  Content-Security-Policy allows `'self'` only, as the platform's
  [security headers](headers-and-rate-limits.md#security-headers) do.
- **Fields:**
  - the person's full name and work email;
  - the business's name;
  - the subdomain, see [the subdomain](#the-subdomain);
  - the built-in application to start from, from the region's list, such as
    [approvals](built-in-modules.md#the-approvals-application);
  - the language, `en` or `vi`, which the tenant and every email use;
  - a box to accept the terms of service and the data processing agreement, each linked at its
    current version, see [terms acceptance](#terms-acceptance).
- **Languages.** The page is in `en` and `vi`, from the browser's language, with a switch.

## The subdomain

- **The person chooses.** The page suggests one from the business's name, in lower-case letters,
  digits and `-`, and the person may change it.
- **Checked as they type.** `GET /api/sign-up/subdomains/{name}` answers whether it is free. It is
  not when a tenant has it, when it is [reserved](region-directory.md#routing-a-host), when it is
  held after a deletion, or when a pending sign-up holds it. The answer never says which tenant has
  it.
- **Held while pending.** A sign-up holds its subdomain for 24 hours, until it is verified or
  expires.
- **The rule.** The rule of a tenant id of [`tenant create`](administration.md#tenant-create), at
  most 40 characters.

## Bots and abuse

- **Proof of work.** The form needs an ALTCHA solution. The control plane serves the challenge itself
  at `GET /api/sign-up/challenge`: an HMAC-signed challenge that expires after 10 minutes and is
  accepted once. The browser solves it while the person types, so a person barely waits and a bot
  pays for each try. No sign-up data goes to a bot-check service.
- **Rate limits.** The control plane's `sign_up` partition allows 5 sign-ups an hour from one client
  address, and 20 a day for one email domain, except the public mailbox domains of a settings list,
  such as `gmail.com`, which count by address only. Past a limit it answers `429` with
  `Retry-After`.
- **One trial for each email.** A verified email gets one trial. The address is compared in lower
  case and without a `+tag`.
- **Disposable domains.** An email of a domain on a list of disposable mailbox providers is refused.
  The control plane refreshes the list each week from an open-source list, pinned by its hash.

## Verifying the email first

No tenant exists until the person proves the email is theirs.

1. **Pending.** `POST /api/sign-up` checks the form, the challenge and the limits, stores a pending
   sign-up, and sends an email with a link that holds a random 256-bit token. The control plane keeps
   only the token's hash. The page says to check the inbox.
2. **Verify.** The link opens `https://account.<region domain>/sign-up/verify`, which asks the person
   to confirm, so a mail scanner that follows links does not verify anyone. The token works once,
   for 24 hours.
3. **Then the tenant.** On confirm, the control plane records the terms acceptance, creates the
   person's [billing portal](billing-portal.md) account as the tenant's owner, starts the trial
   [subscription](subscriptions.md), and starts provisioning.

An unverified sign-up expires after 24 hours with nothing created, and its subdomain is free again.

## Provisioning

The control plane provisions the tenant as one job, with the sign-up's id as the idempotency key.

1. Pick a cell, see [picking a cell](region-directory.md#picking-a-cell).
2. Write the tenant and its host to the [directory](region-directory.md), and its `CNAME` record.
3. Call `POST /api/cell/tenants` with the tenant id, the built-in application, the person's email as
   the administrator, and the trial's quotas, see [creating a tenant](tenant-lifecycle.md#creating-a-tenant).
4. Poll the cell job until it ends. A failed job is run again, up to 3 times. After that the operator
   is alerted, and the person gets an email that the team is on it.
5. On success, email the person that their Axis is ready, with the tenant's address. The identity
   provider sends its own invitation to set a password, as the `invite` step of the cell does.

- **Progress.** After the confirm, the page polls `GET /api/sign-up/{id}` every 2 seconds and shows
  the steps: verified, creating, ready. It needs no account, only the id and a secret that the
  confirm put in the page. When the person closes the page, the email still tells them.
- **What the person does next.** They follow the invitation, set their password and second factor,
  and open the application. The application's administrators are the people who hold
  `Users:AdministratorRole`, see [the administrator role](user-administration.md#the-administrator-role).

## Terms acceptance

- **What is recorded.** For each acceptance, an append-only row: the account, the tenant, the
  document (`terms` or `dpa`), its version, the time, the client address and the browser's user
  agent. A row is never changed or deleted while the account exists.
- **When.** At sign-up, and again when a new version of either document is published. The
  [billing portal](billing-portal.md) then asks the owner to accept it before any other action.
- **Versions.** Each document's version and text are settings of the control plane, per language.
  The texts come from a lawyer before sign-up opens, as D118 says.
