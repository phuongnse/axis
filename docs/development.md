# Development

This page says how to run Axis on one machine, what each script does, and the development data the
scripts set up: the database roles, the Keycloak realms and their users, and the preview tenant.
Production installs follow [installation](reference/installation.md), the short list of commands is
in [AGENTS.md](../AGENTS.md#commands), and the test levels and rules are in [testing](testing.md).

## Scripts

The scripts build, check and run Axis, and NexKit and CI run the same ones as their checks. Run them
from the repository root. NexKit and CI run the scripts from `scripts/setup.sh` to
`scripts/smoke.sh`; the others say which workflow runs them, or that they run on demand only.

| Script                          | What it does                                                                                                                                                      | Needs           |
| ------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------- |
| `scripts/setup.sh`              | Restores .NET and npm packages, the Playwright browser, the pinned Helm tools and, on CI, the container images                                                    | Network         |
| `scripts/build.sh`              | Builds the SPA with its checks, the licence lists, the Keycloak themes and the solution, and installs the samples and writes the OpenAPI document in `artifacts/` |                 |
| `scripts/lint.sh`               | Format, analyzer and type checks, the code checks of the SPA and the tests, the docs checks and the Helm lint                                                     |                 |
| `scripts/test.sh`               | .NET unit tests, the control plane's unit tests, Vitest and the scripts' own tests                                                                                |                 |
| `scripts/integration.sh`        | .NET tests and every sample's test scenarios on real PostgreSQL and Keycloak                                                                                      | Docker          |
| `scripts/e2e.sh`                | Playwright against the full stack                                                                                                                                 | Docker          |
| `scripts/smoke.sh`              | Smoke tests of `scripts/dev.sh`, Compose, the production images and the on-premises install                                                                       | Docker          |
| `scripts/performance.sh`        | The `*PerformanceTests` classes alone, as the performance workflow runs them                                                                                      | Docker          |
| `scripts/scan.sh`               | NuGet and npm vulnerability scan                                                                                                                                  | Network         |
| `scripts/scan-images.sh`        | Vulnerability scan of the production images and the identity provider image                                                                                       | Docker, network |
| `scripts/docs-audit.mjs`        | The docs audit of `docs-audit.yml`: picks a run's pages, reads them, opens issues, picks link homes                                                               | Network         |
| `scripts/check-issue-links.mjs` | Lists open issues' links to a missing file or heading of `main`, with git's facts, and relinks them                                                               | Network         |
| `scripts/shard-durations.mjs`   | CI's [shard durations](#shards) job: refreshes durations from recent runs, opens or updates their issue                                                           | Network         |
| `scripts/load.sh`               | The load test against the reference stack                                                                                                                         | Docker          |
| `scripts/load-cell.sh`          | The cell load test against a staging cell                                                                                                                         | Docker, network |
| `scripts/dev.sh`                | The local stack with hot reload, see [hot reload](#hot-reload-with-scriptsdevsh)                                                                                  | Docker          |

### Setup

`scripts/setup.sh` restores the .NET and npm packages and the Playwright browser, and installs pinned Helm,
kubeconform and the Kubernetes schemas into `~/.cache/axis-tools`. Each step and download gets up to 4 tries,
with waits of 15, 30 and 60 seconds, so a short `403` is retried. A try of a restore or install step stops
after 10 minutes, or runs with no limit on stock macOS, which lacks `timeout`. A download gives up after 30
seconds without a connection or 5 minutes. A service that does not answer fails its try in time for the
next. See [D147](decisions/d147-a-run-downloads-only-what-the-caches-miss-and-a-transient-stop-runs-again-after-a-wait.md).

On CI, setup also keeps every container image that the integration, E2E and smoke checks pull, the ones listed in
`scripts/docker-images.json`, in one archive in `~/.cache/axis-docker`, which NexKit's `docker` cache entry restores, as
[D147](decisions/d147-a-run-downloads-only-what-the-caches-miss-and-a-transient-stop-runs-again-after-a-wait.md)
agrees. One archive stores each layer that images share once. Setup pulls only the images the archive lacks and saves
the whole set again, so every job leaves the full set in the folder. Each try of a pull has the same time limit as
a restore step, so a registry that stalls fails the try and the next one starts, while the write of the archive has
none. An image that still fails to pull is left out with a warning, and the job goes on: the suite that needs it pulls it itself. A test fails when a fixture, a Compose
file, the `Dockerfile` or a test script names an image that the list lacks.

### Build

`scripts/build.sh` runs these steps in order, and fails at the first that fails:

1. It builds the SPA into the server web root and runs the [bundle check](#bundle-budget) and the
   [font check](#fonts-and-the-keycloak-theme).
2. It writes the open source licence lists with `scripts/write-licences.mjs`: the public
   `wwwroot/licences.json` without versions, and the gitignored `src/Axis.Server/licences.json` with
   them. It fails when either list has no npm or no NuGet entries.
3. It builds the Keycloak login theme `axis` into `artifacts/keycloak-theme/axis/login`, and the email
   theme `axis` beside it, and checks their files and fonts.
4. It builds the [sign-up page](reference/sign-up.md#the-page) into the control plane's web root,
   `controlplane/src/Axis.ControlPlane/wwwroot`, and checks that `sign-up.html` is there and runs the
   [font check](#fonts-and-the-keycloak-theme) on the folder.
5. It builds the .NET solution, which installs the sample extension package in
   `artifacts/extensions`.
6. It installs the built-in and sample modules in `artifacts/modules`, and the built-in applications
   in `artifacts/templates`.
7. It writes the server's OpenAPI 3.1 document to `artifacts/openapi/axis-server.json`, and fails when
   the file is missing. The `tests/Axis.OpenApiDocument` console writes it: it starts the server in
   memory with the settings of the server unit tests and takes the document from the generator of
   `Microsoft.AspNetCore.OpenApi`. The server registers the generator and serves no document.

#### Bundle budget

The bundle budget keeps the SPA's first load small. The main bundle is the entry script of
`index.html` and the scripts it preloads, gzipped. It may grow by 50 KB over the baseline that
`web/bundle-budget.json` holds. These parts load in chunks of their own, apart from the main bundle:

| Chunk                             | What it holds                                                            |
| --------------------------------- | ------------------------------------------------------------------------ |
| `assets/ChartDrawing-*.js`        | The chart library, loaded lazily                                         |
| `assets/StudioPageDesign-*.js`    | The page designer's canvas and palette, loaded when the Design tab opens |
| `assets/StudioFormDesign-*.js`    | The form designer's Design tab, loaded when it first opens               |
| `assets/StudioProcessDesign-*.js` | The process designer's Design tab, loaded when it first opens            |
| `assets/DragAndDrop-*.js`         | The drag and drop library `@dnd-kit`, which the three designers share    |

[`scripts/check-bundle.mjs`](../scripts/check-bundle.mjs) runs after the SPA build. It prints the
sizes and fails when the main bundle is over budget, when the chart, page design, process design or
drag and drop chunk is missing, or when any of them is among the main bundle files. Do not raise the
budget to make the check pass: find what grew.

#### Fonts and the Keycloak theme

[`scripts/check-fonts.mjs`](../scripts/check-fonts.mjs) runs on the SPA build output, on
`artifacts/keycloak-theme/axis/login/resources` and on the control plane's web root. It lists the font files in `assets/` and fails when a
file is not IBM Plex Sans or IBM Plex Mono in the Latin, Latin Extended or Vietnamese subset, when one
of the six combinations has no `.woff2` file, or when a licence text is missing from `fonts/`. The
build also fails when the login theme has no `theme.properties`, `login.ftl` or
`resources/assets/axis-sign-in.js`. The theme has no size budget.

### Lint

`scripts/lint.sh` runs these checks:

- the `dotnet format` check, oxlint, and the TypeScript and Prettier checks;
- that no test selects an `ant-` class, and that oxlint, with the locator rule of Axis's plugin on
  `tests/e2e`, finds every Playwright locator matching a string name or text exactly, see
  [Testing](testing.md#rules-for-tests);
- that no file under `web/` writes raw HTML and that the SPA has no code that a key such as
  `__proto__` can turn into prototype pollution. Oxlint runs these rules. The exception to a security
  rule is an `overrides` entry in `web/.oxlintrc.json` with its reason, and `scripts/lint.sh` refuses
  a disable comment that names one or names no rule;
- that `web/` uses `AxisButton`, `AxisAlert`, `AxisTable` and the colour tokens in place of Ant Design's own.
  Oxlint runs these rules too, and a use that predates them carries a disable comment with its reason, see
  [shared widgets](reference/shared-widgets.md#what-the-build-checks);
- that no test outside a `*PerformanceTests` class measures elapsed time;
- the Markdown link and anchor check and the docs structure check, see
  [writing the docs](writing-docs.md#how-the-docs-stay-correct);
- the Helm charts' lint and their check against the Kubernetes schemas.

### Unit tests

`scripts/test.sh` runs the .NET unit tests, `Axis.ControlPlane.Tests` of the control plane included,
Vitest and the tests of the scripts themselves. It first installs the built-in and sample modules
and the built-in applications in `artifacts/`, for the server tests. It then writes the server's
OpenAPI document with the `tests/Axis.OpenApiDocument` console, as [the build](#build) does, because
NexKit runs it without `scripts/build.sh`. `OpenApiDocumentTests` in `Axis.Server.Tests` reads that
file and compares it with the routes the server maps. It fails when a route is missing from the
document or the document lists a route the server does not map, and it names each endpoint left out
on purpose with a reason. `RouteTableDocTests` reads the same file and every `Route` table and route
heading under `docs/`. It fails, naming each route, when one side has a route that the other lacks.
See [how the docs stay correct](writing-docs.md#how-the-docs-stay-correct).

Set `AXIS_TEST_SHARD`, such as `AXIS_TEST_SHARD=2/3 scripts/test.sh`, to run one shard of these suites,
see [test runs and reports](#test-runs-and-reports).

When Vitest starts, it runs one worker for each GiB of free memory above 1 GiB, and no more workers than CPU
cores. The count is never below 2 or above 6. `vitestWorkers` in `web/vitestWorkers.ts` computes it, and
`web/vite.config.ts` passes it the free memory and the core count. A worker holds about 1 GB, so too many
workers swap on a machine that also runs the local stack. Workers that outnumber the cores also wait for a
CPU, which slows each test until it times out. A `--maxWorkers` flag on the command line overrides the number.

Vitest runs without the CSS of the `<style>` elements in the document head, which holds Ant Design's styles, because
jsdom matches every rule on each query. A test that checks a style calls `withAntdStyles()` from
`web/src/test/antdStyles.ts` at file scope or in a `describe`.

### Integration tests

`scripts/integration.sh` loads the [saved images](#setup), builds the Keycloak theme, then runs the .NET tests against real PostgreSQL
and Keycloak through Testcontainers. They also run the test scenarios of every application under
`samples/apps/` on that PostgreSQL, and the control plane's `Axis.ControlPlane.Integration.Tests`.
The performance tests, whose class names end in `PerformanceTests`, stay out: `scripts/performance.sh`
runs them.

- **Arguments** go to `dotnet test`, such as `--filter-class 'Axis.Integration.Tests.Studio*'`, and
  run only the matching tests, without the scenario suites and the control plane suite.
- **`AXIS_INTEGRATION_SHARD`**, such as `1/3`, runs only that shard, see
  [test runs and reports](#test-runs-and-reports).
- **A skip.** The script skips every suite when NexKit gives a commit to compare with and every
  file changed since it is in the `unaffected_by` list of the integration check in
  `.nexkit/config.json`, see [skipped suites](#skipped-suites).

### End-to-end tests

`scripts/e2e.sh` loads the [saved images](#setup), builds the Keycloak theme, then starts the stack that Playwright runs against, and
runs Playwright in Chromium. It:

- starts PostgreSQL, Keycloak, the built purchasing system stub, the server, the worker and the
  publisher;
- adds the `axis first login` flow to the `axis` realm before the server starts, see
  [the axis realm](#the-axis-realm);
- gives Keycloak the `axis-admin` client and the breached-password list, and creates the PostgreSQL
  role `axis_admin`, so a spec can run `Axis.Admin tenant create`;
- starts MinIO with the bucket `axis-exports` and a KMS key, which the export job's encrypted upload needs, and
  gives Playwright its five `AXIS_E2E_EXPORT_STORAGE_*` values. Only the cell specs pass them on, as the
  `Cell:ExportStorage` settings of their server and their publisher;
- creates the `journey` tenant for Studio journeys, with its own database `axis_journey`, its roles
  `axis_journey_schema` and `axis_journey_runtime`, and its server on `127.0.0.2`, which the worker
  and the publisher serve too;
- starts Mailpit with its SMTP and API ports published on 127.0.0.1, and gives Playwright its API address
  as `AXIS_E2E_MAILPIT_URL`;
- starts the control plane with its databases `axis_controlplane` and `axis_directory`, which sends its emails
  to that Mailpit, see [the E2E stack](testing.md#the-e2e-stack);
- makes a throwaway TLS certificate for the run, which the https server of each spec that runs `tenant create`
  uses, on `AXIS_E2E_TENANT_HTTPS_PORT` (default 5283) and the ports after it, one offset for each spec;
- builds and checks the design-system bundle in `artifacts/design-system` for its spec, see
  [widget previews](reference/widget-previews.md).

Arguments go to Playwright, such as `tests/e2e/specs/studio.spec.ts` or `--grep 'publishes'`, and run
only the matching specs, after the sign-in setup. `AXIS_E2E_SHARD`, such as `1/3`, runs only that
shard, and with arguments it runs Playwright's own `--shard` of the matching tests. Playwright and the
services it starts are skipped when NexKit gives a commit to compare with and every file changed
since it is under `docs/`, `.github/` or `.nexkit/` or is a Markdown file. What the E2E stack holds is
in [Testing](testing.md).

### Smoke tests

`scripts/smoke.sh` runs four smoke tests. It first installs the built-in and sample modules in
`artifacts/modules`, for the `onprem` test, and then loads the [saved images](#setup).

| Test         | What it does                                                          |
| ------------ | --------------------------------------------------------------------- |
| `dev-script` | Starts `scripts/dev.sh` in a copy of the repository                   |
| `compose`    | Runs the Compose stack                                                |
| `images`     | Runs the production images, from `scripts/images.test.mjs`            |
| `onprem`     | Installs `deploy/compose/` from the production images for two tenants |

`dev-script` starts the Compose services of `scripts/dev.sh` once, on ports it picks before its first case, and
its cases reuse them. Each start of `scripts/dev.sh` gets fresh ports for the server, the SPA, the worker and the
agent host. The test removes the Compose project at the end.

`scripts/smoke.sh` builds the [production images](production-images.md) once, before `images` and `onprem`, which both use them,
and removes them when it exits. A change that skips both tests builds nothing. The PostgreSQL tools layer of
`axis-admin` comes from the `admin-tools` stage of the `Dockerfile`. The build saves that stage and its base
image in `~/.cache/axis-docker/admin-tools-<hash>.tar`, where the hash is of the stage text without its
comments, and loads the file in a later run, so the run does not download the packages again, and pulls the base
image only when Docker lacks it. NexKit's `docker` cache entry restores the folder, and CI on `main` saves it. The
layer is built again when the stage changes, when the entry's key changes, or when GitHub drops the cache after 7 days
without use. The saved layer can be
weeks old while the stage stays the same, so the smoke tests may run older client packages than an install gets.
The [image scan](#workflows-outside-nexkits-checks) never uses the saved layer: it builds from scratch and sees
each new package release.

Set `AXIS_SMOKE_SHARD`, such as `AXIS_SMOKE_SHARD=3/3 scripts/smoke.sh`, to run only the smoke tests of one
[shard](#shards). A shard builds the production images only when it holds `images` or `onprem`.

Each test is skipped when NexKit gives a commit to compare with and every file changed since it is
in the `unaffected_by` list of the smoke check in `.nexkit/config.json`: under `docs/`, `.github/` or
`.nexkit/`, or a Markdown file. CI and NexKit run the smoke tests in [shards](#shards), one job per shard.

### Performance and load tests

`scripts/performance.sh` runs the performance tests of the integration tests, the classes whose names
end in `PerformanceTests`, alone and one class at a time, against real PostgreSQL and Keycloak. It
writes `artifacts/test-results/performance.xml`. Arguments go to `dotnet test` in place of its class
filter, such as `--filter-class '*SearchPerformanceTests'`, and run only the matching classes. It is
not a NexKit check: the [performance workflow](#workflows-outside-nexkits-checks) runs it.

`scripts/load.sh` is the load test of [performance](reference/performance.md#load-test). It:

1. builds the three production images, writes the generated application of typical size with seed 1
   into `artifacts/load/apps/load`, and starts the reference stack of `deploy/compose/compose.yaml`
   with a load override in `artifacts/load`: PostgreSQL over TLS, a certificate authority made for the
   run, and the secret files. The stack starts PostgreSQL, then the identity provider and the proxy,
   then `server` and the worker, then `server-2` with the `replicas` profile;
2. after the health checks, runs `admin migrate` and creates the tenant `load`;
3. sets up the `load` realm through the identity provider's admin API: the redirect addresses and
   the roles mapper of the `axis` client, and the application's roles. It adds the users
   `load-user-01` to `load-user-50` with a password, an authenticator from the seed and every role
   but `load-caller`, and writes their names, passwords and secrets to
   `artifacts/load/k6/users.json`. It adds the confidential client `load-caller`, a machine caller
   whose service account holds the role `load-caller`, and writes its id and secret to
   `artifacts/load/k6/caller.json`;
4. runs `admin tenant oidc set load --endpoint-audience axis-load-endpoints` and
   `admin activate /apps/load --tenant load`, which stores the release and writes the seed records;
5. runs the k6 scenario `tests/load/objectives.js` in a container through the proxy: 50 users sign
   in through the `load` realm's sign-in page, with a password and then an authenticator code, and
   loop over the read and write routes of the objectives, and the machine caller calls the inbound
   endpoint;
6. removes the stack and its folder, or leaves both with `--keep` and prints the command that removes
   them.

k6 exits non-zero when reads miss p95 under 300 ms, writes miss p95 under 500 ms, more than 0.1% of
requests answer `5xx` or fewer than 99% of the checks pass. It writes its summary to
`artifacts/load-results/summary.json`. The stack also runs an OpenTelemetry Collector that receives
the worker's metrics over https and writes them to `artifacts/load-results/worker-metrics.json`. The
cleanup keeps both files. `scripts/lib/load-objectives.mjs` prints each objective with its measured
value, from both files. The exit code is the checker's when it fails, otherwise k6's, and k6's when k6
wrote no summary. Arguments after `--` go to k6, and any other argument exits with code 2. The host
ports default to 18080, 18081, 18443 and 15208, and the `AXIS_*` port variables override them.

`scripts/load-cell.sh` is the [cell load test](reference/performance.md#cell-load-test). It runs on demand against a
staging cell, with the address of its provisioning API as its only argument, and never in CI or NexKit.

1. It writes the generated application into `artifacts/load-cell` for its roles.
2. It creates the 1,000 load tenants once, 50 busy, 949 idle and `load-backlog`, through the
   provisioning API, and reuses `artifacts/load-cell/k6/tenants.json` on a rerun.
3. It runs the k6 scenario `tests/load/cell.js` in Docker at 1,000 tenants and at each lower count in
   steps of 100, and suspends the idle tenants with the highest numbers before each lower step.
4. Each step runs the operator commands `AXIS_LOAD_BACKLOG_COMMAND`, `AXIS_LOAD_METRICS_COMMAND` and
   `AXIS_LOAD_USAGE_COMMAND`, keeps `summary.json`, `worker-metrics.json`, `usage.json` and
   `result.txt` in `artifacts/load-results/cell-<count>`, and prints each pass criterion with its
   value.
5. It stops at the first passing count and prints it as `Cell:TenantCap=<count>`.

It exits 1 when no count from 1,000 down to 100 passes or a step breaks, and 2 without an address or
with a missing setting.

### Scans

`scripts/scan.sh` scans the NuGet and npm packages, and `scripts/scan-images.sh` scans the production
images. Both fail on a high or critical finding that their accepted list does not cover, see
[dependency updates](#dependency-updates). CI runs both on its triggers, and its weekly schedule
runs them alone, so a new advisory fails CI on `main` even when nothing merged. Neither is a NexKit
check.

`scripts/scan-images.sh` builds the Dockerfile's `axis-*` stages, pulls the identity provider image
that `KeycloakFixture.Image` pins, and scans them with Trivy, which it downloads at its pinned
version.

### Workflows outside NexKit's checks

These workflows run on `main`, and none of them gates a NexKit merge.

- **CodeQL**, `.github/workflows/codeql.yml`, scans C# and TypeScript on hand-made pull requests to
  `main` and on `main`. Its alerts go to GitHub code scanning, and its check fails on a new high or
  critical alert. It does not scan NexKit's pull requests: GitHub gives each one a run that waits for
  approval and runs no job, and ends it as failed when the pull request merges, so a failed CodeQL run
  on a `nexkit/` branch is not an alert. NexKit starts the scan on `main` every few merges, see
  [delivery](delivery.md#decisions).
- **Performance**, `.github/workflows/performance.yml`, runs `scripts/performance.sh` on `main` when
  NexKit starts it, and on demand. NexKit watches it as it watches CI, so a failure that repeats opens
  a `bug` issue. Its results are the `test-results-performance` artifact.
- **Load test**, `.github/workflows/load.yml`, runs `scripts/load.sh` every Monday and on demand,
  never on pull requests. A missed objective marks only its run red, and NexKit does not watch it. Its
  results are the `load-results` artifact, kept 90 days.

## Dependency updates

Axis keeps its third-party packages current, and sets how fast a vulnerable one must change.

**Inventory.** These files list every dependency and its version.

- `Directory.Packages.props` holds the central version of each direct NuGet package.
  `dotnet list package --include-transitive` lists the transitive ones, which the scan reads too.
- `web/package-lock.json` and `tests/e2e/package-lock.json` pin every npm package.
- `global.json` pins the .NET SDK.
- `KeycloakFixture.Image` pins the identity provider image to an exact version. Every other Keycloak
  image reference names the same image, and `scripts/keycloak-image.test.mjs` checks this.

**Sources.** Packages come from nuget.org and the npm registry, `registry.npmjs.org`, and nowhere
else. The repository has no `nuget.config` and no `.npmrc`, so no other source is set. A change
that adds a source changes this section too.

**Time frames.** The time starts on the day the scan first finds the vulnerability.

| Severity | Time frame to update or replace the package |
| -------- | ------------------------------------------- |
| Critical | 7 days                                      |
| High     | 30 days                                     |
| Moderate | 90 days                                     |
| Low      | The next routine update                     |

- **Routine update.** At least once per milestone, every dependency moves to its latest compatible
  version, whether or not it has a finding.
- **What the scans enforce.** `scripts/scan.sh` and `scripts/scan-images.sh` enforce the time frames
  of critical and high findings, with the same rules and messages. They print moderate, medium, low
  and unknown findings and pass. People keep those time frames in the routine update.
- **A fix that does not exist yet.** When no fixed version exists, record the finding in
  `scripts/scan-accepted.json`: `{ "advisory": "GHSA-...", "package": "<name>", "found": "YYYY-MM-DD",
"reason": "<why it waits>" }`. `found` is the day the scan first found it. The scan then passes
  the finding until `found` plus its time frame, and prints `accepted until <date>`.
- **Past the deadline.** After that date the scan fails again and prints
  `past its time frame since <date>` on the finding's line. Update or remove the package, and delete
  the entry.
- **A bad entry.** An entry with no advisory, no package, no date, an invalid date or a date in the
  future fails the scan and names the entry. An entry that matches no finding does nothing, so
  delete it when its package is updated.
- **Images.** The image scan's accepted list is `scripts/scan-images-accepted.json`, with entries of
  the same shape: `advisory` holds Trivy's vulnerability id, such as a CVE, and `package` its package
  name, and one entry covers that package in every image.
- **Alerts.** Dependabot alerts cover NuGet, npm, GitHub Actions and the Dockerfile's base images,
  and Dependabot opens no pull requests. A time frame starts on the alert's date when it comes
  before the scan's. NexKit's autopilot opens a NexKit issue, in no milestone, for each advisory of
  a high or critical alert that no open issue covers, because `.nexkit/config.json` sets its
  `autopilot.dependabot` to `high`, see
  [D107](decisions/d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md).

## Test runs and reports

This section says which suites a run skips, how CI and NexKit split the slow suites into shards, where
the results go, and how a flaky test is handled.

### Skipped suites

The test, integration and E2E scripts run every suite even when one fails, except the suites that no
change since NexKit's commit can affect.

- **The commit.** It is `NEXKIT_CHECK_VERIFIED_SHA`, the head on which that check last passed with its
  current command and setup, also in a round that NexKit stopped or whose other checks failed, else
  `NEXKIT_BASE_SHA`, the merge base with `main`. `NEXKIT_VERIFIED_SHA`, the head of the last round
  whose checks all passed, is never used, because NexKit does not record which checks or commands that
  round ran, so a check added or changed since would skip without having passed. Without any of them,
  as in CI and local runs, every suite runs.
- **The safe lists** live only in the `unaffected_by` lists of the checks in
  [`.nexkit/config.json`](../.nexkit/config.json). `scripts/lib/affected.mjs` reads them with NexKit's
  glob rules: `*` stays within one folder, `**` crosses folders, a `!` pattern takes a path back out and
  the last pattern that matches decides. A suite skips only when every check that runs its script lists
  every changed path. A suite that no check lists runs in full. The integration tests and their scenarios
  have a list that holds the SPA under `web/`, but not its package files, the Keycloak login theme in
  `web/keycloak-theme/` or the Vite configs that build it, because `scripts/integration.sh` builds that
  theme and the tests read its sign-in page. Playwright and the smoke tests have the smaller list of
  `docs/`, Markdown files, the GitHub workflows in `.github/` and NexKit's settings in `.nexkit/`.
- **Doubt runs the suite:** a missing base, a git error or an empty change list.
- **NexKit's own skip.** NexKit reads the same `unaffected_by` lists, so it can skip a smoke,
  integration or E2E check before it gets a runner.
- **What a run shows.** Under NexKit, a suite that runs in full says which changed path made it run
  and which commit it compared with. A skipped suite is listed as skipped, and on GitHub Actions it
  gets a notice annotation, so the run's page lists what each check skipped. GitHub shows the check's
  job as passed, because the job decides what to skip only after it starts.

### Shards

NexKit's `e2e`, `integration`, `smoke` and `test` checks run in as many shards as their split's `--count` prints,
as [D97](decisions/d97-reliable-test-runs.md) says: `e2e-1` to `e2e-N`, each with `NEXKIT_SHARD=k/N`, and [CI on
main](delivery.md#ci-on-main) runs the same. A check's `max` matches its split's cap, since NexKit fails a count
above it. Two NexKit rounds at their caps fit within the jobs that GitHub's Pro plan runs at once, as
`nexkit-shards.test.mjs` checks, and when a third round or CI on `main` and its two scans run beside them, a few
jobs may wait.

- **Unit tests.** Set `AXIS_TEST_SHARD`, such as `AXIS_TEST_SHARD=2/3 scripts/test.sh`. `NEXKIT_SHARD` works
  the same way beside it, and `AXIS_TEST_SHARD` wins when both are set. Shard 1 runs the .NET unit tests,
  the control plane's unit tests and the scripts' own tests, and no Vitest file. Shards 2 to N split the
  Vitest files between them. A file weighs its seconds in `web/vitest-durations.json`, and a new file
  weighs the median. A shard's results go to `artifacts/test-results/web-<n>.xml`. With `1/1` or no shard,
  the whole suite runs.
- **E2E.** Set `AXIS_E2E_SHARD`, such as `AXIS_E2E_SHARD=2/3 scripts/e2e.sh`. `NEXKIT_SHARD` works the
  same way beside it, and `AXIS_E2E_SHARD` wins when both are set. Its Playwright results go
  to `artifacts/test-results/playwright-<n>.xml`. The shards split the specs by how long they run, not
  by how many tests they hold: each spec weighs its seconds in `tests/e2e/spec-durations.json`, and a
  new spec weighs the median. A serial spec, such as a Studio journey, runs on one worker from start
  to end, so the split gives a long one a shard with little else. The count adds the shard setup, the
  seconds a shard pays besides its specs, under `shard setup` in the same file. `--durations` measures it
  as each file's wall time minus its specs' estimate, averaged.
- **Integration.** Set `AXIS_INTEGRATION_SHARD`, such as `AXIS_INTEGRATION_SHARD=2/3 scripts/integration.sh`.
  `NEXKIT_SHARD` works the same way beside it, and `AXIS_INTEGRATION_SHARD` wins when both are set.
  A shard runs whole test classes of `Axis.Integration.Tests`, which `scripts/lib/integration-shard.mjs`
  gives it: classes that share an xUnit collection stay together, and the heaviest go first to the
  lightest shard. A class weighs its seconds in `tests/Axis.Integration.Tests/class-durations.json`,
  or, without an entry, its number of tests times the median seconds per test. Shard 1 also runs
  `Axis.ControlPlane.Integration.Tests`, which the split counts as its seconds under that suite's name
  in the same file. Shard 1 starts with those seconds, so it gets fewer classes, or none and then runs only that
  suite. The count estimates a shard as its seconds over the `parallelism` in that file, or its heaviest collection if longer, since
  a collection runs its classes one after another. `--durations` measures the parallelism as the test seconds of
  `Axis.Integration.Tests` over those suites' wall time, and the control plane and scenario suites count on
  neither side. The last shard runs every class that the other shards do not, so a class the split does not
  find still runs. The scenario suites are expected only in the shard that holds `ScenarioRunnerTests`, which writes them. A shard cannot be combined with arguments.
- **Smoke.** Set `AXIS_SMOKE_SHARD`, such as `AXIS_SMOKE_SHARD=3/3 scripts/smoke.sh`. `NEXKIT_SHARD` works the
  same way beside it, and `AXIS_SMOKE_SHARD` wins when both are set. A shard runs the smoke tests that
  `scripts/lib/smoke-shard.mjs` gives it, so each test runs in exactly one shard. Each test weighs its seconds in
  `scripts/smoke-durations.json`, and a test without an entry weighs the median. A shard builds the production
  images only when it holds `images` or `onprem`. The image builds are not in those seconds, so such a shard
  takes longer than its estimate.
- **Shard count.** `node scripts/lib/e2e-shard.mjs --count`, `node scripts/lib/integration-shard.mjs --count`,
  `node scripts/lib/smoke-shard.mjs --count` and `node scripts/lib/test-shard.mjs --count` print the fewest
  shards whose longest estimated shard fits the time budget. The unit test count includes shard 1, so it is
  never below 2. Each count stops at its split's cap, and the caps and the budget live in
  `scripts/lib/shards.mjs`. A spec, test class, collection, the control plane suite, a smoke test or a Vitest
  file that alone takes longer than the budget gets an `Over budget:` line on stderr. The count still
  prints, because no number of shards makes such a spec, class or file fit.
- **Durations.** When every check job of a [CI run on `main`](delivery.md#ci-on-main) passes, its `Shard durations`
  job, with no model, no commit and only `contents: read`, `actions: read` and `issues: write`, runs
  `scripts/shard-durations.mjs` on the `test-results-*` artifacts of the run and of the latest successful runs
  dispatched on `main` before it. Each split's `--durations` measures each run from the file as it was, each entry
  becomes the median of its measures, and each `--count` is read. A file drifts when its entries that moved past one
  run's noise pass a set share of its old total. The script sets the runs, the noise and the share. Drifted files go
  into one `process` issue, which NexKit's round commits, and each `Over budget:` unit gets its own unless one is open,
  citing [the rules for tests](testing.md#rules-for-tests). By hand, `--durations` takes `web*.xml`, `playwright-*.xml`,
  each integration shard's `Axis.Integration.Tests.xml` with shard 1's control plane one, and the smoke suites' files.
- **Performance tests.** A performance test class name ends in `PerformanceTests`. It stays out of
  `scripts/integration.sh`, so neither NexKit's checks nor CI's integration jobs run it, and
  `scripts/performance.sh` runs it. The class joins its project's performance collection, which runs
  alone, one class at a time, and a guard test fails when a `*PerformanceTests` class is outside that
  collection.

### Results and logs

Each suite writes a JUnit file to `artifacts/test-results/`, and the E2E run writes its logs to
`artifacts/logs/`:

| Log                      | Output of                  |
| ------------------------ | -------------------------- |
| `e2e-server.log`         | The E2E server             |
| `e2e-worker.log`         | The E2E worker             |
| `e2e-journey-server.log` | The E2E journey server     |
| `e2e-publisher.log`      | The E2E publisher          |
| `e2e-purchasing.log`     | The purchasing system stub |
| `e2e-controlplane.log`   | The E2E control plane      |

`scripts/test-report.mjs` then prints a summary per suite and the failed tests with their
`file:line`. On GitHub Actions the summary goes to the job summary, and each failed test becomes an
error annotation. The failed tests are also listed at the end of the job log, with each message cut to
20 lines.

Each application's test scenarios are a suite of their own, named `scenarios-<application name>`.
The integration tests run them and write `artifacts/test-results/scenarios-<application name>.xml`.
A failure names the scenario and the step. To run them by hand, run
`dotnet run --project src/Axis.Scenarios -- <folder> --server <cs> --extensions artifacts/extensions --modules artifacts/modules`.

### Flaky tests

Playwright does not retry a failed test, on CI or locally, so a flaky test fails its job, and its trace is kept. On
`main`, NexKit runs a failed job once more, and a run that passes only on that retry is recorded on a `bug` issue.

A test that fails at times is fixed by its cause, for every test with that cause, as
[D97](decisions/d97-reliable-test-runs.md) says. The plan of its `bug` issue names the cause's
class. When that class has failed a test before, the change fixes or guards every test of the
class, through a shared helper, a fixture or a lint rule. The classes seen so far and their guards:

| Cause                                      | Guard                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| ------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| An outside download or service             | NexKit's caches hold the NuGet and npm packages, the Playwright browser, the tools and the container images, so a run downloads only what they miss. Setup saves the images in one archive and the suites load it, see [setup](#setup). `scripts/setup.sh`, the CI install steps and the Docker builds of the smoke tests try again. Each try of setup's steps has a time limit, see [setup](#setup), as [D147](decisions/d147-a-run-downloads-only-what-the-caches-miss-and-a-transient-stop-runs-again-after-a-wait.md) says                                                                                                                                                                                                                 |
| A click during an animation or a redraw    | Playwright asks for reduced motion, and the SPA then shows dialogs and overlays without animation. `waitForDialog`, `confirmDialog` and the drag helper wait until the element is ready, and the drag helper and the binding choice in the journeys wait until the page canvas has drawn the dropped draft (`waitForCanvas`). `openFormTab` and `addMapping` wait until no expression editor of the open resource is loading or checking (`aria-busy`), because a check's answer adds a line under its editor and moves what is below. A spec that slows animations on purpose, to prove a wait, asks for motion again with `test.use({ reducedMotion: 'no-preference' })` |
| The real clock                             | A test fixes the time with `axis.now` and runs schedules in UTC                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| State left by another test                 | A test counts from its own baseline                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| Planner statistics                         | A seed that a plan depends on runs `ANALYZE` over every row                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| The MinIO test container cuts a connection | `MinioFixture.ReadObjectAsync` reads an object, `MinioFixture.UploadObjectAsync` uploads one and `MinioFixture.CreateBucketAsync` creates a bucket. Each tries again from the start up to five times when the connection drops or MinIO answers that the client disconnected, waiting half a second, then 1, 2 and 4 seconds. A retry that finds the bucket already owned counts as created                                                                                                                                                                                                                                                                                |
| A read of the page that does not wait      | A step that acts on what a list shows waits for it with a retrying assertion such as `toHaveCount`, not a single `count()` right after a click or a change of address, and a step that needs to know which files the server wrote reads them from the folder                                                                                                                                                                                                                                                                                                                                                                                                               |
| A canvas drawing older than the last save  | The page and form canvases post the draft again after each save, so a label whose texts Save wrote draws without its error, see [page designer](reference/studio.md#page-designer)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |

## Docker Compose

Docker Compose runs the whole stack from source, with nothing but Docker installed. Run
`docker compose up --build`. It starts:

| Service                                                               | Address               | Port setting             |
| --------------------------------------------------------------------- | --------------------- | ------------------------ |
| The server, with the four samples                                     | http://localhost:5206 | `AXIS_SERVER_PORT`       |
| PostgreSQL                                                            | `localhost:5432`      | `AXIS_POSTGRES_PORT`     |
| Keycloak, with the [development realms](#development-realm-and-users) | http://localhost:8080 | `AXIS_KEYCLOAK_PORT`     |
| The purchasing system stub                                            | http://localhost:5207 | `AXIS_PURCHASING_PORT`   |
| Mailpit, SMTP                                                         | `localhost:1025`      | `AXIS_MAILPIT_SMTP_PORT` |
| Mailpit, web page                                                     | http://localhost:8025 | `AXIS_MAILPIT_WEB_PORT`  |
| The worker                                                            | none                  |                          |
| The agent host                                                        | none                  |                          |
| The publisher                                                         | none                  |                          |

All six ports are published on `127.0.0.1` only. The Keycloak admin console login is `admin` /
`admin`.

- **The samples.** The server activates the purchase request, leave request, service desk and asset
  management samples. The images hold the built-in `organization` and `approvals` modules and the
  `department-budgets` sample module in `/modules`. The server sets `Templates:Path` to `/templates`,
  which holds the built-in `approvals` application.
- **Mailpit** is a local mail server that keeps every email, and its web page shows them. The worker
  sends the emails of `notify` steps there: Compose sets `Email__Smtp__Host` to `mailpit`,
  `Email__Smtp__Port` to 1025, `Email__Smtp__UseTls` to `false` and `Email__From` to `axis@localhost`
  on the worker. Only the Development environment, which Compose and `scripts/dev.sh` run, sends email
  with `Email__Smtp__UseTls` off, see [mail settings](reference/notifications.md#mail-settings).
- **The purchasing system stub,** in `tests/Axis.PurchasingStub`, stands in for the purchasing system
  that the purchase request sample calls when a request is approved, see
  [purchasing system stub](testing.md#purchasing-system-stub).
- **The publisher,** `axis-admin publisher`, runs Studio's publish requests, see
  [publisher](reference/administration.md#publisher). It serves the tenants `default` and
  `default-preview`, so it runs preview requests too. It has no port and no health check, and writes
  `default ready` and `default-preview ready` once the server has migrated each tenant database.
- **Health.** `docker compose ps` shows the worker healthy once its `/health/ready` passes, and
  `docker compose up --wait` waits for that. The server has 5 minutes and the worker 6 minutes to
  become healthy before `docker compose up --wait` fails. The worker's health address
  `http://127.0.0.1:5208` stays inside its container.
- **Logs.** `docker compose logs server` shows the activation diagnostics, `docker compose logs worker`
  the worker output and `docker compose logs publisher` the publisher output.
- **Studio's new applications** go to `artifacts/studio-apps`, which the checkout mounts at
  `/studio-apps`. Set `AXIS_STUDIO_APPS_DIR` to move that folder. On Linux the server runs as UID 1654,
  so create the folder first and make it writable by that UID, because Docker creates a missing folder
  as root.
- **No sign-in.** The server in its container cannot reach Keycloak at the address the browser uses,
  so a sign-in under Compose cannot complete. The site endpoints, the record API and the data source
  endpoint need a session, so under Compose they answer `401`, and an application page shows the
  sign-in prompt. Compose sets the `axis-activation` secret blank, so its server skips the realm
  update at activation. Use `scripts/dev.sh` to sign in.
- **A volume from an older checkout.** PostgreSQL runs `samples/postgres/init-roles.sql` only on an
  empty volume, and the server activates the samples over the data the volume holds. A volume made
  before a database or role of [database roles](#database-roles) existed lacks it: without
  `axis_preview` the server stops at start, and without `axis_scenario` Studio cannot run test
  scenarios. A volume that holds data a changed sample cannot activate over, such as budgets with no
  status or departments of a module the sample does not declare, makes activation fail. Run
  `docker compose down -v` once in each case. That deletes the volume's data.

## Database roles

The development stacks and the tests use the same database roles, so the runtime role never holds
schema rights there either. The integration test fixtures create two roles, `axis_schema` and
`axis_runtime`, with the development password `axis`. They own each test database with `axis_schema`,
run servers and workers as `axis_runtime`, and run startup activation as `axis_schema`. Docker
Compose, `scripts/dev.sh` and `scripts/e2e.sh` do the same.

PostgreSQL loads `samples/postgres/init-roles.sql` on its first start. It creates:

| Role or database                              | What it is for                                                                                                                               |
| --------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| `axis_schema`                                 | Owns the tenant database. The server uses it for `SchemaConnectionString` and `ConnectionStrings:PlatformSchema`                             |
| `axis_runtime`                                | Every other connection string of the server, and the worker's only one. The worker gets no `SchemaConnectionString`                          |
| `axis_preview`                                | The database of the preview tenant `default-preview`                                                                                         |
| `axis_preview_schema`, `axis_preview_runtime` | The same two parts for `default-preview`                                                                                                     |
| `axis_scenario`                               | A role with `CREATEDB` that owns no database. The publisher gets it as `Scenarios__ServerConnectionString`, so Studio can run test scenarios |

Every role has the development password `axis`. `axis` stays the bootstrap superuser, for setup and
health checks only. Studio runs scenarios only when the server turns on `Studio:Tests`: E2E sets
`Studio__Tests=true`, and Compose and `scripts/dev.sh` use `appsettings.Development.json`. See
[database roles](reference/storage.md#database-roles) and
[running test scenarios from Studio](decisions/d55-running-test-scenarios-from-studio.md).

## Development realm and users

Docker Compose, `scripts/dev.sh` and `scripts/e2e.sh` start Keycloak with three realms from
`samples/keycloak`: `axis`, the development realm, `company`, which plays an organization's own
identity provider, and `axis-operators`, the operator realm of the cell console. The
[development users table](reference/security.md#development-users) lists each user of the `axis` and
`axis-operators` realms and the roles they hold.

A Keycloak container that already runs keeps its old realms, because the import skips a realm that
exists. Recreate it, with `docker compose rm -sf keycloak`, to get a change to any realm file.

### The axis realm

The `axis` realm is imported from `samples/keycloak/axis-realm.json`.

- **Users.** The realm holds the [development users](reference/security.md#development-users) except
  the operator realm's, with fixed ids that seed data and E2E journeys name.
- **Roles.** The roles are client roles of the confidential client `axis`. A client-role mapper writes
  them to the `roles` claim, so the claim holds only the sample roles.
- **Password and code.** The development password is `axis` for every user. After the password, the
  sign-in asks for a 6-digit code, as
  [D99](decisions/d99-axis-ships-its-own-identity-provider.md) requires of every account. Each user has
  a fixed secret in the realm file, the `secretData` value of the user's `otp` credential. It is Base32
  and test data like the password. To get a code, add the secret to an authenticator app as a
  time-based account (SHA-1, 6 digits, 30 seconds), or run `oathtool --totp -b <secret>`. The realm
  accepts a code more than once, so tests can sign in the same user several times within 30 seconds.
  Service accounts need no code.
- **Theme.** The password, code, forgot-password and set-password pages are the Axis sign-in theme,
  built into `artifacts/keycloak-theme` and set as the realm's `loginTheme`. The realm allows "Forgot
  password?".
- **Email.** The realm sends its emails, such as a password reset link, to Mailpit, because
  `samples/keycloak/axis-realm.json` names `mailpit:1025`. Keycloak reaches Mailpit by the name
  `mailpit` on the Docker network: Compose has it already, and `scripts/e2e.sh` starts Mailpit and
  Keycloak on a network of their own.
- **The whole identity provider in the Axis theme** _(planned for M33)_. Every other page of the
  identity provider is the Axis theme, the realm's `emailTheme` is `axis`, and the realm sets the
  example legal links and the terms step of [legal](reference/legal.md), so every link and the terms
  page show.
- **The first login flow.** `scripts/dev.sh` and `scripts/e2e.sh` add the `axis first login` flow to
  the realm through the admin API, before the server starts. The realm sync names it on every brokered
  provider, see the [identity provider resource](reference/identity-provider.md#identity-provider-resource).
  Its only step is a required `idp-create-user-if-unique`, so a person who signs in through a brokered
  provider gets a new user and is never linked to an existing one. The flow is not in
  `axis-realm.json`, because a partial `authenticationFlows` import drops Keycloak's built-in flows. A
  Keycloak container that `scripts/dev.sh` reuses gets the flow on the next run, and a realm that has
  it is not changed. If an admin call fails, the script stops and names the step and the HTTP status.
  Docker Compose's own start does not add the flow.
- **An old realm** shows as a server that fails to start with a `401` at the activation token, which means
  the `axis-activation` client is missing, or a `404` at the company issuer's discovery document,
  which means the `company` realm is missing, or a users route that answers `502`, which means the
  `axis-users` client cannot change users.

The realm's clients:

| Client                | Secret                                   | What uses it                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| --------------------- | ---------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `axis`                | `axis-development-secret`                | The server's sign-in of people, as the `default` tenant's OIDC client in the Development and E2E settings. The sample roles are its client roles                                                                                                                                                                                                                                                                                                                                                             |
| `axis-activation`     | `axis-activation-development-secret`     | The server's startup activation. Its service account holds the `realm-management` roles that a tenant's realm grants the same client. The `default` tenant's realm details under `scripts/dev.sh` and E2E name the realm `axis` and this secret, so activation signs in as the client, adds the `site-` client of each sample site, creates the brokered provider `oidc-companyissuer` from the purchase request sample's `company` provider and lists it in the `site-purchasing` client's `axis.providers` |
| `axis-users`          | `axis-users-development-secret`          | The users page. Its service account holds `manage-users`, `view-users` and `query-users`, as the client of a realm that `tenant create` makes does. The `default` tenant's `Oidc:UsersClientSecret` in the Development and E2E settings names this secret, so `GET /api/users` lists the realm's users through it, and the commands that invite, disable, enable and reset the authenticator of a user change them                                                                                           |
| `axis-mcp-test`       | `axis-mcp-test-development-secret`       | An integration test host only, for the [MCP server](reference/mcp-server.md). Its password grant gives the realm's users tokens with the audience `https://localhost/mcp`, so `McpServerTests` signs in as `author` and `requester` through it                                                                                                                                                                                                                                                               |
| `axis-mcp-other-test` | `axis-mcp-other-test-development-secret` | An integration test host only, a second client with the same audience as `axis-mcp-test`, so `McpRateLimitTests` signs in as a second client of one tenant                                                                                                                                                                                                                                                                                                                                                   |
| `axis-fapi`           | Development keys in `samples/keycloak`   | An integration test host only, for the `fapi2` sign-in profile, see [FAPI 2.0 profile](reference/fapi2-profile.md)                                                                                                                                                                                                                                                                                                                                                                                           |

The confidential clients `purchasing-system`, a [machine caller](reference/security.md#machine-callers),
and `reporting-system`, a [service account](reference/security.md#service-accounts), get tokens through
the client credentials grant.

### The company realm

The realm `company` (Example Company), from `samples/keycloak/company-realm.json`, plays an
organization's own identity provider, which the `axis` realm brokers to.

- It asks for no code at sign-in.
- Its confidential client `axis-broker` has the secret `axis-broker-development-secret`, a redirect to
  the `axis` realm's broker endpoint for `oidc-companyissuer`, and a `groups` claim of plain group
  names, such as `employees` rather than `/employees`.
- It holds the group `employees` and one person in it, `pat` (Pat Example, `pat@company.example`),
  with the password `axis`.
- In E2E, Keycloak listens on `AXIS_E2E_KEYCLOAK_PORT` (default 8180) inside and outside its
  container, so the `axis` realm reaches the `company` realm at the address the browser uses.
- **Its own look** _(planned for M33)_. Its login theme `example-company`, in
  `samples/keycloak/themes`, looks like another company's sign-in on purpose, so a person sees that
  they left Axis, and `scripts/dev.sh` copies it beside the Axis theme. It is sample data and never
  ships.

### The operator realm

The realm `axis-operators`, from `samples/keycloak/axis-operators-realm.json`, is the
[operator realm](reference/cell-console.md#sign-in) of a development cell.

- It holds the role `cell-operator` and one user with it, `cell-operator` (Cell Operator), with the
  password `axis` and the Base32 authenticator secret that the file holds. Specs read the secret with
  `totpSecret('cell-operator', 'axis-operators')` in `tests/e2e/totp.ts`.
- Its confidential client `axis-cell-console` has the secret `axis-cell-console-development-secret`
  and one redirect, `https://console.localhost/api/cell/auth/callback`.
- `CONFIGURE_TOTP` is a default required action, so an account made in the realm sets up an
  authenticator at its first sign-in. The browser flow keeps Keycloak's own, which asks for a code only
  from an account that has an authenticator.

### Deployment bindings of the default tenant

Development, Compose, `scripts/dev.sh` and E2E give the `default` tenant these bindings:

| Binding                                | Value                                                  | Read by                                          |
| -------------------------------------- | ------------------------------------------------------ | ------------------------------------------------ |
| `CompanyIssuer`                        | The `company` realm's issuer URL                       | The purchase request sample's `company` provider |
| `CompanyClientId`                      | `axis-broker`                                          | The same                                         |
| `CompanyClientSecret`                  | `axis-broker-development-secret`                       | The same                                         |
| `PurchaseRequestApprovedWebhookUrl`    | The stub's `/webhooks/purchase-request-approved` route | The purchase request sample's approval webhook   |
| `PurchaseRequestApprovedWebhookSecret` | The stub's default webhook secret                      | The same                                         |

### The samples and their users

Each sample uses development users of the `axis` realm, in a site of its own, and declares one
`accounts` identity provider in `identity-providers/accounts.json`, because a site needs one.

| Sample                                              | Site                | Users and roles                                                                                                                                                                                                                                                |
| --------------------------------------------------- | ------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Purchase requests, `samples/apps/purchase-requests` | `/purchasing`       | Declares `organization`, seeds the same employees and departments as leave requests, lets `operator` administer the organization and `department-manager` read it. A request goes to the head of its department. `operator` opens the Approvals inspector page |
| Leave requests, `samples/apps/leave-requests`       | `/leave`            | Reuses `employee` and `department-manager`. Declares `organization`, seeds an employee for each of its users and three departments headed by `finance-reviewer`, `manager` and `manager-2`, and sends a leave request to the head of its department            |
| Service desk, `samples/apps/service-desk`           | `/service-desk`     | Adds `agent` and two `service-desk-lead` users and gives an employee `requester`. `approver` stays in the realm for the service desk E2E, which assigns tickets to it                                                                                          |
| Asset management, `samples/apps/asset-management`   | `/asset-management` | Adds `asset-manager` and `auditor`, and uses `employee` as the holder of assets                                                                                                                                                                                |

- **Sign-in per site.** The purchase request sample also declares the `oidc` provider `company` in
  `identity-providers/company.json`, which brokers to the `company` realm through the
  `axis first login` flow, and its Purchasing site allows both `accounts` and `company`. The service
  desk's site allows only `accounts`. `tests/e2e/specs/site-sign-in-journey.spec.ts` covers three
  sign-ins: `pat` signs in to Purchasing through "Continue with Example Company", `agent` signs in to
  the Service desk with a password and a code, and a forced `kc_idp_hint=oidc-companyissuer` on a
  Service desk sign-in is refused with `/?signIn=failed`.
- **Department budgets.** `tests/e2e/specs/department-budgets-journey.spec.ts` signs in as `manager` to
  create and submit a budget, as `manager-2` to approve it, and as `employee` to be refused.
- **Platform roles.** The `administrator` user holds `tenant-administrator`, so it sees "Users" in the
  shell, and the E2E shell journey uses it. The `platform-operator` user holds `platform-operator`, so
  it sees the [operator console](reference/operator-console.md) in the shell, and the E2E operator
  console journey uses it. The [users page](reference/user-administration.md) lists the development
  users, and the E2E users page journey uses it.
- **Studio.** The `author` role lets the authoring API list and read the resources of every sample
  under `samples/apps/` under `scripts/dev.sh`, and opens the Studio page at
  http://localhost:5173/studio. Studio saves into those committed folders, so
  `git checkout -- samples/apps` reverts an edit. The `publisher` role lets the authoring API publish,
  shows **Publish** on the Studio page and allows activating a release from the release history.
- **Signing in.** Under `scripts/dev.sh` and in the E2E run, the server signs people in through Keycloak at
  `/api/auth/sign-in`, and the session then answers `GET /api/me`. Opening the home page without a
  session starts sign-in at once, and the header offers sign-in on other pages and sign-out once
  signed in. See [sign-in and the session](reference/security.md#sign-in-and-the-session).

### Tenants made by a spec

A spec can make a tenant the way an operator does. `tests/e2e/admin-tool.ts` runs any `Axis.Admin`
command against the E2E stack. It turns `AXIS_E2E_ADMIN_DATABASE` and
`AXIS_E2E_IDENTITY_ADMIN_SECRET`, which `scripts/e2e.sh` gives only to Playwright, into the tool's own
settings, so the server and the worker never inherit them. The tool's realm emails, such as the first
administrator's invitation, go to the run's Mailpit. `tests/e2e/tenant-realm.ts` lists a realm's
clients, adds an account with a password and an authenticator to a tenant's realm, gives a user a realm role and
clears a user's required actions, through the `axis-admin` client.

The run's shared publisher reads the `Tenants` section, so it runs no provisioning jobs. A spec that queues
jobs, such as the cell console spec and the cell quota journey, starts its own publisher with `startPublisher` of the
same file, which reads the tenant directory with a cell's settings and runs the jobs. Both start it, with their server
and browser, through `startCell` of `tests/e2e/cell.ts`, see [the E2E stack](testing.md#the-e2e-stack).

## Hot reload with `scripts/dev.sh`

`scripts/dev.sh` runs the local stack with hot reload, the way to work on Axis day to day. It needs
Docker, .NET and Node, and works on a fresh clone.

### What it starts

1. It installs the SPA packages when `web/node_modules` is missing or `web/package-lock.json` changed
   since the last install. It installs no Playwright browser.
2. It builds the Keycloak theme into `artifacts/keycloak-theme`, which the Compose `keycloak` service
   mounts, and copies the email theme with it. It fails when one of the theme's files is missing.
3. **Sample themes and licences** _(planned for M33)_. It copies the sample themes of
   `samples/keycloak/themes` beside the Axis theme, and writes the list of
   [open source licences](reference/legal.md#open-source-licences) that `/licences` shows.
4. It starts PostgreSQL, Keycloak, Mailpit and the purchasing system stub with Docker Compose, and
   waits until PostgreSQL accepts connections and Keycloak's `axis` realm, Mailpit and the stub
   answer. The first run builds the stub's image.
5. It builds the sample extension package, `samples/packages/PaymentReferences`, into
   `artifacts/extensions`, where the Development settings of the server and the worker point
   `Extensions:Path`. The purchase request sample declares the package, so it does not activate
   without it.
6. It installs the built-in modules, `modules/*`, and the sample modules, `samples/modules/*`, into
   `artifacts/modules/<id>/<version>/`, where the Development settings of the server and the worker
   point `Modules:Path`.
7. It installs the built-in applications, `applications/*`, and any sample template under
   `samples/templates/*`, into `artifacts/templates/<id>/<version>/`, and creates that folder even when
   there are none. The Development and E2E settings of the server point `Templates:Path` there. The
   production images, the Compose example and the Helm chart's server set no `Templates:Path`,
   so Studio lists no template there. In a cell the chart sets it on the publisher for the create job,
   see [the publisher in a cell](reference/installation.md#the-publisher-in-a-cell).
8. It builds the agent host, `src/Axis.Agent`, before the server starts, and runs the built `Axis.Agent.dll`
   at once, not under `dotnet watch`. With no `Ai` settings it runs idle, and it serves its health
   endpoints at `http://127.0.0.1:5209`, see [health checks](reference/health-checks.md#agent-host).
9. It runs the server and the worker under `dotnet watch` and the SPA on Vite, in one terminal. It
   starts the worker once the server has migrated the tenant databases. The SPA proxies `/api` and
   `/health` to the server.
10. It runs one publisher, `axis-admin publisher`, once the server has migrated the tenant databases.
    The publisher runs from the built `Axis.Admin.dll`, not under `dotnet watch`. The script sets its
    tenant settings as environment variables of that process only, because `Axis.Admin` reads no
    `appsettings` file. They cover `default` and `default-preview`, so it runs preview requests too, see
    [publisher](reference/administration.md#publisher).

Each output line starts with `[server]`, `[worker]`, `[web]`, `[publisher]` or `[agent]`, and the script's own
messages start with `[dev]`.

### Settings it sets

- **Studio's new applications.** `scripts/dev.sh` and the Compose server set `Studio:ApplicationsPath`
  to `artifacts/studio-apps`, which `artifacts/` keeps out of git. An exported
  `Studio__ApplicationsPath` replaces that folder. E2E's journey server sets it to a temporary folder.
- **Email.** The worker sends the emails of `notify` steps to Mailpit, as under
  [Docker Compose](#docker-compose). The script sets `Email__Smtp__Host` to `localhost`,
  `Email__Smtp__Port` to Mailpit's SMTP port, `Email__Smtp__UseTls` to `false` and `Email__From` to
  `axis@localhost` for the worker's process only. An exported `Email__*` setting replaces each one, so
  a developer can try a real mail server. The script gives the publisher `Email__*` settings for
  `mailpit:1025`, the address that Keycloak sees, because the publisher writes the realm's mail server
  when it publishes.
- **The preview tenant.** Development, Compose and E2E configure one preview tenant,
  `default-preview`, with `PreviewOf: default`, see
  [preview tenants](architecture.md#preview-tenants). It has its own database `axis_preview` and its
  own roles, `axis_preview_schema` and `axis_preview_runtime`. It has no deployment bindings, so the
  purchase request sample logs `AXC0100` warnings on it, and a connector call there fails. It signs
  people in through the same realm as `default`. The server activates the four samples on it at start,
  and the worker runs it with only its runtime connection string. Its host is `preview.localhost`:
  Compose and `scripts/dev.sh` serve it at `http://preview.localhost:5206`, and `scripts/dev.sh` also
  serves it through the SPA at `http://preview.localhost:5173`. The E2E server and worker serve it on
  `localhost`, while `default` stays on `127.0.0.1`. The realm accepts sign-in and sign-out redirects
  to those addresses.
- **Ports.** `AXIS_POSTGRES_PORT`, `AXIS_KEYCLOAK_PORT`, `AXIS_PURCHASING_PORT`,
  `AXIS_MAILPIT_SMTP_PORT`, `AXIS_MAILPIT_WEB_PORT`, `AXIS_SERVER_PORT`, `AXIS_WEB_PORT` and
  `AXIS_AGENT_HEALTH_PORT` move the eight ports, whose defaults are 5432, 8080, 5207, 1025, 8025, 5206,
  5173 and 5209. When
  `AXIS_POSTGRES_PORT` is set, the script points the server's connection strings and the worker's
  `Tenants__default__ConnectionString` at that port. When `AXIS_KEYCLOAK_PORT` is set, it points the
  server's `Tenants__default__Oidc__Authority` at Keycloak on that port, and its
  `Tenants__default__Bindings__CompanyIssuer__Value` at the `company` realm there. When
  `AXIS_PURCHASING_PORT` is set, it points the server's and the worker's
  `Tenants__default__Bindings__PurchasingSystemUrl__Value`,
  `Tenants__default__Bindings__PurchasingSystemTokenUrl__Value` and
  `Tenants__default__Bindings__PurchaseRequestApprovedWebhookUrl__Value` at the stub on that port.
- **Worker health.** The worker serves `/health/live` and `/health/ready` at `http://127.0.0.1:5208`
  and answers `404` elsewhere. `Health__Urls` moves that address, and two workers on one machine need
  different addresses. An exported `Health__Urls` moves only the worker's address: the script sets the
  agent host's address from `AXIS_AGENT_HEALTH_PORT`.
- **Telemetry.** Neither Docker Compose nor `scripts/dev.sh` starts a telemetry collector, so logs go
  to the console only. Set `OTEL_EXPORTER_OTLP_ENDPOINT` for the server and the worker to export
  traces, metrics and logs, see [telemetry export](reference/host-settings.md#telemetry-export).

### What reloads

- `dotnet watch` applies C# changes, or restarts the server or the worker without a prompt when it
  cannot. Vite applies SPA changes.
- A change to any `*.json` file under an activated application folder restarts the server once, and
  the server activates the edited configuration again. The script watches the `ActivateOnStartup`
  folders itself, and prints a `[dev]` line when it restarts the server. The worker and the SPA keep
  running. If the edited configuration does not activate, the server exits on its own and the script
  stops with a non-zero code.
- A change to the sample extension package's C# code, to the publisher's code, to the agent host's
  code, or to a module or a template needs a restart of the script, because the server reads the module and template catalogs
  only when it starts.

### Stopping

Ctrl+C stops the server, the worker, the publisher, the agent host and the SPA. PostgreSQL keeps running with its
data, and Keycloak, Mailpit and the purchasing system stub keep running: run `docker compose down` to
stop them. If any of the five processes exits on its own, the others stop too and the script exits
non-zero.

## Widget previews

`npm run dev:design-system --prefix web` serves `http://localhost:5174/design-system.html` from
`web/src/designSystem/`, with React Fast Refresh. It needs no Docker, server, database or sign-in. An edit to a
widget swaps in place in the open page. An edit to the previews or the fixtures reloads the page. The query
parameters are the ones in [widget previews](reference/widget-previews.md#the-bundle).

The server serves the harness, the modules it loads and the files in `web/`. Paths that begin with `/api` or
`/health` go to the Axis server, and answer `502` while it is not running. Any other path that names none of
these, such as the one Chrome's developer tools request when they open, answers `404`.

## Formatting

Two commands fix the formatting that `scripts/lint.sh` checks: `dotnet format Axis.slnx` and
`npm run format --prefix web`.
