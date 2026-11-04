# Control plane

Detailed reference for the control plane: the service that runs Axis's own business around the
[cells](cells.md), which is sign-up, trials, plans, payments and invoices. It follows
[D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md),
[D126](../decisions/d126-sign-up-trials-and-the-control-plane.md) and
[D127](../decisions/d127-plans-payments-and-invoices.md). Everything in this file is _(planned for
M33)_, except the parts that billing adds, _(planned for M34)_, and except the service itself, its
settings, its health, its [durable jobs](#its-own-work), its audit records, its image and its Helm chart, which
are built: `controlplane/src/Axis.ControlPlane` starts, checks its `ControlPlane:` settings, applies its own
migrations, runs its job queue and answers `/health/live` and `/health/ready`, the `axis-controlplane` image
packs it, and `deploy/helm/axis-controlplane` installs it. No job kind exists yet, so the job runner is idle
until the issues that add provisioning, the subscription clock and emails register their handlers. The
conventions of [architecture.md](../architecture.md) apply.

## What it is

- **A service of its own, not an Axis application.** The control plane is a .NET 10 service in the
  repository under `controlplane/`, with the same libraries, analyzers and test setup as the platform:
  real PostgreSQL through Testcontainers for its storage, and Playwright for its pages.
- **Apart from the cells.** It runs on a small Kubernetes cluster of its own, with its own managed
  PostgreSQL, in the region it serves. It is never deployed in a cell, and a cell never calls it. So a
  failing cell never stops sign-up or billing, and billing code never runs beside a customer's data.
- **No engine.** It holds no application, process or record of a customer. It reaches a tenant only
  through the cell's [provisioning API](tenant-lifecycle.md#provisioning-api).
- **Its own Helm chart and image.** `deploy/helm/axis-controlplane` and the image
  `axis-controlplane`, built, scanned and versioned with the platform's images.

## Two parts

| Part | Where it runs | What it holds |
| --- | --- | --- |
| The [region directory](region-directory.md) | One instance for all regions | Which cell holds each tenant and host. No personal data. |
| The regional control plane | One instance in each region, with its data in that region | Sign-up, [billing portal](billing-portal.md) accounts, [subscriptions](subscriptions.md), [plans](plans.md), [payments](payments.md), [e-invoices](e-invoices.md), terms acceptances, and the emails it sends |

- **Data stays in its region.** A person who signs up in `vn` gives their name, email and billing
  details to the `vn` control plane, and nothing of it leaves the region, as D118 asks. The directory
  holds tenant ids, hosts and cell ids only.
- **Where the directory runs.** Beside the first region's control plane, in its own database. It
  holds no personal data, so its place does not change where customer data is kept.

## Reaching the cells

- **One client for each cell.** The cell's [operator realm](cell-console.md#sign-in) has a client for
  the control plane, with the client credentials grant and the role `Cell:OperatorRole`. Its secret is
  a Kubernetes Secret of the control plane, never a setting.
- **Private network.** The control plane calls `Cell:InternalUrl`, never the public console host. The
  cluster of the control plane and the cells of its region share a private network, and the calls use
  mutual TLS: the cell accepts on its internal listener only a client certificate that the region's
  internal certificate authority issued to the control plane.
- **Idempotent.** Every `POST` to a cell carries an `Idempotency-Key` that the control plane derives
  from its own record, such as the sign-up id, so a retried call never makes a second tenant.
- **Jobs.** The control plane polls a cell job until it ends, see [jobs](tenant-lifecycle.md#provisioning-api).

## Its own work

- **Durable jobs.** Provisioning a tenant, the subscription clock, emails, invoice issuing and the
  daily reconciliation run as jobs in the control plane's database. They are built on the queue
  below, which follows the [publisher](administration.md#publisher) in claiming with a lease.
  - **The `control_plane.jobs` table.** `id`, `kind`, `payload` (JSON), `state` (`queued`,
    `running`, `succeeded` or `failed`), `attempts`, `next_attempt_at`, `lease_owner`,
    `lease_until`, `last_error`, `created_at` and `finished_at`. Code enqueues a job of a kind with
    a payload, and may do it in the transaction of the change that needs it. A job that ends
    `succeeded` or `failed` stays in the table with its last error.
  - **Claim.** Each replica runs one job runner, which runs one job at a time. It claims the
    oldest due job with `FOR UPDATE SKIP LOCKED`, and only a kind it has a handler for, so an older
    replica never claims a kind it cannot run. A job is due when it is `queued` and its
    `next_attempt_at` has passed, or when it is `running` and its lease has ended. Times are the
    database clock's. A claim counts one attempt, so a runner that stops in the middle of a job still
    uses up an attempt.
  - **Lease.** The lease lasts 5 minutes. The runner renews it every third of its length while the
    handler runs, on a connection of its own. Every update after the claim names the attempt count,
    so a runner that lost its claim changes nothing. A job whose runner stopped is claimed again
    once its lease has ended, and the runner polls every second when nothing is due.
  - **Retries.** A handler that throws gets the job queued again after 30 s × 2^(attempt − 1), at
    most 1 hour, with no jitter. After 10 failed attempts the job is `failed`. An error log
    `Job {JobId} of kind {JobKind} failed after {Attempts} attempts.` names it, and the counter
    `axis.controlplane.jobs.failed` counts it. A job claimed an 11th time, because its runner
    stopped on the 10th attempt, is `failed` the same way without running. These timings are code
    defaults and not `ControlPlane:` settings.
  - **At-least-once.** A runner that stops after the handler's side effect but before it marks the
    job done causes a second run, so a handler must be idempotent. The cell's `Idempotency-Key`
    makes the calls to a cell safe.
  - **No payload in a record of failure.** `last_error` holds the exception type and the start of
    its message, never the payload.
- **Emails.** The control plane sends its own emails, from a mail domain of its own,
  `ControlPlane:MailFrom`, with SPF, DKIM and DMARC. It never uses a cell's mail domain, so one
  tenant's sending never touches a sign-up or a payment reminder. Every email is in `en` and `vi`,
  in the language the person chose at sign-up.
- **Audit.** Every change to a subscription, a payment, an invoice or an account writes an
  append-only audit record with its actor: a person of the [billing portal](billing-portal.md), a
  provider's webhook, a job, or an operator.
  - **The `control_plane.audit_records` table.** `id`, `occurred_at`, `actor_kind` (`account`,
    `webhook`, `job` or `operator`), `actor`, `action` and `subject`. The database rejects every
    update, delete and truncate with `restrict_violation`, raw SQL included, and the control plane's
    code has only an append and a read.
  - **Who writes it.** The handler or the command that makes the change appends the record in the
    same transaction as the change. A job's record has the actor kind `job` and the job id as the
    actor. The job runner writes none itself: claiming and retrying are bookkeeping and not a
    business change.

## Operators

- **Who.** The platform operator, who runs the cells, runs the control plane too.
- **Commands.** `Axis.ControlPlane.Admin` holds the operator's commands: extend a trial, record a
  bank transfer that arrived without a payment link, issue a refund, resend an email, adjust an
  e-invoice, and list a region's sign-ups and subscriptions. Each writes an audit record. There is no
  operator web page in M33 or M34.
- **Settings.** `ControlPlane:` settings hold the region, its domain, its cells' internal addresses,
  the mail settings, the [plans](plans.md) and the providers' non-secret values. Secrets are
  Kubernetes Secrets.

The service reads three settings at start, and the rest are _(planned for M33)_. Every one is required.
A missing or wrong value stops the start, and the message names the setting and never its value. The
connection string may also come from the key-per-file secret `/run/secrets/ControlPlane__ConnectionString`.

| Setting | Meaning | Start check |
| --- | --- | --- |
| `ControlPlane:Region` | The region code, such as `vn` | Lowercase letters and digits only |
| `ControlPlane:TenantDomain` | The domain of the region's tenant subdomains, such as `vn.example.com` | A valid domain name |
| `ControlPlane:ConnectionString` | The connection to the control plane's own PostgreSQL | A valid Npgsql connection string. Outside Development and Testing it must set `SSL Mode=VerifyFull` |

At start the service applies its migrations to the `control_plane` schema, with the history table
`control_plane.__control_plane_migrations`. A failed migration stops the start. One database role runs
the migrations and the queries for now.

## Image and chart

- **Image.** Build it with
  `docker build --target axis-controlplane --build-arg AXIS_VERSION=<version> .`. It runs as the
  non-root user of the base image in the Production environment, works on a read-only root file system,
  carries the version as the OCI label `org.opencontainers.image.version`, and holds no sample,
  extension package or module.
- **Chart.** `deploy/helm/axis-controlplane` installs one Deployment and one ClusterIP Service. The
  Deployment has startup, liveness and readiness probes on `/health/live` and `/health/ready`.
- **HTTPS only.** The control plane listens on port 8443 with the certificate of a Kubernetes TLS Secret
  (`secrets.certificate`), and the Service forwards its port 443 to it. The probes use HTTPS.
- **Values.** `controlPlane.region` and `controlPlane.tenantDomain` set the settings of the same name.
  `secrets.connectionString` names the Kubernetes Secret, and optionally its key, that holds the
  connection string. The chart mounts it as the file `/run/secrets/ControlPlane__ConnectionString`.
  `image`, `replicas`, `resources` and `service` are the usual. The chart holds no secret value and
  creates no Secret, and its schema rejects a `value` in a secret reference.
- **Render failures.** Rendering fails and names the value when `controlPlane.region`,
  `controlPlane.tenantDomain`, `secrets.connectionString.name` or `secrets.certificate.name` is not set.
- **Checks.** `scripts/lint.sh` lints the chart and checks its rendered manifests against the Kubernetes
  schemas, `scripts/helm-controlplane.test.mjs` tests the render, the image scan covers the image, and
  the `images` smoke suite starts it read-only against PostgreSQL over TLS.

## Health and telemetry

- **Health.** `/health/live` and `/health/ready`, as the platform's hosts answer them. Live runs no
  check. Ready checks its database, which must answer `SELECT 1`, and answers 503 `Unhealthy` when it
  does not. The directory and the mail relay _(planned for M33)_ join the ready check when they exist.
- **Telemetry.** Logs, metrics and traces as the platform's hosts write them, under the service name
  `axis-controlplane`, and the platform's security headers on every response. A log never holds a
  payment card, a bank account, a tax code or a webhook body.
- **Metrics.** Next to the standard metrics, the service counts the jobs that ended `failed`.

  | Metric | Instrument | Unit | Attributes |
  | --- | --- | --- | --- |
  | `axis.controlplane.jobs.failed` | counter | `{job}` | `axis.job.kind`. Never the job id |
