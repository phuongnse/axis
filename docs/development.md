# Development

This page says how to run Axis on one machine, what each script does, and the development data the
scripts set up: the database roles, the Keycloak realms and their users, and the preview tenant.
Production installs follow [installation](reference/installation.md), and the short list of commands
is in [AGENTS.md](../AGENTS.md#commands).

## Scripts

The scripts build, check and run Axis, and NexKit and CI run the same ones as their checks. Run them
from the repository root. NexKit and CI run the scripts from `scripts/setup.sh` to
`scripts/smoke.sh`; the others say which workflow runs them, or that they run on demand only.

| Script                          | What it does                                                                                        | Needs           |
| ------------------------------- | --------------------------------------------------------------------------------------------------- | --------------- |
| `scripts/setup.sh`              | Restores .NET and npm packages, the Playwright browser and the pinned Helm tools                    | Network         |
| `scripts/build.sh`              | Builds the SPA with its checks, the licence lists, the Keycloak themes and the solution, and installs the samples in `artifacts/` |                 |
| `scripts/lint.sh`               | Format, analyzer and type checks, the code checks of the SPA and the tests, the docs checks and the Helm lint |                 |
| `scripts/test.sh`               | .NET unit tests, the control plane's unit tests, Vitest and the scripts' own tests                  |                 |
| `scripts/integration.sh`        | .NET tests and every sample's test scenarios on real PostgreSQL and Keycloak                        | Docker          |
| `scripts/e2e.sh`                | Playwright against the full stack                                                                   | Docker          |
| `scripts/smoke.sh`              | Smoke tests of `scripts/dev.sh`, Compose, the production images and the on-premises install         | Docker          |
| `scripts/performance.sh`        | The `*PerformanceTests` classes alone, as the performance workflow runs them                        | Docker          |
| `scripts/scan.sh`               | NuGet and npm vulnerability scan                                                                    | Network         |
| `scripts/scan-images.sh`        | Vulnerability scan of the production images and the identity provider image                         | Docker, network |
| `scripts/docs-audit.mjs`        | The docs audit of the `docs-audit.yml` workflow: picks a run's pages, reads them, opens the issues  | Network         |
| `scripts/check-issue-links.mjs` | Prints the links of open issues to a file or heading of `main` that does not exist                  | Network         |
| `scripts/load.sh`               | The load test against the reference stack                                                           | Docker          |
| `scripts/load-cell.sh`          | The cell load test against a staging cell                                                           | Docker, network |
| `scripts/dev.sh`                | The local stack with hot reload, see [hot reload](#hot-reload-with-scriptsdevsh)                    | Docker          |

### Setup

`scripts/setup.sh` restores the .NET packages, installs the npm packages and the Playwright browser,
and installs pinned Helm, kubeconform and the Kubernetes schemas into `~/.cache/axis-tools`. Each step
and each tool download gets up to 4 tries, with waits of 15, 30 and 60 seconds between them, so a
short refusal such as a `403` is retried. On CI the Playwright system packages install in a step of
their own.

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

#### Bundle budget

The bundle budget keeps the SPA's first load small. The main bundle is the entry script of
`index.html` and the scripts it preloads, gzipped. It may grow by 50 KB over the baseline that
`web/bundle-budget.json` holds. These parts load in chunks of their own, apart from the main bundle:

| Chunk                           | What it holds                                                          |
| ------------------------------- | ---------------------------------------------------------------------- |
| `assets/ChartDrawing-*.js`      | The chart library, loaded lazily                                       |
| `assets/StudioPageDesign-*.js`  | The page designer's canvas and palette, loaded when the Design tab opens |
| `assets/StudioFormDesign-*.js`  | The form designer's Design tab, loaded when it first opens             |
| `assets/StudioProcessDesign-*.js` | The process designer's Design tab, loaded when it first opens        |
| `assets/DragAndDrop-*.js`       | The drag and drop library `@dnd-kit`, which the three designers share  |

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
- that no test selects an `ant-` class, and that every Playwright locator matches a string name or
  text exactly, see [Testing](architecture.md#testing);
- that no file under `web/` writes raw HTML, and that the SPA has no code that a key such as
  `__proto__` can turn into prototype pollution;
- that `web/` uses `AxisButton`, `AxisTable` and the colour tokens in place of Ant Design's own, with
  no `Table` import from `antd` and the other uses within the baseline in
  `scripts/shared-widgets-baseline.json`, which only goes down, see
  [shared widgets](reference/shared-widgets.md);
- that no test outside a `*PerformanceTests` class measures elapsed time;
- the Markdown link and anchor check and the docs structure check, see
  [writing the docs](writing-docs.md#how-the-docs-stay-correct);
- the Helm charts' lint and their check against the Kubernetes schemas.

### Unit tests

`scripts/test.sh` runs the .NET unit tests, `Axis.ControlPlane.Tests` of the control plane included,
Vitest and the tests of the scripts themselves. It first installs the built-in and sample modules
and the built-in applications in `artifacts/`, for the server tests.

### Integration tests

`scripts/integration.sh` builds the Keycloak theme, then runs the .NET tests against real PostgreSQL
and Keycloak through Testcontainers. They also run the test scenarios of every application under
`samples/apps/` on that PostgreSQL, and the control plane's `Axis.ControlPlane.Integration.Tests`.
The performance tests, whose class names end in `PerformanceTests`, stay out: `scripts/performance.sh`
runs them.

- **Arguments** go to `dotnet test`, such as `--filter-class 'Axis.Integration.Tests.Studio*'`, and
  run only the matching tests, without the scenario suites and the control plane suite.
- **`AXIS_INTEGRATION_SHARD`**, such as `1/3`, runs only that shard, see
  [test runs and reports](#test-runs-and-reports).
- **A skip.** The script skips every suite when NexKit gives a commit to compare with and every
  file changed since it is in the `unaffected_by` list of the integration checks in
  `.nexkit/config.json`, see [skipped suites](#skipped-suites).

### End-to-end tests

`scripts/e2e.sh` builds the Keycloak theme, then starts the stack that Playwright runs against, and
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
- makes a throwaway TLS certificate for the run, which the https server of the tenant create spec and
  of the two cell specs use on `AXIS_E2E_TENANT_HTTPS_PORT` (default 5283);
- builds and checks the design-system bundle in `artifacts/design-system` for its spec, see
  [widget previews](#widget-previews).

Arguments go to Playwright, such as `tests/e2e/specs/studio.spec.ts` or `--grep 'publishes'`, and run
only the matching specs, after the sign-in setup. `AXIS_E2E_SHARD`, such as `1/3`, runs only that
shard, and with arguments it runs Playwright's own `--shard` of the matching tests. Playwright and the
services it starts are skipped when NexKit gives a commit to compare with and every file changed
since it is under `docs/`, `.github/` or `.nexkit/` or is a Markdown file. What the E2E stack holds is
in [Testing](architecture.md#testing).

### Smoke tests

`scripts/smoke.sh` runs four smoke tests. It first installs the built-in and sample modules in
`artifacts/modules`, for the `onprem` test.

| Test         | What it does                                                                  |
| ------------ | ----------------------------------------------------------------------------- |
| `dev-script` | Starts `scripts/dev.sh` in a copy of the repository                           |
| `compose`    | Runs the Compose stack                                                        |
| `images`     | Builds and runs the production images, from `scripts/images.test.mjs`         |
| `onprem`     | Installs `deploy/compose/` from the production images for two tenants          |

Each test is skipped when NexKit gives a commit to compare with and every file changed since it is
in the `unaffected_by` list of the smoke check in `.nexkit/config.json`: under `docs/`, `.github/` or
`.nexkit/`, or a Markdown file. CI and NexKit each run the smoke tests as a job of their own.

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

`scripts/load-cell.sh` is the [cell load test](reference/performance.md#cell-load-test). It runs on
demand against a staging cell, with the address of its provisioning API as its only argument, and
never in CI or NexKit.

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

NexKit's checks split the E2E and integration tests into three shards each, `e2e-1` to `e2e-3` and
`integration-1` to `integration-3`, as [D97](decisions/d97-reliable-test-runs.md) says, and
[CI on main](delivery.md#ci-on-main) runs those checks as its jobs. The whole integration suite in one
job came close to the check's 30-minute limit. The smoke tests run in a job of their own, so the shards
hold only their own tests.

- **E2E.** Set `AXIS_E2E_SHARD`, such as `AXIS_E2E_SHARD=2/3 scripts/e2e.sh`. Its Playwright results go
  to `artifacts/test-results/playwright-<n>.xml`. The shards split the specs by how long they run, not
  by how many tests they hold: each spec weighs its seconds in `tests/e2e/spec-durations.json`, and a
  new spec weighs the median. A serial spec, such as a Studio journey, runs on one worker from start
  to end, so the split gives a long one a shard with little else. When the shards drift apart by
  minutes, refresh the durations from the JUnit files of a full run on `main`:
  `node scripts/lib/e2e-shard.mjs --durations artifacts/test-results/playwright-*.xml`.
- **Integration.** Set `AXIS_INTEGRATION_SHARD`, such as `AXIS_INTEGRATION_SHARD=2/3 scripts/integration.sh`.
  A shard runs whole test classes of `Axis.Integration.Tests`, which `scripts/lib/integration-shard.mjs`
  gives it: classes that share an xUnit collection stay together, and the heaviest go first to the
  lightest shard. A class weighs its seconds in `tests/Axis.Integration.Tests/class-durations.json`,
  or, without an entry, its number of tests times the median seconds per test. Shard 1 also runs
  `Axis.ControlPlane.Integration.Tests`, which the split counts as its seconds under that suite's name
  in the same file. Shard 1 starts with those seconds, so it gets fewer classes. The last shard runs
  every class that the other shards do not, so a class the split does not find still runs. The
  scenario suites are expected only in the shard that holds `ScenarioRunnerTests`, which writes them.
  A shard cannot be combined with arguments. When the shards drift apart by minutes, refresh the
  durations from the JUnit files of a full run on `main`: each shard's `Axis.Integration.Tests.xml`
  and shard 1's `Axis.ControlPlane.Integration.Tests.xml`, with
  `node scripts/lib/integration-shard.mjs --durations <those files>`.
- **Performance tests.** A performance test class name ends in `PerformanceTests`. It stays out of
  `scripts/integration.sh`, so neither NexKit's checks nor CI's integration jobs run it, and
  `scripts/performance.sh` runs it. The class joins its project's performance collection, which runs
  alone, one class at a time, and a guard test fails when a `*PerformanceTests` class is outside that
  collection.

### Results and logs

Each suite writes a JUnit file to `artifacts/test-results/`, and the E2E run writes its logs to
`artifacts/logs/`:

| Log                         | Output of                    |
| --------------------------- | ---------------------------- |
| `e2e-server.log`            | The E2E server               |
| `e2e-worker.log`            | The E2E worker               |
| `e2e-journey-server.log`    | The E2E journey server       |
| `e2e-publisher.log`         | The E2E publisher            |
| `e2e-purchasing.log`        | The purchasing system stub   |

`scripts/test-report.mjs` then prints a summary per suite and the failed tests with their
`file:line`. On GitHub Actions the summary goes to the job summary, and each failed test becomes an
error annotation. The failed tests are also listed at the end of the job log, with each message cut to
20 lines.

Each application's test scenarios are a suite of their own, named `scenarios-<application name>`.
The integration tests run them and write `artifacts/test-results/scenarios-<application name>.xml`.
A failure names the scenario and the step. To run them by hand, run
`dotnet run --project src/Axis.Scenarios -- <folder> --server <cs> --extensions artifacts/extensions --modules artifacts/modules`.

### Flaky tests

Playwright does not retry a failed test, on CI or locally, so a flaky test fails its job, and the
trace of a failed test is kept. On `main`, NexKit runs a failed job once more, and a run that passes
only on that second attempt is recorded on a `bug` issue.

A test that fails at times is fixed by its cause, for every test with that cause, as
[D97](decisions/d97-reliable-test-runs.md) says. The plan of its `bug` issue names the cause's
class. When that class has failed a test before, the change fixes or guards every test of the
class, through a shared helper, a fixture or a lint rule. The classes seen so far and their guards:

| Cause                                      | Guard                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| ------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| An outside download or service             | `scripts/setup.sh`, the CI install steps and the Docker builds of the smoke tests try again                                                                                                                                                                                                                                                                                                                                                                       |
| A click during an animation or a redraw    | Playwright asks for reduced motion, and the SPA then shows dialogs and overlays without animation. `waitForDialog`, `confirmDialog` and the drag helper wait until the element is ready, and the drag helper and the binding choice in the journeys wait until the page canvas has drawn the dropped draft (`waitForCanvas`). A spec that slows animations on purpose, to prove a wait, asks for motion again with `test.use({ reducedMotion: 'no-preference' })` |
| The real clock                             | A test fixes the time with `axis.now` and runs schedules in UTC                                                                                                                                                                                                                                                                                                                                                                                                   |
| State left by another test                 | A test counts from its own baseline                                                                                                                                                                                                                                                                                                                                                                                                                               |
| Planner statistics                         | A seed that a plan depends on runs `ANALYZE` over every row                                                                                                                                                                                                                                                                                                                                                                                                       |
| The MinIO test container cuts a connection | `MinioFixture.ReadObjectAsync` reads an object and `MinioFixture.UploadObjectAsync` uploads one, and each tries again from the start up to five times when the connection drops or MinIO answers that the client disconnected, waiting half a second, then 1, 2 and 4 seconds                                                                                                                                                                                     |

## Production images and Axis.Admin

The production images and the admin tool are the same in development as in an install. The
`Dockerfile` has four production targets, `axis-server`, `axis-worker`, `axis-admin` and
`axis-controlplane`, which hold no samples, development settings or source control metadata. Build one
with `docker build --target axis-server --build-arg AXIS_VERSION=<version> .`. The default target is
the development server. The `images` and `onprem` smoke tests build each image up to three times, with
waits of 15 and then 30 seconds between attempts, because a download in the build can fail at times.

`Axis.Admin` is the console tool that the platform operator runs. It creates, suspends, resumes,
lists, backs up and restores tenants, and it migrates and activates them. Run it with
`dotnet run --project src/Axis.Admin -- <command>`, or with the `axis-admin` image. See
[administration](reference/administration.md). The production install is in `deploy/compose/`
and `deploy/helm/axis/`, see [installation](reference/installation.md).

## Docker Compose

Docker Compose runs the whole stack from source, with nothing but Docker installed. Run
`docker compose up --build`. It starts:

| Service                   | Address                  | Port setting             |
| ------------------------- | ------------------------ | ------------------------ |
| The server, with the four samples | http://localhost:5206 | `AXIS_SERVER_PORT`  |
| PostgreSQL                | `localhost:5432`         | `AXIS_POSTGRES_PORT`     |
| Keycloak, with the [development realms](#development-realm-and-users) | http://localhost:8080 | `AXIS_KEYCLOAK_PORT` |
| The purchasing system stub | http://localhost:5207   | `AXIS_PURCHASING_PORT`   |
| Mailpit, SMTP             | `localhost:1025`         | `AXIS_MAILPIT_SMTP_PORT` |
| Mailpit, web page         | http://localhost:8025    | `AXIS_MAILPIT_WEB_PORT`  |
| The worker                | none                     |                          |
| The publisher             | none                     |                          |

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
  [purchasing system stub](architecture.md#purchasing-system-stub).
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

| Role or database        | What it is for                                                                                       |
| ----------------------- | ---------------------------------------------------------------------------------------------------- |
| `axis_schema`           | Owns the tenant database. The server uses it for `SchemaConnectionString` and `ConnectionStrings:PlatformSchema` |
| `axis_runtime`          | Every other connection string of the server, and the worker's only one. The worker gets no `SchemaConnectionString` |
| `axis_preview`          | The database of the preview tenant `default-preview`                                                  |
| `axis_preview_schema`, `axis_preview_runtime` | The same two parts for `default-preview`                                        |
| `axis_scenario`         | A role with `CREATEDB` that owns no database. The publisher gets it as `Scenarios__ServerConnectionString`, so Studio can run test scenarios |

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
- **The whole identity provider in the Axis theme** _(planned for M31)_. Every other page of the
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

| Client           | Secret                               | What uses it                                                                                                                                                                                                                                                                                                                                     |
| ---------------- | ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `axis`           | `axis-development-secret`            | The server's sign-in of people, as the `default` tenant's OIDC client in the Development and E2E settings. The sample roles are its client roles                                                                                                                                                                                                 |
| `axis-activation` | `axis-activation-development-secret` | The server's startup activation. Its service account holds the `realm-management` roles that a tenant's realm grants the same client. The `default` tenant's realm details under `scripts/dev.sh` and E2E name the realm `axis` and this secret, so activation signs in as the client, adds the `site-` client of each sample site, creates the brokered provider `oidc-companyissuer` from the purchase request sample's `company` provider and lists it in the `site-purchasing` client's `axis.providers` |
| `axis-users`     | `axis-users-development-secret`      | The users page. Its service account holds `manage-users`, `view-users` and `query-users`, as the client of a realm that `tenant create` makes does. The `default` tenant's `Oidc:UsersClientSecret` in the Development and E2E settings names this secret, so `GET /api/users` lists the realm's users through it, and the commands that invite, disable, enable and reset the authenticator of a user change them |
| `axis-mcp-test`  | `axis-mcp-test-development-secret`   | An integration test host only, for the [MCP server](reference/mcp-server.md). Its password grant gives the realm's users tokens with the audience `https://localhost/mcp`, so `McpServerTests` signs in as `author` and `requester` through it |
| `axis-fapi`      | Development keys in `samples/keycloak` | An integration test host only, for the `fapi2` sign-in profile, see [FAPI 2.0 profile](reference/fapi2-profile.md)                                                                                                                                                                                                                                |

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
- **Its own look** _(planned for M31)_. Its login theme `example-company`, in
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

| Binding                                | Value                                                         | Read by                                       |
| -------------------------------------- | ------------------------------------------------------------- | --------------------------------------------- |
| `CompanyIssuer`                        | The `company` realm's issuer URL                              | The purchase request sample's `company` provider |
| `CompanyClientId`                      | `axis-broker`                                                 | The same                                      |
| `CompanyClientSecret`                  | `axis-broker-development-secret`                              | The same                                      |
| `PurchaseRequestApprovedWebhookUrl`    | The stub's `/webhooks/purchase-request-approved` route        | The purchase request sample's approval webhook |
| `PurchaseRequestApprovedWebhookSecret` | The stub's default webhook secret                             | The same                                      |

### The samples and their users

Each sample uses development users of the `axis` realm, in a site of its own, and declares one
`accounts` identity provider in `identity-providers/accounts.json`, because a site needs one.

| Sample                                                      | Site                | Users and roles                                                                                                                                                                                         |
| ----------------------------------------------------------- | ------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Purchase requests, `samples/apps/purchase-requests`         | `/purchasing`       | Declares `organization`, seeds the same employees and departments as leave requests, lets `operator` administer the organization and `department-manager` read it. A request goes to the head of its department. `operator` opens the Approvals inspector page |
| Leave requests, `samples/apps/leave-requests`               | `/leave`            | Reuses `employee` and `department-manager`. Declares `organization`, seeds an employee for each of its users and three departments headed by `finance-reviewer`, `manager` and `manager-2`, and sends a leave request to the head of its department |
| Service desk, `samples/apps/service-desk`                   | `/service-desk`     | Adds `agent` and two `service-desk-lead` users and gives an employee `requester`. `approver` stays in the realm for the service desk E2E, which assigns tickets to it                                     |
| Asset management, `samples/apps/asset-management`           | `/asset-management` | Adds `asset-manager` and `auditor`, and uses `employee` as the holder of assets                                                                                                                          |

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
and browser, through `startCell` of `tests/e2e/cell.ts`, see [the E2E stack](architecture.md#the-e2e-stack).

## Hot reload with `scripts/dev.sh`

`scripts/dev.sh` runs the local stack with hot reload, the way to work on Axis day to day. It needs
Docker, .NET and Node, and works on a fresh clone.

### What it starts

1. It installs the SPA packages when `web/node_modules` is missing or `web/package-lock.json` changed
   since the last install. It installs no Playwright browser.
2. It builds the Keycloak theme into `artifacts/keycloak-theme`, which the Compose `keycloak` service
   mounts, and copies the email theme with it. It fails when one of the theme's files is missing.
3. **Sample themes and licences** _(planned for M31)_. It copies the sample themes of
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
   production images and `deploy/` set no `Templates:Path`, so they list no template.
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

## Running a host on its own

A host can also run alone, outside `scripts/dev.sh`. To run the worker, run
`dotnet run --project src/Axis.Worker`. It reads its tenants from its `Tenants` section, as the server
does, and runs their due work items. It waits until the server has migrated each tenant database, so
either can start first. If you moved PostgreSQL with `AXIS_POSTGRES_PORT`, override
`Tenants__default__ConnectionString` for the worker.

`dotnet run` of the server or the worker stops at start when a folder it reads does not exist. Create
them first:

- `Extensions:Path`: build the sample extension package, with `scripts/build.sh` or
  `dotnet build samples/packages/PaymentReferences/PaymentReferences.csproj`;
- `Modules:Path`: install the built-in and sample modules, with `scripts/build.sh` or
  `bash scripts/lib/install-modules.sh`;
- `Templates:Path`, the server only: `bash scripts/lib/install-sample-templates.sh` creates it.

## Activating other application folders

The development server activates the four samples at start, and can activate any other folder.
`src/Axis.Server/appsettings.Development.json` lists the purchase request sample,
`../../samples/apps/purchase-requests`, the leave request sample, `../../samples/apps/leave-requests`,
the service desk sample, `../../samples/apps/service-desk`, and the asset management sample,
`../../samples/apps/asset-management`, in `ActivateOnStartup`. So `scripts/dev.sh` migrates every
tenant database, then compiles and activates all four samples before it listens, and stops on any
diagnostic.

To activate another application folder, add it to that list, or set `ActivateOnStartup__0=<folder>`
to activate it in place of the purchase request sample, while the other samples stay active. Relative
paths are resolved from `src/Axis.Server`.

## Widget previews

The design system has a live preview of every widget, drawn by the real widget components in light
and dark mode, so the Design System artifact in Claude shows what the SPA draws, see
[D67](decisions/d67-live-widget-previews-in-the-design-system.md) and
[design changes](delivery.md#design-changes). The previews are one bundle, built from
`web/src/designSystem/`. It feeds fixture page metadata to the widgets and answers their API calls
from fixtures, only for the API paths of the fixture application, so it needs no server, database or
sign-in. Every other request goes to the page's own fetch, unchanged.

### The bundle

`scripts/design-system.sh` builds the bundle into `artifacts/design-system/axis-design-system.js` and
checks it with `scripts/check-design-system.mjs`: one classic script, no `import`, no literal
`</script` or `<!--`, at most 6 MB, and no HTML file in the folder. It needs no Playwright browser and
is not a NexKit check. The local agent session publishes the bundle as the Design System artifact.
The bundle carries its own React and never uses one that the page provides, because React 19 ships no
UMD build.

- **Mounting.** The bundle sets `window.Axis`. `window.Axis.<name>(element)` mounts one preview into an
  element. It draws every state of the preview in one card, each under its caption, and returns a
  function that unmounts them all, see
  [D95](decisions/d95-widget-previews-show-the-states-a-design-change-touches.md).
- **Theme.** A mounted widget follows `data-theme` on the root element of the page: `dark` is dark
  mode, and any other value or none is light mode. It also follows the token custom properties of the
  root element, such as `--primary`, see [frontend](reference/frontend.md).
- **Forms in a sandbox.** The Design System artifact draws each card in a frame sandboxed without
  `allow-forms`, where the browser blocks a form's own submission before its submit event fires. The
  bundle therefore fires a form's submit event itself, for a click on a submit button, Enter in an
  input and `requestSubmit()`, and cancels the browser's submission.
- **The header line.** The bundle's first line is
  `/* @ds-bundle: {"format":4,"namespace":"Axis","components":[{"name":"TextField"},...]} */`, and the
  Design System artifact reads it. The namespace is `Axis`, and the artifact reads each card from
  `window.Axis`. `scripts/design-system.sh` writes the line with `scripts/design-system-header.mjs`
  after the build, so a sync keeps it. `scripts/check-design-system.mjs` rejects a bundle whose line
  has no format 4, another namespace, or a card missing, named twice or matching no preview.
- **Card width.** Each card's own marker in the artifact sets `width=992`, the
  [narrow width](reference/frontend.md) breakpoint. The artifact scales the card to fit its frame, so
  every card shares one scale and shows the desktop layout. A card never scrolls sideways at that
  width. The states of a card share one `minmax(0, 1fr)` column, so a wide table scrolls inside its
  widget, as it does on a page.
- **The harness.** `web/design-system.html` is the harness. After `scripts/design-system.sh`, open it
  from disk. With no query it mounts every preview, and `?preview=<name>&mode=light|dark` mounts one
  preview in that mode. The Vite dev server does not serve the previews.

### The cards

The header line names every card once, in the order of the groups in
`web/src/designSystem/cardOrder.ts`. A `field-<type>` preview's card is `<Type>Field`, a
`chart-<kind>` preview's is `Chart<Kind>`, and every other card takes its preview's PascalCase name,
such as `Table` or `TaskInbox`. Every preview has at least one state, and a preview with only one shows
it as "Typical". A state can bring its own fixtures, on its own paths in the fixture application. A
change that adds a state to a widget adds that state to the widget's preview in the same change.

| Group           | Previews and their states                                                                                                                                                                                                                                                                                                                                                                                                                            |
| --------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Field inputs    | `field-<type>` for each field type: the input the form widget uses for that type, with its label and the fixture value, laid out as on a form, in three states: Editable, Read-only, and "Required, help text and error", which shows the required mark, a help text and a fixed message at once. `field-child-collection` draws line items with a reference cell and a computed cell, in four states: Reference and computed cells, Cell error, Read-only, and Empty. A field preview has no Save, Cancel or action buttons and no history. Its input works as on a form, and nothing is saved |
| Data widgets    | `table`: With data, with row and toolbar actions and a long-text row, Empty, and Many row actions, which folds a row's actions into the "…" menu. `form`: Validation errors, which submits the form once when it mounts to a fixture that answers with a validation problem, Read-only by a condition, Wizard steps, "Fields in columns, with errors", where fields of several widths share rows and some show messages, and Many steps, a wizard with seven steps, so the steps bar shows numbers. `detail`: With data and Empty |
| Charts and KPIs | `chart-<kind>` for `bar`, `line` and `pie`, and `kpi`, whose comparison is last month's total: each With data and Empty                                                                                                                                                                                                                                                                                                                             |
| Process widgets | `taskInbox`: With data, with an overdue and a claimed task, and Empty. `processInspector`: one state with a failed instance and an open dead letter                                                                                                                                                                                                                                                                                                  |
| Containers      | `columns`, `tabs`, `section`                                                                                                                                                                                                                                                                                                                                                                                                                         |
| Shared states   | `states`: drawn once, the states every widget shares, a table while it loads and a table whose request fails                                                                                                                                                                                                                                                                                                                                         |
| Shell           | `shell`, `signIn` and `identitySignIn`, see [shell](reference/shell.md#design-system-previews). Each draws its states in a frame at least as tall as its height, which grows to hold the whole shell, so the state's menus and dialogs open inside it                                                                                                                                                                                                  |

- **Studio cards** _(planned for M31)_. An eighth group, Studio, follows Shell, with the previews
  `studio`, `studioPageDesign`, `studioFormDesign` and `studioProcessDesign`.

### The spec

`tests/e2e/specs/design-system.spec.ts` mounts each preview from the bundle in the harness, in both
modes, in a 992 px frame sandboxed without `allow-forms`, as the artifact draws it. It fails on a
console error, on a request other than the page that holds the frame, the harness and the bundle, and
on sideways scroll. It also proves that each card's states show, that the cards are named in order,
that a mounted widget follows `data-theme` and `--primary` and a validation message `--dangerText`,
that a form submits inside the sandbox, and that no fixture reaches the server's web root.

## Formatting

Two commands fix the formatting that `scripts/lint.sh` checks: `dotnet format Axis.slnx` and
`npm run format --prefix web`.
