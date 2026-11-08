# Testing

This page says how Axis is tested: the test levels, the rules that tests follow, the shared test services, the E2E stack and the purchasing system stub.

Every acceptance criterion maps to at least one of these levels. The commands that run them are in
[AGENTS.md](../AGENTS.md#commands), and how the runs are split and reported is in
[test runs and reports](development.md#test-runs-and-reports).

| Level         | Tooling                                                   | Scope                                                                                                                                                                                                                                                                                                                                                                       |
| ------------- | --------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Unit          | xUnit                                                     | Compiler, expression engine, policy evaluation, pure logic                                                                                                                                                                                                                                                                                                                  |
| Integration   | xUnit + Testcontainers PostgreSQL, PgBouncer and Keycloak | Storage, transactions, concurrency, tenancy isolation, engine recovery, the development realm                                                                                                                                                                                                                                                                               |
| Frontend unit | Vitest + Testing Library                                  | Shared components and metadata rendering                                                                                                                                                                                                                                                                                                                                    |
| End to end    | Playwright                                                | Real server + real database + SPA, user journeys and denied access                                                                                                                                                                                                                                                                                                          |
| Scenario      | Axis.Scenarios + Testcontainers PostgreSQL                | Configuration-level tests of an application. Record actions, process starts, task completions and the `expect` steps run, with `now` fixing the time. Stubs answer the calls to connectors and packages. Event deliveries run, so `async` event triggers start their processes. Schedule triggers run through `advanceTime`, which moves `now` and runs every due work item |

## Rules for tests

These rules keep the tests reliable, and `scripts/lint.sh` enforces the ones a machine can check.

- **Locators.** Playwright specs find elements by role, accessible name and ARIA state, and never
  select a class of the UI library. `scripts/lint.sh` fails when a file under `tests/` names a class
  that starts with `ant-`.
- **Exact names.** A string name or text passed to `getByRole`, `getByLabel`, `getByText` or
  `getByTitle` comes with `exact: true`, and only a pattern matches part of a name: a regular
  expression, `new RegExp(...)`, `fieldLabel(...)` or a `const` that holds one. The oxlint rule
  `axis/exact-locators` of Axis's plugin enforces this on `tests/e2e`, and `scripts/lint.sh` runs it. A
  name or options that the rule cannot see, such as a function parameter, need `exact: true` where
  the call shows it.
- **Selects.** Specs pick select options only through the helpers in `tests/e2e/select.ts`. The
  helpers never click an option, because a click can land without choosing it when the open list is
  placed again between the mouse going down and up. They type the option into an editable select,
  move to it with ArrowDown and press Enter.
  - `pickOption` makes a pick that did not land again, for up to 20 seconds, then fails, naming the
    select and the option.
  - `pickOptions` presses Enter only for an option that is not chosen yet, because Enter switches an
    option of a select that allows several on or off. It makes its picks again only when a step
    throws, for up to 20 seconds, then waits up to 5 seconds for each option to show as chosen, and
    does not pick again. A pick that did not land fails, naming the select and the option.
  - `pickAction` serves selects that act and then clear themselves.
- **Dialogs.** Specs reach a dialog only through `waitForDialog` and `confirmDialog` in
  `tests/e2e/dialogs.ts`. A dialog zooms in when it opens, and a click during the zoom is lost, so
  `waitForDialog` returns the dialog once it is fully opaque and its opening animation has ended.
  `confirmDialog` waits the same way before it clicks its button, and retries until the dialog
  closes. The same rule fails when a file under `tests/e2e/specs/` calls `getByRole('dialog')`
  itself.
- **Elapsed time.** Only `*PerformanceTests` classes may assert on elapsed time, because they run
  alone. Other tests prove a bound with a count, such as the steps used or the nodes inlined, and show
  that nothing waited through an outcome. `scripts/lint.sh` fails when a test file that is not named
  `*PerformanceTests.cs` uses `Stopwatch`, `.Elapsed` or `GetElapsedTime`. A polling deadline built on
  `DateTime.UtcNow` only ends a wait and is allowed.
- **Absent values.** A test that asserts a value is absent from a log entry or a message uses a
  marker that cannot appear there by chance. Record ids are UUIDv7 hex, so a short number or a hex
  word can match one: use a number of ten or more digits.
- **Policy violations.** Every spec imports `test` and `expect` from `tests/e2e/test.ts`. Its `test`
  has an automatic fixture that listens for `securitypolicyviolation` on every page of the test,
  including pages in browser contexts that the spec opens itself, and fails the test that caused a
  violation. `tests/e2e/policy-violations.test.ts` checks the listener, and fails when a spec imports
  `test` from `@playwright/test` instead.
- **Hang guard.** A step that once hung the browser until the test timeout can run through
  `guardStep` in `tests/e2e/hang-guard.ts`. It fails the step after its own limit, and attaches a
  screenshot and a text to the test: the page URL, the console output, the page errors and the failed
  requests since the step began, the load of the machine and its busiest processes. In
  `purchase-request-release.spec.ts` it wraps `editInStudio`, the edit of `texts/en.json` through
  Studio's JSON view in the journey from an edit to activating an earlier release, and
  `openStudio`, which opens Studio before a publish, each with a limit of 60 seconds. The other
  journeys of that spec write their changed files through the Studio resource API, without the
  browser.
- **Label texts.** A journey types a label's texts in the label's Translations… dialog, and keeps
  the key that Studio makes. It types a key into a text resource only for the keys that a label picks
  from a list, and for keys that no label uses.
- **Removals.** A removal deletes the thing and its tests, and adds no test of its own. A test checks
  what the code does now. A guard against a kind of mistake is a lint rule or a check of the current
  rule, such as "every chart import goes through `ChartDrawing`", never a list of removed names.

## Shared test services

The integration tests share their containers, so a run starts each service at most once.

- **Keycloak.** The integration tests share one Keycloak container with the development realm per
  run. It starts the first time a test needs it, so a run with no such test starts none, and it is
  removed when the run ends. A test class never starts its own Keycloak. A test that changes realm
  state must not use the shared container: it keeps its data in a PostgreSQL container of its own,
  with a read-only role that has `SELECT` on the `credential` table.
- **PostgreSQL.** The integration tests share one PostgreSQL 18 container per run, started and
  removed the same way. Each test class works in its own new database on that server, and every
  database a fixture creates has a name unique in the run. A fixture never starts its own container.
  Tenant isolation tests still use one tenant database per tenant. Tests that read
  `pg_stat_activity` filter on `current_database()`, so they neither see nor end other classes'
  sessions.
- **Scenarios.** `scripts/integration.sh` runs the scenarios of every application under
  `samples/apps/` from the integration tests, on their PostgreSQL container, see
  [test scenarios](reference/test-scenarios.md).
- **Doc facts.** `scripts/lint.sh` checks every relative Markdown link and `#anchor` in the
  repository. Unit tests in `Axis.Configuration.Tests` compare the diagnostic code table and the
  resource kinds in the [configuration pipeline](reference/configuration.md#configuration-pipeline)
  with `DiagnosticCodes`, `ResourceKinds` and the schema files. They find them by heading or table
  header, so the sections may move within `docs/`.

## The E2E stack

`scripts/e2e.sh` starts the whole stack that the Playwright journeys run against: a main server, a
journey server, the worker, the publisher, the purchasing system stub and the control plane.

The main E2E server activates these applications in `ActivateOnStartup`, see
[startup activation](reference/host-settings.md#startup-activation), so the journeys run against real
metadata and records:

| Application                                | For                                                                                                 |
| ------------------------------------------ | --------------------------------------------------------------------------------------------------- |
| `tests/e2e/fixtures/e2e-app`               | The platform journeys                                                                               |
| `samples/apps/purchase-requests`           | The purchase request journeys                                                                       |
| `samples/apps/leave-requests`              | The leave request journeys                                                                          |
| `samples/apps/service-desk`                | The service desk journeys                                                                           |
| `samples/apps/asset-management`            | The asset register and import journeys                                                              |
| `tests/e2e/fixtures/subprocess-app`        | The process inspector's sub-process journey: a parent that starts a child on a 24-hour timer, both without a subject |
| `tests/e2e/fixtures/host-site-app`         | The journey that opens a site at its own host's root                                                |
| `tests/e2e/fixtures/organization-app`      | The pages of `organization`, which it declares                                                      |
| `tests/e2e/fixtures/approvals-app`         | The pages of `approvals`, which it declares with `organization`                                     |

**Studio folders.** No spec writes a committed folder, so a spec that saves a resource in Studio never
changes a file under `samples/`.

- The main server's `Studio:Folders` lists only a copy of the purchase request sample, which
  `scripts/e2e.sh` makes in a temporary folder for each run, passes in `AXIS_E2E_STUDIO_FOLDER` and
  deletes on exit. The E2E settings blank the other entries of `appsettings.Development.json`. No spec
  writes to that copy.
- A spec that writes a Studio folder gives it a folder of its own. It copies the committed sample, or
  another folder it made, into a temporary folder, starts a server over it with `startServer`, and
  deletes the folder and stops the server when it ends. Tests in one file that change the same folder
  run in order, in one worker, under `test.describe.configure({ mode: 'default' })` or `serial`.
- No spec opens Studio on the main E2E server or names its folder, except `studio-new-application`,
  which only reads it to check that the main server offers no New application button.

The Studio specs use these folders:

| Specs                                                                                                                                                         | Folder                                                                                                                       |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `studio`, `studio-form-editor`, `studio-page-editor`, `studio-process-editor`, `studio-process-redraw`, `studio-scenario`, `studio-new-resource` and `studio-tests` | A temporary copy of `samples/apps/purchase-requests`, without `texts/vi.json` for `studio-new-resource`. `studio-tests` also copies it again for its server with `Studio:Tests` off |
| `studio-module-entity`                                                                                                                                        | A temporary copy of `samples/apps/leave-requests`                                                                            |
| `studio-module-pickers`                                                                                                                                       | A temporary copy of `tests/e2e/fixtures/module-app`, and a temporary `Modules:Path` with the sample modules and the fixture module `checklists` |
| `studio-new-application`, `studio-page-containers` and `studio-page-parameters`                                                                              | A temporary `Studio:ApplicationsPath`                                                                                        |
| `studio-entity-migrations` and `purchase-request-release`                                                                                                     | Folders under the test's output path                                                                                         |
| `service-desk-journey`, `asset-management-journey`, `approvals-application-journey` and `journey-tenant`                                                      | Each its own application under the journey server's applications folder, `AXIS_E2E_JOURNEY_APPLICATIONS`                     |
| `first-application-tutorial`                                                                                                                                  | Each its own application under the journey server's applications folder, `AXIS_E2E_JOURNEY_APPLICATIONS`                     |

**Activating another folder.** A spec can activate another folder during the run, to test releases.
The helper `tests/e2e/activate.ts` starts the built server once more on a spare port, with the same
settings as the E2E server and that folder in `ActivateOnStartup`, waits until `/health/ready`
answers, then stops that server. The E2E server reads the active release on each request, so it
serves the new release at once. The helper throws an error with the server output when the folder
does not activate. Only the spec that needs it uses `tests/e2e/fixtures/release-app`, so the other
specs see no change. A spec's own server activates `ActivateOnStartup__0` in every tenant, the
preview one included, so only `purchase-request-release` may leave it unset, and
`startup-activation.test.ts` checks this.

**The worker.** The E2E run starts the worker against the same database, with a 5 second lease and a
delivery limit of 2, so that a failing webhook becomes a dead letter after one 30 second backoff
instead of about 7.5 minutes. A spec can restart the worker: it writes the restart file that
`scripts/e2e.sh` names, then stops the worker process, and the script starts a new one. A worker that
stops with no restart request fails the run.

**The journey tenant.** The E2E run also serves a third tenant, `journey`, so that a Studio journey
builds a whole application and publishes it with no clash with the committed samples on the main
tenant.

- `scripts/e2e.sh` creates its database `axis_journey` and its roles `axis_journey_schema` and
  `axis_journey_runtime` through `psql`, so Docker Compose, `scripts/dev.sh` and `init-roles.sql`
  stay as they are.
- A second server serves it on the loopback address `127.0.0.2`, because the worker and the publisher
  reject two tenants that share a host. Playwright starts it after the main server, so the platform
  migrations of the two never race, and the two share the platform database.
- The journey server runs in the `Testing` environment, because `appsettings.Development.json` would
  add the `default` tenant, the startup activation and the Studio folders. It sets
  `Studio:ApplicationsPath` to a temporary folder, has no preview tenant or bindings, and sets
  `Console:OperatorRole` to `platform-operator`, so the operator console answers there.
- Only startup activation migrates a tenant database, so the journey server activates one
  application at start, `tests/e2e/fixtures/journey-base`, which holds only a manifest and clashes
  with no sample.
- The setup step signs in, on the journey host, the author, the requester, the service desk lead,
  the agent, the approver, the asset manager, the employee, the auditor, the department managers and
  the platform operator, and saves those sessions apart from the sessions of the main host.
- The worker and the publisher serve the journey tenant too, and the run fails when the worker log
  lacks `Tenant journey is ready for work` or the publisher log lacks `journey ready`.

**The tenant create spec** serves the tenant it creates. After `tenant create` it starts one more
server in `Testing` with `Tenancy:Source=directory`, which reads `operator-made` from the tenant
directory. The server listens on https at `AXIS_E2E_TENANT_HTTPS_PORT`, with a certificate that
`scripts/e2e.sh` makes for each run, and stops when the test ends. The spec's browser maps
`operator-made.localhost` on port 443 to that port and accepts the certificate, so a person signs in
on the tenant host with no port in the address. No redirect address is added by hand: only
`tenant create` writes them.

**The cell console spec** starts a server of its own in `Testing`, on the next port offset, with the
`Cell:*` settings of a cell, `Cell:ConsoleHost` set to `console.localhost` and the secret of the
`axis-cell-console` client. The spec's browser maps `console.localhost` and the tenant host on
port 443 to that server. The operator signs in through the `axis-operators` realm with a password and
a code, and a user of the tenant it creates is refused on the console host. Its server and publisher get the
`Cell:ExportStorage` settings of the MinIO that `scripts/e2e.sh` starts, and the spec exports a tenant and downloads
the ZIP after the warning. Both cell specs start their server, publisher and browser through `tests/e2e/cell.ts`.

**The cell quota journey** runs a cell on the port offset after the cell console spec's, with a server that reads the
tenant directory every 2 seconds, so a new tenant and a changed quota take effect within seconds. It signs in as an
operator account of its own in the `axis-operators` realm, because the cell console spec may sign the development
operator in at the same moment and Keycloak refuses one code twice in a period. The operator creates two tenants from
the built-in `approvals` application in the Create tenant dialog, and each first administrator gives themselves the
application's `admin` role and saves a department. No worker samples the directory tenants of the run, so the spec
writes each tenant's storage sample to `platform.tenant_usage_latest` from the size of its database, and the operator
sets one tenant's storage quota below it. That tenant's next save is refused with the quota title and the other tenant's
save goes through. The operator then exports the first tenant, downloads the ZIP after the warning and deletes the
tenant. The grace period is 7 days, so the spec moves the tenant's `delete_after` back and waits for the publisher's
purge job, after which the tenant's host answers `404`.

**The purchasing system.** The E2E run starts the [purchasing system stub](#purchasing-system-stub)
from the build output, and fails if it does not answer. Specs find it at `AXIS_E2E_PURCHASING_URL`.
The E2E server and worker run in the `Development` environment, because only Development lets a
connector call an `http` URL. Their `PurchasingSystemUrl`, `PurchasingSystemTokenUrl`,
`PurchasingSystemClientId` and `PurchasingSystemClientSecret` bindings point at the stub and hold its
default credentials, so an approved purchase request signs in and creates its purchase order there.
The E2E server, worker and publisher also hold the `PurchaseRequestApprovedWebhookUrl` and
`PurchaseRequestApprovedWebhookSecret` bindings, which point at the stub, where the sample's approval
webhook delivers.

**The control plane.** The E2E run starts the [control plane](reference/control-plane.md) from the build
output on `AXIS_E2E_CONTROL_PLANE_PORT` (default 5290), and fails if it does not answer. Specs find it at
`AXIS_E2E_CONTROL_PLANE_URL`. It runs in `Testing`, with its own database `axis_controlplane` and the region
directory's database `axis_directory`, both created through `psql`. It uses the `InMemory` DNS provider and sends
no mail. It serves the [sign-up page](reference/sign-up.md#the-page), and the sign-up spec seeds its own tenant in
the directory, so the subdomain check finds a taken name.

**Logs.** The worker output goes to `artifacts/logs/e2e-worker.log`, the journey server output to
`artifacts/logs/e2e-journey-server.log`, the stub output to `artifacts/logs/e2e-purchasing.log` and the control
plane output to `artifacts/logs/e2e-controlplane.log`.

## Journeys on the journey tenant

These journeys build or change an application on the journey tenant and use it on its site. Each spec
file holds its steps. This section says what each proves and what it must not do.

- **`service-desk-journey.spec.ts`** builds the whole service desk with Studio only, as the
  [product brief](product-brief.md#third-application-service-desk) asks. It never opens the JSON
  view and uses no file edit. It builds the data model, the data sources of the lists and the dashboard, the
  two-step ticket form on the form's Design tab, the forms of the tasks, the `TicketHandling` process
  with its escalations and its notify step, the pages, the site, the notify processes and their
  schedule and delete triggers, the policies in the policy matrix, and the twelve test scenarios in
  the scenario editor, typing every expression in the expression editor and every label's texts in its
  label editor. Each save must report no error diagnostic. At the end the built folder must match
  `samples/apps/service-desk` with no error diagnostic, as [the folder comparison](#the-folder-comparison)
  says, and the `migrations` folder must be empty, because the sample has none. Run tests must pass all
  twelve scenarios, and the release history must show release 1 as the one active row. On the
  `service-desk` site the requester, the service desk lead and the agent take one ticket to `closed`,
  the requester opening it from the bell, and the lead's bell has no row for that ticket.
- **`first-application-tutorial.spec.ts`** runs the steps of the
  [first application tutorial](tutorials/first-application.md) on the journey tenant, one test for
  each step. The author builds the application in Studio, from the texts and roles to the process,
  pages, site and policies, then validates and publishes it. An employee submits an expense, and a
  department manager approves it. The manager has only read grants, so the test also checks that the
  manager's expense list has no New button.
- **`module-contract-journey.spec.ts`** creates a blank application that pins the built-in module
  `organization` and the sample module `department-budgets`, through the Studio API. The publish
  succeeds and the release is not off-standard. A process step that approves a budget is refused by
  the contract's `approve` guard, and the platform operator sees the instance in the console's Failed
  work with `named point Budget/transitions/approve`. The platform operator gets Studio's forbidden
  page with no Relax button, and a PATCH that adds a relax entry answers `403`. The author
  relaxes the point with a reason in Studio and publishes, Studio's release history and the console's
  Publishing row both show Off-standard with the module, the point and the reason, and the same step
  then approves a budget within 30 seconds.
- **`approvals-data-business.spec.ts`** creates an application from the built-in `approvals`
  application and maps the `admin` slot of `organization` and `approvals` to a new role. An
  administrator sets up employees, a department and request types in the running application. A
  request never shows in its requester's own To decide list, a one-step request is approved by the
  department head, and a two-step request is decided by a delegate on behalf of the approver, with
  both decisions recorded.
- **`approvals-module-business.spec.ts`** creates an application from the built-in `approvals`
  application, adds a `costCenter` field to `approvals/Request` in the entity designer, and checks
  that Studio saves it as the layer `customizations/approvals/Request.json`. A request type made in
  the running application works, and a department head's own request is absent from their To decide
  list. A step that sets `approved` right after `submit`, saved through the module resource save
  route, fails the publish with `AXC0342` and a failed contract scenario, and the release history
  keeps its one row.
- **`asset-management-journey.spec.ts`** builds the asset management application with Studio only. It
  opens the JSON view only to write the `AssetWithMaintenanceAndSoftware` scenario, because the
  scenario editor cannot add a value to a child collection. Every migration that a save asks about is
  answered, and the `migrations` folder must be empty at the end. It checks the built folder against
  `samples/apps/asset-management` in parts as it goes, its texts excepted, which are checked at the end
  as [the folder comparison](#the-folder-comparison) says. At the end the whole folder must match with
  no error diagnostic and Run tests must pass all six scenarios. It publishes release 1. On the
  `asset-management` site the asset manager registers an asset with an attachment, software and a
  maintenance entry, and imports `tests/e2e/fixtures/asset-management-import.csv` with a dry run that
  shows no row error. The auditor exports the register to CSV, whose data rows must equal the
  `totalCount` that the record API gives the auditor, and to Excel, whose file must have the `.xlsx`
  name and start with the zip signature. The auditor has no New or Import button, and the employee
  sees only the asset it holds and no Export, Import or New button.
- **`approvals-application-journey.spec.ts`** creates `OfficeApprovals` from the built-in Approvals
  application in the New application dialog, with no file edit and no error diagnostic, and publishes
  release 1, the one active row. On the `office-approvals` site an employee opens My requests and is
  refused All requests, by the menu and by a `403` from the page API.

A new trigger starts with an empty `event`, see [new resource](reference/studio.md#new-resource). So a
journey that builds a trigger and must match a sample with no event creates
a temporary event first, and deletes it once the triggers are saved.

### The folder comparison

The service desk and asset management journeys compare the folder they built with the committed
sample. `compareApplicationFolders` in `tests/e2e/folder-comparison.ts` throws at the first difference.

- **Files.** Every file of the sample must exist in the built folder, and the reverse, unless the
  comparison names the files it checks. A named file missing from either folder fails.
- **Ids and order.** A resource's top-level `id` and the key order are ignored. Seed record ids pair by
  position, so a scenario's seed reference must match the record at the same place.
- **Label keys.** A label's text key may differ from the sample's when the sample's key is used at the
  same place, such as a field's label or a step's label. Each built key pairs with the sample key used
  at the same place, and one sample key may pair with a new key at each place where it is used.
- **Texts.** A text resource compares through those pairs. Each built text must equal the sample's text
  for each locale, under the sample key it pairs with, or under its own key when none pairs with it. A
  built key that no label uses fails when the sample's labels use that key, and passes when the sample
  holds it with the same texts. A sample key that no built key covers fails as missing.
- **Page element names.** A journey may skip the names of page widgets, actions, columns and tabs with
  `ignorePageElementNames`, because Studio does not name them the way the samples do. Without it, a
  different name fails. With `generatedNames`, a page may hold an extra generated name such as `table1`
  that the sample lacks.

## Purchasing system stub

`tests/Axis.PurchasingStub` stands in for the external purchasing system that the purchase request
sample's connector calls. It is a test fixture, not platform code, and references nothing under
`src/`. The E2E run, `scripts/dev.sh` and Docker Compose start it. Its orders and webhook deliveries
live in memory only.

**Tokens.** `POST /oauth/token` is the token route of the OAuth2 client credentials grant. It reads
the form field `grant_type`, and the client from the `Authorization: Basic` header, as the worker
sends them.

- Any other grant type is a `400` with `unsupported_grant_type`, and a missing or wrong client is a
  `401` with `invalid_client`. Otherwise it answers `200` with `access_token`, `token_type` `Bearer`
  and `expires_in`, and `Cache-Control: no-store`.
- The stub knows one client, `axis-purchasing` with the secret `axis-development-purchasing-secret`.
  The settings `Purchasing:ClientId` and `Purchasing:ClientSecret` change them, but every development
  and test setting uses these defaults.
- A token lives 120 seconds, and the setting `Purchasing:TokenLifetime`, in seconds, changes that.
- `POST /purchase-orders` and `GET /purchase-orders/by-key/{idempotencyKey}` need an
  `Authorization: Bearer` header with a token the stub issued that has not expired. Without one the
  stub answers `401` with `WWW-Authenticate: Bearer error="invalid_token"` and stores nothing. The
  order list, the webhook routes, `/health` and the `/test/*` switches need no token.

**Orders.**

- `POST /purchase-orders` with an `Idempotency-Key` header and a body with `requestNumber`,
  `supplierCode`, `amount` and `lines` creates an order. Each line has a `description`, a
  `quantity`, a `unitPrice` and an `amount`. It answers `201` with an `orderNumber` such as
  `PO-2026-0001`. A repeat with the same key answers `200` with the same order and stores nothing new.
  A create without the key, `requestNumber`, `amount` or `lines`, with an empty `lines`, or with a
  line that lacks a field is a `400`.
- `GET /purchase-orders/by-key/{idempotencyKey}` answers `200` with the `orderNumber`, or `404` when
  the key is unknown.
- `GET /purchase-orders` lists every stored order in creation order. Each order shows its `lines` in
  the order sent and the `clientId` of the token that created it, so a test can tell that the order
  came after a token request with the sample's credentials.

**Webhooks.**

- `POST /webhooks/purchase-request-approved` receives a
  [webhook delivery](reference/outbound-webhooks.md#webhook-delivery) and checks its Standard
  Webhooks signature. The headers `webhook-id`, `webhook-timestamp` and `webhook-signature` are all
  required. A missing header, a signature that matches no secret, or a timestamp more than 5 minutes
  from the stub's clock is a `401` and stores nothing. The header may hold two signatures separated
  by a space, and one matching signature is enough.
- The body must be JSON with a string at `data.fields.number`, or the route answers `400`. A valid
  delivery is stored once per `webhook-id` and answers `204`. A repeat with the same `webhook-id` also
  answers `204` and stores nothing new.
- The stub checks the signature against the secret of the `Webhook:Secret` setting. Its default is
  `whsec_YXhpcy1kZXZlbG9wbWVudC13ZWJob29rLXNlY3JldA==`, and a `whsec_` secret is the key after
  base64-decoding the rest.
- `GET /webhooks/purchase-request-approved` lists the stored deliveries in arrival order. Each shows
  its `webhookId`, its `type`, its `requestNumber`, taken from `data.fields.number`, and its
  `receivedAt` time.

**Test switches.** Each switch answers `204`, and each drop and each hold happens once.

| Stub route                                     | Effect                                                                                                                                                                                                                                                                                                                                                                                      |
| ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `POST /test/fail-webhook/{requestNumber}`      | Every delivery for that request number answers `500` and stores nothing. Other request numbers are unaffected.                                                                                                                                                                                                                                                                              |
| `POST /test/accept-webhook/{requestNumber}`    | Turns `fail-webhook` off, so the deliveries for that request number are accepted again.                                                                                                                                                                                                                                                                                                     |
| `POST /test/drop-next`                         | Drops the response of the next create. It affects the whole stub, so specs that run in parallel drop by key or by request number.                                                                                                                                                                                                                                                          |
| `POST /test/drop/{idempotencyKey}`             | Drops the response of the first create with that key.                                                                                                                                                                                                                                                                                                                                       |
| `POST /test/drop-request/{requestNumber}`      | Drops the response of the first create for that `requestNumber`, such as `PR-2026-00042`. A spec knows the request number as soon as it creates the request, but not the idempotency key, which only the worker knows, so specs that run in parallel can use it.                                                                                                                           |
| `POST /test/hold-request/{requestNumber}`      | Holds back the response of the first create for that `requestNumber`. The create stores its order first, then answers only when `POST /test/release-request/{requestNumber}` is called or the caller hangs up. Tests use it to stop or overtake a worker while its call is still running.                                                                                                  |
| `POST /test/unknown-request/{requestNumber}`   | Makes the purchase order call for that `requestNumber` end `unknown` and stay so: the first create stores its order but answers `201` without an `orderNumber`, and every lookup for the request answers `503`. An operator then [resolves](reference/process-inspection.md#resolve) the call. The switch has no off route, since each spec uses its own request number. |

A dropped create stores the order, then closes the connection without answering, so the retry gets
the stored order back, as in the [lost response](reference/process-execution.md#lost-response) flow.
A dropped response cannot make a call `unknown`, because the connector declares `idempotency`, so
the worker sends the call again under the same key and it succeeds. Only a `2xx` body that does not
fit the output goes straight to reconciliation, which is why `unknown-request` answers one.

`GET /health` answers `200`.
