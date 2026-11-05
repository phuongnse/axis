# Control plane

The control plane is the service that runs Axis's own business around the [cells](cells.md):
sign-up, trials, plans, payments and invoices. This page covers what the service is, how it reaches
the cells, the work it runs, its operators and settings, its image and chart, and its health. The
business it runs is on the pages of [sign-up](sign-up.md), [subscriptions](subscriptions.md),
[plans](plans.md), [payments](payments.md), [e-invoices](e-invoices.md) and the
[billing portal](billing-portal.md).

## What it is

The control plane is a .NET service of its own, apart from the cells, that reaches a tenant only
through a cell's API.

- **A service of its own, not an Axis application.** The control plane is a .NET 10 service in the
  repository under `controlplane/`, with the same libraries, analyzers and test setup as the platform:
  real PostgreSQL through Testcontainers for its storage, and Playwright for its pages.
- **Apart from the cells.** It runs on a small Kubernetes cluster of its own, with its own managed
  PostgreSQL, in the region it serves. It is never deployed in a cell, and a cell never calls it. So
  a failing cell never stops sign-up or billing, and billing code never runs beside a customer's data.
- **No engine.** It holds no application, process or record of a customer. It reaches a tenant only
  through the cell's [provisioning API](tenant-lifecycle.md#provisioning-api).
- **Its own Helm chart and image.** `deploy/helm/axis-controlplane` and the image
  `axis-controlplane`, built, scanned and versioned with the platform's images, see
  [image and chart](#image-and-chart).

## Two parts

_(planned for M33)_

The control plane has a directory for all regions and one regional part in each region, so that
personal data stays in its region.

| Part | Where it runs | What it holds |
| --- | --- | --- |
| The [region directory](region-directory.md) | One instance for all regions | Which cell holds each tenant and host. No personal data. |
| The regional control plane | One instance in each region, with its data in that region | Sign-up, [billing portal](billing-portal.md) accounts, [subscriptions](subscriptions.md), [plans](plans.md), [payments](payments.md), [e-invoices](e-invoices.md), terms acceptances, and the emails it sends |

- **Data stays in its region.** A person who signs up in `vn` gives their name, email and billing
  details to the `vn` control plane, and nothing of it leaves the region. The directory holds tenant
  ids, hosts and cell ids only.
- **Where the directory runs.** Beside the first region's control plane, in its own database. It
  holds no personal data, so its place does not change where customer data is kept.

## Reaching the cells

The control plane calls each cell of its region as a machine caller of the cell's provisioning API,
over a private network with mutual TLS.

- **One client for each cell.** The cell's [operator realm](cell-console.md#sign-in) has a client for
  the control plane, with the client credentials grant and the role `Cell:OperatorRole`. Its secret is
  a Kubernetes Secret of the control plane, never a setting. The control plane gets a token from the
  realm's `TokenUrl` with that grant, keeps it until one minute before it expires, and sends it as a
  bearer token. When the cell answers `401`, it drops the token, gets a new one and sends the call
  once more. A second `401` is an error.
- **Private network.** The control plane calls the cell's `InternalUrl` (the cell's
  `Cell:InternalUrl`), never the public console host. The cluster of the control plane and the cells
  of its region share a private network. Every call, the token request included, presents the control
  plane's client certificate, follows no redirect and goes over `https` only: a cell address that is
  not `https` stops the start. With `ControlPlane:CellCertificateAuthorityPath` the client trusts only
  a server certificate that chains to that authority, and checks no revocation list.
