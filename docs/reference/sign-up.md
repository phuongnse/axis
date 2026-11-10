# Sign-up

A person signs up for Axis SaaS on a page of the regional [control plane](control-plane.md) and gets
a tenant of their own, with a subdomain and a trial. This page covers the sign-up page, the choice of a
subdomain, the defences against bots, the email check, the provisioning of the tenant and the record of
the terms the person accepts. The operator can also create a tenant
through the [provisioning API](tenant-lifecycle.md#creating-a-tenant).

## The page

The sign-up page asks for who the person is, the business, its subdomain and what to start from, and
sends nothing outside the region.

- **Where.** `https://account.<region domain>/sign-up`, on the regional control plane, which serves the
  page from its web root. Axis's public site links each region's page, so the person picks the region
  by the link they follow, and the form never sends their data to another region.
- **No third party.** The page loads no script, font or image from another host. Its
  Content-Security-Policy allows `'self'` only, as the platform's
  [security headers](headers-and-rate-limits.md#security-headers) do.
- **One frame for every sign-up page**. Each page of the journey, from the form to
  the progress page, draws `SignUpFrame`: the [card page](shared-widgets.md#which-widget) with the
  shared "Language and theme" menu and the [legal line](legal.md#legal-line) under the card. The card is
  as wide as the `small` dialog, so a long form needs height and not width, and the page draws no frame
  of its own. The legal line's Privacy, Terms and Licences addresses come from
  [the region's documents](#the-documents).
- **Fields**. Each is an [`AxisField`](actions-and-controls.md#fields) with its help,
  in the order of the person's thinking: the person first, then the business's short fields together,
  and the choice that needs most thought last.
  1. The person's full name.
  2. The person's work email.
  3. The business's name.
  4. The subdomain, see [the subdomain](#the-subdomain).
  5. The language, `en` or `vi`, which the tenant and every email use. It starts at the page's language
     and follows it until the person chooses one.
  6. Start from, see [start from](#start-from).
  7. The box that accepts the terms, see [the terms box](#the-terms-box).
  8. The privacy notice under the box.
- **Required fields**. Full name, work email, business name, subdomain and language
  carry the shared required mark and show a message when empty. Start from and the terms box are not
  marked, because each always has a value or a check of its own. The sign-up pages mark their required
  fields, as every platform form does.
- **Languages.** The page is in `en` and `vi`. It starts in `vi` for a Vietnamese browser and in `en`
  otherwise, and the shared "Language and theme" menu switches between them.
- **No trial told on the page** _(planned for M33)_. No text of the page says whether an email already
  had a trial, see [one trial for each email](#bots-and-abuse).
- **Sign up** _(planned for M33)_. The finish button Sign up, as wide as the card, ends the form. A click
  checks every field, an email's format included, focuses the first that is wrong and sends nothing,
  and sends nothing while the subdomain is refused or still being checked. The button is busy while the
  sign-up is sent and is never off because the form is wrong. A success goes on to
  [check your inbox](#verifying-the-email-first).
- **A refusal shows where the person can put it right**. A refusal of a field shows
  under that field, which the page scrolls to and focuses. Any other refusal shows in an error
  `AxisAlert` above the form, as the form widget's does, and the page scrolls to it and focuses it, so a
  person who pressed Sign up at the foot of the long form sees it. A refused document version unticks
  the terms box, loads the documents again and asks for the box again.

### Start from

The person chooses what the new tenant starts from, because the platform does not require an
application, as [D51](../decisions/d51-creating-applications-and-resources-from-studio.md) starts one blank
or from a template.

- **The choices**. `StartFromField` offers Blank, chosen first, then the region's
  built-in applications, each with its description. It is the field that Studio's
  [New application](studio.md#new-application) draws too. When the list does not load, the field's
  warning status says under the choices that Blank is still there.
- **Applications.** The region's list is the setting `ControlPlane:SignUp:Applications`, see the
  [settings](control-plane.md#settings), and `GET /api/sign-up/applications` serves it as
  `{ applications: [{ id, titles: { en, vi }, descriptions: { en, vi } }] }`, from the settings
  `ControlPlane:SignUp:Applications:<n>:Titles` and `:Descriptions`. The page shows each title in its language.
- **Blank**. The form sends `application: ""` for Blank. A tenant created without
  an application gets a blank one, see [creating a tenant](tenant-lifecycle.md#creating-a-tenant).

### The terms box

The box accepts the business's agreements.

- **The box**. `AcceptTermsField` reads "On behalf of my business, I accept the Terms
  of service and the Data processing agreement.", in `vi` "Tôi thay mặt doanh nghiệp đồng ý với Điều
  khoản dịch vụ và Thỏa thuận xử lý dữ liệu.". It names each document as a link, in the page's language,
  opening in a new tab, and it is never ticked at first. See
  [accepting the terms](legal.md#accepting-the-terms).
- **The privacy notice**. A line under the box, before sending, names the Privacy
  policy as a link and has no box: the person's name and email are used to run the contract, which needs
  no separate consent. A lawyer confirms this before sign-up opens. If Vietnamese law needs an explicit
  consent, it is a box of its own, never joined to the terms.

### The documents

`GET /api/sign-up/documents` answers `{ terms, dpa, privacy, licencesUrl }`, where each document is
`{ version, urls: { en, vi } }`, from the settings `ControlPlane:SignUp:Terms`, `:Dpa`, `:Privacy` and
`:LicencesUrl`, see the [settings](control-plane.md#settings). Without them the route answers `503`, and so
does `POST /api/sign-up`.

- **The page links the documents**. The page links each document in its language,
  and the legal line takes its Privacy, Terms and Licences addresses from the answer.
- **The versions are sent**. `POST /api/sign-up` sends `termsVersion`, `dpaVersion` and `privacyVersion`,
  the versions shown. The control plane refuses a missing version, or one that is not the region's
  current version exactly, and stores the three on the pending sign-up. When the documents did not load,
  the page sends none, and the refusal loads them again.

### Sending the form

_(planned for M33)_

`POST /api/sign-up` takes `fullName`, `email`, `businessName`, `subdomain`, `language`, `application`,
the three document versions and the `altcha` solution of the [proof of work](#bots-and-abuse), and
answers `202` with `{ resend }` when it is accepted, see
[verifying the email first](#verifying-the-email-first). A refusal is a problem whose `errors` are text
keys of the page by JSON Pointer, as the [record API](record-api.md)'s are:

- `/altcha` for a solution that is missing, malformed, wrong, expired or already used. One answer
  covers all five, so a bot learns nothing about which check failed. The page shows it in the error
  alert above the form, in the texts below, and its next click solves a new challenge.
- `/email` for a disposable domain, and `/subdomain` for a name that is not free. A disposable domain is
  `signUp.email.disposable`, which the page shows under the Work email field: "This email is from a
  disposable mailbox service. Enter your work email.", in `vi` "Không thể đăng ký bằng hộp thư dùng tạm.
  Nhập email công việc."
- `/termsVersion`, `/dpaVersion` or `/privacyVersion` for a version that is missing or no longer current.
  The key is `signUp.terms.required`, and the page shows it under the terms box.
- Any other key for the form.

A limit answers `429` with `Retry-After` and `limit`: `address` for the client address, which the page
shows in minutes, or `domain` for the email's domain, which asks the person to try tomorrow. The
answer shows in the error alert above the form, in these texts:

| Limit | English | Vietnamese |
| --- | --- | --- |
| `address` | Too many sign-ups came from this network. Try again in {minutes} minutes. (`1 minute` for one) | Có quá nhiều lượt đăng ký từ mạng này. Thử lại sau {minutes} phút. |
| `domain` | Too many sign-ups came for this email's domain today. Try again tomorrow. | Hôm nay có quá nhiều lượt đăng ký cho tên miền của email này. Thử lại vào ngày mai. |

The `/altcha` refusal (`signUp.altcha.refused`) shows in the same alert, in these texts. They name
no bot check, as the [glossary](ui-glossary.json) has no term for it:

| Refusal | English | Vietnamese |
| --- | --- | --- |
| `/altcha` | The sign-up could not be checked. Try again. | Không kiểm tra được lượt đăng ký. Thử lại. |

The minutes are `Retry-After` in seconds, rounded up. A `429` without a usable `Retry-After` or with an
unknown `limit` shows the text of any other failure.

## The subdomain

The person chooses the subdomain, which becomes the tenant's id, and the page checks it as they type.

- **The person chooses.** The page suggests one from the business's name, in lower-case letters,
  digits and `-`, and the person may change it.
- **Checked as they type.** `GET /api/sign-up/subdomains/{name}` answers `{ free, reason }`. The name
  is not free when it breaks the rule below (`invalid`), when it is
  [reserved](region-directory.md#routing-a-host) (`reserved`), when a tenant has it or its host is in
  use (`taken`), or when it is held after a deletion or by a pending sign-up (`held`). `reason` is null
  when the name is free. The answer never says which tenant has it. Without the region directory the
  route answers `503`.
- **The status under the input**. The check's answer is the field's
  [status](actions-and-controls.md#fields): checking shows the input busy, a free name shows nothing but
  a status region for a screen reader, a refused name shows its message as an error, and a check that
  could not run shows a warning, all under the input.
- **The tenant domain.** `GET /api/sign-up/region` answers `{ tenantDomain }` from
  `ControlPlane:TenantDomain`, see the [settings](control-plane.md#settings).
- **Ends in the tenant domain**. The input shows the region's tenant domain after what
  the person types, in `textMuted`, such as `contoso-trading` then `.vn.example.com`, so the person reads
  the whole address as they type. A long subdomain scrolls inside the input. When the route fails, the
  input shows the subdomain alone, and its help still says it starts the address.
- **Held while pending.** A sign-up holds its subdomain for 24 hours, until it is verified or expires.
  The hold makes the name not free.
- **The rule.** The rule of a tenant id of [`tenant create`](administration.md#tenant-create), at
  most 40 characters.

## Bots and abuse

Sign-up is open to anyone, so a proof of work, rate limits and email checks make each abusive try
cost something.

- **Proof of work.** The form needs an ALTCHA solution. The control plane serves the challenge itself
  at `GET /api/sign-up/challenge`: an HMAC-signed challenge that expires after 10 minutes and is
  accepted once. The browser solves it while the person types, so a person barely waits and a bot
  pays for each try. No sign-up data goes to a bot-check service. The route answers
  `{ algorithm, challenge, maxnumber, salt, signature }`, signed with
  [`ControlPlane:SignUp:ChallengeKey`](control-plane.md#settings), and answers `503` without that key.
  `POST /api/sign-up` answers `503` without it too, as no solution can pass. The salt is random hex,
  then `?expires=` and the expiry in Unix seconds, then a closing `&`, and the route refuses any other
  shape, so a caller cannot move digits between the salt and the number to push the expiry out.
  The page sends the solution as `altcha` in the body of `POST /api/sign-up`. The route checks the
  solution before the limits and the form's fields, and refuses a missing, malformed, wrong, expired
  or used one under `/altcha`, see [sending the form](#sending-the-form). It stores each accepted
  solution in the control plane's database until it expires, so it works once across replicas, even
  when a limit or a field then refuses the try. The `expireSignUps` job deletes the rows that have
  expired.
- **Rate limits.** The control plane's `sign_up` partition allows 5 sign-ups an hour from one client
  address, and 20 a day for one email domain, except the public mailbox domains of the setting
  `ControlPlane:SignUp:PublicMailboxDomains`, such as `gmail.com`, which count by address only. Past a
  limit it answers `429` with `Retry-After` and `limit`, see [sending the form](#sending-the-form).
  - **Every try with an accepted solution counts**, even one the form refuses. A try without one is
    refused first and does not count. A refused try stores no sign-up and sends no email.
  - **By address at a public domain.** Each address of a public mailbox domain counts on its own,
    compared as the rule of one trial for each email, below, compares them.
  - **Per replica.** Each replica counts in memory as the platform's [rate limits](headers-and-rate-limits.md#rate-limits)
    do, so a restart starts the counts again.
  - **`Retry-After` is the window**, an hour or a day, not the time left in it.
  - **The client address** is read by the rules of the
    [forwarded headers](control-plane.md#settings), and each refusal counts in the
    [metric](headers-and-rate-limits.md#a-refused-request) `axis.rate_limits.refused` with the partition `sign_up`.
- **Three emails a day for an address.** _(planned for M33)_ Each email address, compared as below, gets at most 3 sign-up
  emails of either kind in 24 hours, resends included, so the form cannot fill someone else's inbox. A
  sign-up past that answers `202` and sends nothing, so the limit tells nothing either.
- **One trial for each email.** Each sign-up stores its email in lower case and without a `+tag`, so
  `Owner+Test@Example.com` is `owner@example.com`. A unique index lets only one sign-up of that address be
  verified. A second pending sign-up of it that is confirmed after the first answers `409` with
  `{ state: "confirmed" }` and starts no second trial. A sign-up that was never verified does not
  count.
- **The page never tells whether an email already had a trial.** An email that already verified a sign-up
  is answered as a new one, `202`, and the page goes on to check the inbox, so no one can learn from the
  page whether a business uses Axis. The control plane stores no sign-up for it and holds no
  subdomain. It sends that address the [trial-used email](#the-emails) instead of the confirm email,
  with a link to sign in to its Axis while the region directory holds its tenant, or to the
  [billing portal](billing-portal.md) otherwise. A subdomain that is taken is still
  told, as it is public in DNS.
- **Disposable domains.** An email of a domain on a list of disposable mailbox providers is refused.
  The list is the `control_plane.disposable_domains` table, whose domains are lower case. The domain of
  the email is compared in lower case, and a subdomain of a listed domain is refused too: with
  `mailinator.com` listed, `x@sub.mailinator.com` is refused and `x@notmailinator.com` is not. A bare
  top-level domain such as `com` never matches. With both `ControlPlane:SignUp:DisposableDomains` settings set, the
  job `refreshDisposableDomains` downloads the open-source list every 7 days and checks its SHA-256 against the pinned hash. On a match it
  replaces the table in one transaction. On a mismatch it keeps the table and logs an error. A failed download is retried by the job runner.
  A line is a domain, which the job lower-cases, and it skips blank lines and lines that start with `#`. Without the settings the table is not refreshed.

## Verifying the email first

_(planned for M33)_

No tenant exists until the person proves the email is theirs.

1. **Pending.** `POST /api/sign-up` checks the challenge, then the limits, then the form, stores a pending
   sign-up, and sends the confirm email with a link that holds a random 256-bit token after `#`. The
   control plane keeps only the token's hash. It answers `202` with `{ resend }`, an opaque token for
   Send again, and the answer is alike for a new email and for one that already had a trial.
2. **Verify.** The link opens `https://account.<region domain>/sign-up/verify#<token>`, which asks the
   person to confirm, so a mail scanner that follows links does not verify anyone. The token works once,
   for 24 hours.
3. **Then the tenant.** On confirm, the control plane records the terms acceptance, creates the
   person's [billing portal](billing-portal.md) account as the tenant's owner, starts the trial
   [subscription](subscriptions.md), and starts provisioning.

An unverified sign-up expires after 24 hours with nothing created, and its subdomain is free again.

### Check your inbox

Once the sign-up is accepted, the card's title becomes "Check your inbox", in `vi` "Kiểm tra hộp thư".
The card is the same for a new email and for one that already had a trial.

- **What it says** _(planned for M33)_. It names the address the email went to, says to look in the spam folder, wait a
  minute or send it again, and offers Send again, a secondary button, and Change email, a plain one. The
  focus moves into the new step.
- **Change email** brings the form back with every value kept and the focus on the email.
- **Send again** _(planned for M33)_. `POST /api/sign-up/resend` takes `{ resend }` and answers `202`, alike for every
  token, and a resend counts toward the address's [three sign-up emails a day](#bots-and-abuse). A sent
  resend shows a notice that the email was sent again in an info `AxisAlert`, and a failed one an error
  `AxisAlert`. Every error `AxisAlert` of the sign-up pages carries its icon, in the text colour of its
  kind.

### The verify page

The link of the confirm email opens `/sign-up/verify#<token>`, titled "Confirm your sign-up", in `vi`
"Xác nhận đăng ký".

- **It asks to confirm.** It checks the link first, with "Checking the link.", in `vi` "Đang kiểm tra liên kết.", then says "Confirm to
  start the trial for **{business}** at **{address}**. You sign in with **{email}**.", in `vi` "Xác nhận
  để bắt đầu dùng thử cho **{business}** tại **{address}**. Bạn sẽ đăng nhập bằng **{email}**.". The
  person sees what starts before anything is created. `FilledText` fills the sentence, so each language
  keeps its word order. Confirm is the finish button.
- **A failure keeps the page.** A check that fails shows "The link could not be checked. Try again.",
  in `vi` "Không kiểm tra được liên kết. Thử lại.", and offers Try again, in `vi` "Thử lại". A confirm
  that fails shows "The sign-up could not be confirmed. Try again.", in `vi` "Không xác nhận được đăng
  ký. Thử lại.", above the sentence and keeps Confirm.
- **Already confirmed.** A link already used says "Already confirmed", in `vi` "Đã xác nhận": "This
  sign-up is confirmed. An email tells you when you can sign in.", in `vi` "Đã xác nhận đăng ký này. Bạn
  sẽ nhận email khi có thể đăng nhập."
- **Expired.** An expired link says "This link has expired", in `vi` "Liên kết đã hết hạn": "A link works
  once, for 24 hours. Sign up again to get a new one.", in `vi` "Liên kết chỉ dùng được một lần, trong 24
  giờ. Hãy đăng ký lại để nhận liên kết mới.", with Sign up again, in `vi` "Đăng ký lại", as the finish
  button to `/sign-up`. An unknown token
  answers as an expired one, so the page tells nothing of a sign-up it does not hold.
- **The fragment.** A token or a secret travels after `#`, so no request line, server log or referrer
  carries it. The page reads the fragment and sends the value in a request body, or in a header for
  [the progress](#provisioning).
- **Checking.** `POST /api/sign-up/verification` takes `{ token }` and answers
  `{ state: "pending", email, businessName, address }`, `{ state: "confirmed" }` or
  `{ state: "expired" }`. It changes nothing.
- **Confirming.** `POST /api/sign-up/confirm` takes `{ token }` and answers `{ id, secret }` for the
  progress page. When the link was used or expired while the page was open, it answers `409` with
  `{ state }`, and the page shows that state, not an error. A link whose email another sign-up
  verified first answers `409` with `{ state: "confirmed" }`, see [one trial for each email](#bots-and-abuse).

### The emails

Each is plain text, as every control plane email is, in the sign-up's language and sent from the control
plane's mail domain. They are the templates `signUpConfirm.<language>.txt`,
`signUpTrialUsedSignIn.<language>.txt` and `signUpTrialUsedBillingPortal.<language>.txt`, see
[control plane](control-plane.md#its-own-work).
They follow [UI writing](ui-writing.md#principles): each names the address the person signs in at, or the
trial, never "your Axis".

The confirm email has `{{name}}`, the form's full name, `{{tenantAddress}}`, the tenant's address to
be, and `{{confirmLink}}`, the verify page with its token.

```
Subject: Confirm your Axis sign-up

Hello {{name}},

Open this link to confirm your email and start the trial at {{tenantAddress}}:

{{confirmLink}}

The link works once, for 24 hours. If you did not sign up, ignore this email: the sign-up lapses.

The Axis team
```

```
Subject: Xác nhận đăng ký Axis

Chào {{name}},

Mở liên kết này để xác nhận email và bắt đầu dùng thử tại {{tenantAddress}}:

{{confirmLink}}

Liên kết chỉ dùng được một lần, trong 24 giờ. Nếu bạn không đăng ký, hãy bỏ qua email này: đăng ký sẽ tự hết hạn.

Đội ngũ Axis
```

The trial-used email goes to an address that already had a trial, instead of the confirm email. It is two
templates with the same text but one middle paragraph: `signUpTrialUsedSignIn` holds the sign-in one, with
`{{tenantAddress}}`, while the region directory holds the tenant, and `signUpTrialUsedBillingPortal` holds
the [billing portal](billing-portal.md) one otherwise, whose link is `{{billingPortalUrl}}`.

```
Subject: This email already had an Axis trial

Hello {{name}},

Someone, maybe you, tried to sign up for Axis with this email. It already had a trial, so no other one starts.

Sign in at:
{{tenantAddress}}

To go on after the trial, choose a plan in the billing portal:
{{billingPortalUrl}}

If this was not you, ignore this email.

The Axis team
```

```
Subject: Email này đã dùng thử Axis

Chào {{name}},

Có người, có thể là bạn, vừa đăng ký Axis bằng email này. Email này đã dùng thử, nên không có thêm lượt dùng thử nào.

Đăng nhập tại:
{{tenantAddress}}

Để tiếp tục sau khi hết dùng thử, hãy chọn gói dịch vụ tại trang thanh toán:
{{billingPortalUrl}}

Nếu không phải bạn, hãy bỏ qua email này.

Đội ngũ Axis
```

## Provisioning

_(planned for M33)_

The control plane provisions the tenant as one job, with the sign-up's id as the idempotency key.

1. Pick a cell, see [picking a cell](region-directory.md#picking-a-cell).
2. Call `POST /api/cell/tenants` on it with the tenant id, the built-in application, the person's
   email as the administrator, and the trial's quotas, see
   [creating a tenant](tenant-lifecycle.md#creating-a-tenant).
3. When the cell accepts, write the tenant and its host to the [directory](region-directory.md), and
   its `CNAME` record.
4. Poll the cell job until it ends. A failed job is run again, up to 3 times. After that the operator
   is alerted, and the person gets an email that the team is on it.
5. On success, email the person that the trial is ready, with the tenant's address. The identity
   provider sends its own invitation to set a password, as the `invite` step of the cell does.

- **Progress.** After Confirm, the page `/sign-up/progress#<id>.<secret>` follows the trial being set
  up. It needs no account, only the id and the secret that the confirm answered, which travel after `#`
  as [the verify page](#the-verify-page) says. When the person closes the page, the email still tells
  them.
  - **The contract.** `GET /api/sign-up/{id}` with the header `Sign-Up-Secret` answers
    `{ step: "verified" | "creating" | "ready" | "delayed", address }`, and `404` for an unknown id or a
    wrong secret, so the secret is in no request line.
  - **The steps.** A vertical `StepsBar` shows one step on each line: Confirmed, Setting up and Ready,
    in `vi` Đã xác nhận, Đang chuẩn bị and Sẵn sàng.
  - **While it is set up.** From the moment it opens, the page says "Setting up the trial. You can close
    this page: an email tells you when you can sign in.", in `vi` "Đang chuẩn bị bản dùng thử. Có thể
    đóng trang này: bạn sẽ nhận email khi có thể đăng nhập.", because it opens only after a Confirm that
    succeeded. The sentence is a status region, so a screen reader hears each change. The page asks
    every 2 seconds until the trial is ready, through `useFollowJob`.
  - **Ready.** The page is titled "The trial is ready" and says "The trial is ready at {address}. Open the
    invitation email to set your password, then sign in.", with the address as a link. It has no Sign in
    button, as the person has no password until the invitation.
  - **Delayed.** A set-up that needs more time, after the cell job's retries, shows an info `AxisAlert`,
    "The trial needs more time to set up. Our team is on it, and you do not need to do anything.", and
    the page keeps asking.
  - **Unknown.** An unknown id or a wrong secret is titled "The progress cannot be shown" and says only
    that an email follows.
- **What the person does next.** They follow the invitation, set their password and second factor,
  and open the application. The application's administrators are the people who hold
  `Users:AdministratorRole`, see [the administrator role](user-administration.md#the-administrator-role).

## Terms acceptance

Each acceptance of the terms of service and the data processing agreement is recorded and never
changed.

- **What is recorded.** For each acceptance, an append-only row of `control_plane.terms_acceptances`:
  the `account`, the `tenant_id`, the `document` (`terms` or `dpa`), its `version`, the
  `privacy_version`, the `accepted_at` time, the `client_address` and the browser's `user_agent`. The
  address and the user agent are empty when the request had none, and a user agent is cut to 512
  characters. [The confirm](#the-verify-page) writes the two rows in its transaction, with the
  versions that the pending sign-up stored. Triggers reject every update, delete and truncate of the
  table with `restrict_violation`, even from a superuser, and the control plane's role keeps only
  `SELECT` and `INSERT` on it.
- **The privacy policy is told, not accepted.** It has no row of its own. The acceptance row also holds
  the version of the Privacy policy that [the terms box](#the-terms-box) showed.
- **At sign-up.** A confirm that succeeds records one acceptance of each document. A confirm that is
  refused records none.
- **A new version** _(planned for M33)_. When a new version of either document is published, the
  [billing portal](billing-portal.md) asks the owner to accept it before any other action.
- **Versions.** Each document's version and its address in each language are settings of the control
  plane, and [the documents' route](#the-documents) serves them. A sign-up that sends a version that is no longer
  current is refused. The texts come from a lawyer before sign-up opens.