- **The cell's side.** The cell accepts on its
  [internal listener](tenant-lifecycle.md#provisioning-api) only a client certificate that the
  region's internal certificate authority issued to the control plane. A cell sets
  `Cell:InternalClientCa` for this, see [host settings](host-settings.md#cell-settings).
- **The chart's wiring** _(planned for M33)_. The Helm chart mounts the client certificate, the
  authority file and the cells' secrets, so that a region's control plane reaches its cells.
- **Idempotent.** Every `POST` to a cell carries an `Idempotency-Key` that the control plane derives
  from its own record, such as the sign-up id, so a retried call never makes a second tenant. The key
  is `<operation>:<record id>`, such as `tenant-create:3f2a…`, and a retry must send the same body,
  because the cell answers `422` for the same key with another body.
- **Jobs.** The control plane polls a cell job until it ends, see
  [jobs](tenant-lifecycle.md#provisioning-api). It reads `GET /api/cell/jobs/{id}` after 1 s, then
  after twice the wait each time up to 30 s, until the job is `succeeded` or `failed`, and returns its
  steps and error. Following a job for more than 30 minutes throws, and so does a failed call, so the
  job runner of the control plane retries the handler with the same key and finds the same cell job.
  These timings are code defaults and not `ControlPlane:` settings.

## Its own work

The control plane runs its work as durable jobs in its own database, sends its own emails, and audits
every business change.

- **Durable jobs.** Provisioning a tenant, the subscription clock, emails, invoice issuing and the
  daily reconciliation run as jobs in the control plane's database, on the queue below, which follows
  the [publisher](administration.md#publisher) in claiming with a lease.
  - **The `control_plane.jobs` table.** `id`, `kind`, `payload` (JSON), `state` (`queued`,
    `running`, `succeeded` or `failed`), `attempts`, `next_attempt_at`, `lease_owner`,
    `lease_until`, `last_error`, `created_at` and `finished_at`. Code enqueues a job of a kind with a
    payload, and may do it in the transaction of the change that needs it. A job that ends
    `succeeded` or `failed` stays in the table with its last error.
  - **Claim.** Each replica runs one job runner, which runs one job at a time. It claims the oldest
    due job with `FOR UPDATE SKIP LOCKED`, and only a kind it has a handler for, so an older replica
    never claims a kind it cannot run. A job is due when it is `queued` and its `next_attempt_at` has
    passed, or when it is `running` and its lease has ended. Times are the database clock's. A claim
    counts one attempt, so a runner that stops in the middle of a job still uses up an attempt.
  - **Lease.** The lease lasts 5 minutes. The runner renews it every third of its length while the
    handler runs, on a connection of its own. Every update after the claim names the attempt count,
    so a runner that lost its claim changes nothing. A job whose runner stopped is claimed again once
    its lease has ended, and the runner polls every second when nothing is due.
  - **Retries.** A handler that throws gets the job queued again after 30 s × 2^(attempt − 1), at
    most 1 hour, with no jitter. After 10 failed attempts the job is `failed`. An error log
    `Job {JobId} of kind {JobKind} failed after {Attempts} attempts.` names it, and the counter
    `axis.controlplane.jobs.failed` counts it. A job claimed an 11th time, because its runner stopped
    on the 10th attempt, is `failed` the same way without running. These timings are code defaults
    and not `ControlPlane:` settings.
  - **At-least-once.** A runner that stops after the handler's side effect but before it marks the
    job done causes a second run, so a handler must be idempotent. The cell's `Idempotency-Key` makes
    the calls to a cell safe.
  - **No payload in a record of failure.** `last_error` holds the exception type and the start of its
    message, never the payload.
- **Emails.** The control plane sends its own emails, from a mail domain of its own,
  `ControlPlane:MailFrom`, with SPF, DKIM and DMARC. It never uses a cell's mail domain, so one
  tenant's sending never touches a sign-up or a payment reminder. Every email is in `en` and `vi`, in
  the language the person chose at sign-up. SPF, DKIM and DMARC are DNS and relay setup, outside the
  repository.
  - **The `sendEmail` job.** Every email is one durable job of the kind `sendEmail`, so a restart
    loses none. Code queues it through one helper, `EmailJobs.EnqueueAsync`, which may run in the
    transaction of the change that needs the email. The payload is
    `{ template, language, to, values }`. The helper rejects an unknown template, a language other
    than `en` or `vi`, an address that is not an email address and a missing value, so a bad email
    fails when it is queued and not on the tenth retry. A payload is stored in the database, so it
    never holds a password, a token or a payment detail: an email that needs a link with a token needs
    its own design.
  - **Templates.** A template is a plain text file embedded in the service, named
    `<kind>.<language>.txt`, such as `tenantReady.vi.txt`. Its first line is `Subject: …`, a blank
    line follows, and the rest is the body. A placeholder is written `{{name}}`. The kinds are found
    from the files themselves, in `controlplane/src/Axis.ControlPlane/Emails/Templates`. At start every
    kind must have exactly an `en` text and a `vi` text, with the same placeholders in both, or the
    start stops and the message names the kind. The email is plain text, with no HTML part.
  - **Sending.** The job fills in the template in the payload's language and sends it through the
    relay of `ControlPlane:Mail`, from `ControlPlane:MailFrom`, never through a cell's mail settings.
    A failed send is retried on the job queue's normal backoff, see the retries above. `last_error`
    holds the type of the failure and never its message, because an SMTP rejection can quote the
    recipient's address.
  - **At-least-once, made visible.** The email's `Message-ID` is the job id and the domain of
    `ControlPlane:MailFrom`. A job that runs twice after a crash sends the same message twice, and the
    receiver can drop the copy.
- **Audit.** Every change to a subscription, a payment, an invoice or an account writes an
  append-only audit record with its actor: a person of the [billing portal](billing-portal.md), a
  provider's webhook, a job, or an operator.
  - **The `control_plane.audit_records` table.** `id`, `occurred_at`, `actor_kind` (`account`,
    `webhook`, `job` or `operator`), `actor`, `action` and `subject`. The database rejects every
    update, delete and truncate with `restrict_violation`, raw SQL included, and the control plane's
    code has only an append and a read.
  - **Who writes it.** The handler or the command that makes the change appends the record in the
    same transaction as the change. A job's record has the actor kind `job` and the job id as the
    actor. The job runner writes none itself, because claiming and retrying are bookkeeping and not a
    business change.

## Operators

The platform operator, who runs the cells, runs the control plane too, from the command line.

- **Commands** _(planned for M33)_. `Axis.ControlPlane.Admin` holds the operator's commands: extend
  a trial, resend an email, and list a region's sign-ups and subscriptions. Each writes an audit
  record.
- **Billing commands** _(planned for M34)_. `Axis.ControlPlane.Admin` also records a bank transfer
  that arrived without a payment link, issues a refund and adjusts an e-invoice. Each writes an audit
  record.
- **No web page.** The operator works through these commands, and the control plane has no operator
  web page.

## Settings

`ControlPlane:` settings hold the region, its domain, its cells' internal addresses and the mail
settings, and secrets are Kubernetes Secrets. The service reads them at start.

- **Required.** The region, the tenant domain, the connection string, the mail sender and the mail
  relay are required, except `ControlPlane:Mail:UserName`. The cell settings are optional, and once a
  cell is listed, all of its settings, its secret and the client certificate are required.
- **Refused.** A missing or wrong value stops the start, and the message names the setting, with the
  index of a cell, and never its value.
- **Secrets as files.** The connection string, the mail password and the cells' secrets may also come
  from the key-per-file secrets `/run/secrets/ControlPlane__ConnectionString`,
  `/run/secrets/ControlPlane__Mail__Password` and `/run/secrets/ControlPlane__CellSecrets__<cell id>`.
- **Plans and providers** _(planned for M34)_. `ControlPlane:` settings also hold the
  [plans](plans.md) and the payment and e-invoice providers' non-secret values.

| Setting | Meaning | Start check |
| --- | --- | --- |
| `ControlPlane:Region` | The region code, such as `vn` | Lowercase letters and digits only |
| `ControlPlane:TenantDomain` | The domain of the region's tenant subdomains, such as `vn.example.com` | A valid domain name |
| `ControlPlane:ConnectionString` | The connection to the control plane's own PostgreSQL | A valid Npgsql connection string. Outside Development and Testing it must set `SSL Mode=VerifyFull` |
| `ControlPlane:MailFrom` | The address every email of the control plane comes from | A valid email address |
| `ControlPlane:Mail:Host` | The host of the SMTP relay | Not empty |
| `ControlPlane:Mail:Port` | The SMTP port. Default 587 | 1 to 65535. Port 465 starts with TLS, any other port upgrades with STARTTLS |
| `ControlPlane:Mail:UseTls` | Whether the relay connection uses TLS. Default on | Outside Development and Testing it must be on |
| `ControlPlane:Mail:UserName` | The user name to sign in to the relay. Optional | When set, `ControlPlane:Mail:Password` is required |
| `ControlPlane:Mail:Password` | The password of the user name, from the secret `ControlPlane__Mail__Password` | Never a value in a setting file or a message |
| `ControlPlane:Cells:<n>:Id` | The cell id, such as `vn-1` | Lowercase letters and digits, joined by single hyphens, and unique |
| `ControlPlane:Cells:<n>:InternalUrl` | The address of the cell's internal listener | An absolute `https` URL |
| `ControlPlane:Cells:<n>:TokenUrl` | The token endpoint of the cell's operator realm | An absolute `https` URL |
| `ControlPlane:Cells:<n>:ClientId` | The id of the realm's client for the control plane | Not empty |
| `ControlPlane:CellSecrets:<cell id>` | The client secret of that cell's client, a key-per-file secret | Not empty for every listed cell |
| `ControlPlane:CellClientCertificate:Path` and `:KeyPath` | The PEM certificate and private key that the control plane presents to every cell | Both set and both files exist, when a cell is listed |
| `ControlPlane:CellCertificateAuthorityPath` | The PEM file of the region's internal certificate authority. Without it the system trust store decides | The file exists, when set |

At start the service applies its migrations to the `control_plane` schema, with the history table
`control_plane.__control_plane_migrations`. A failed migration stops the start. One database role
runs the migrations and the queries.

## Image and chart

The control plane ships as its own image and Helm chart, beside the platform's.

- **Image.** Build it with
  `docker build --target axis-controlplane --build-arg AXIS_VERSION=<version> .`. It runs as the
  non-root user of the base image in the Production environment, works on a read-only root file
  system, carries the version as the OCI label `org.opencontainers.image.version`, and holds no
  sample, extension package or module.
- **Chart.** `deploy/helm/axis-controlplane` installs one Deployment and one ClusterIP Service. The
  Deployment has startup, liveness and readiness probes on `/health/live` and `/health/ready`.
- **HTTPS only.** The control plane listens on port 8443 with the certificate of a Kubernetes TLS
  Secret (`secrets.certificate`), and the Service forwards its port 443 to it. The probes use HTTPS.
- **Values.** `controlPlane.region` and `controlPlane.tenantDomain` set the settings of the same name.
  `controlPlane.mailFrom` and `controlPlane.mail.{host,port,useTls,userName}` set
  `ControlPlane:MailFrom` and `ControlPlane:Mail`. `secrets.connectionString` names the Kubernetes
  Secret, and optionally its key, that holds the connection string, which the chart mounts as the file
  `/run/secrets/ControlPlane__ConnectionString`. `secrets.mailPassword` does the same for the relay
  password, as `/run/secrets/ControlPlane__Mail__Password`, and is left out when the relay needs no
  sign-in. `image`, `replicas`, `resources` and `service` are the usual. The chart holds no secret
  value and creates no Secret, and its schema rejects a `value` in a secret reference.
- **Render failures.** Rendering fails and names the value when `controlPlane.region`,
  `controlPlane.tenantDomain`, `controlPlane.mailFrom`, `controlPlane.mail.host`,
  `secrets.connectionString.name` or `secrets.certificate.name` is not set.
- **Checks.** `scripts/lint.sh` lints the chart and checks its rendered manifests against the
  Kubernetes schemas, `scripts/helm-controlplane.test.mjs` tests the render, the image scan covers the
  image, and the `images` smoke suite starts it read-only against PostgreSQL over TLS.

## Health and telemetry

The control plane reports its health and telemetry as the platform's hosts do.

- **Health.** `/health/live` and `/health/ready`, as the platform's hosts answer them. Live runs no
  check. Ready checks its database, which must answer `SELECT 1`, and the mail relay, which must
  accept a connection (the check signs in with nothing and sends no email). It answers 503
  `Unhealthy` when either does not, so a relay outage takes every replica out of readiness.
- **The directory in the ready check** _(planned for M33)_. Ready also checks the
  [region directory](region-directory.md).
- **Telemetry.** Logs, metrics and traces as the platform's hosts write them, under the service name
  `axis-controlplane`, and the platform's security headers on every response. A log never holds a
  payment card, a bank account, a tax code or a webhook body.
- **Metrics.** Next to the standard metrics, the service counts the jobs that ended `failed`.

  | Metric | Instrument | Unit | Attributes |
  | --- | --- | --- | --- |
  | `axis.controlplane.jobs.failed` | counter | `{job}` | `axis.job.kind`. Never the job id |
