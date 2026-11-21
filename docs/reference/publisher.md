# The publisher

This page describes the `publisher` command of [Axis.Admin](administration.md). The other commands
are on that page. In this page, "database role" always means the PostgreSQL role.

## Publisher

`publisher` runs the [publish requests](authoring-publishing.md#publishing) that Studio stores, and the
provisioning jobs of a cell. It is the only host that activates a release on an author's behalf, so
the server never needs a tenant schema connection. It takes no option and, unlike the other commands,
runs until it is stopped. It has no HTTP endpoint and listens on no port, so nothing can call it.

The `axis-admin` image runs it. Both production installs start one, see
[installation](installation.md), and `scripts/dev.sh`, the Docker Compose file in the repository root
and the E2E script each start one too. It runs `publish`, `validate`,
[`activate`](authoring-publishing.md#publishing), `accept`, `test` and `build` requests under both tenancy sources, and `publish`
requests with the target `preview`, see [preview requests](#preview-requests).

One process runs these loops:

| Loop         | What it does                                                                                                                                                                                                                                                                                                                                                                                     |
| ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Requests     | Claims and runs `publish`, `validate`, `activate` and `accept` requests, see [claiming](#claiming). Only this loop reads the tenants again.                                                                                                                                                                                                                                                      |
| Tests        | Claims and runs `test` requests, which run an application's [test scenarios](test-scenarios.md) from Studio, and `build` requests, see [test](authoring-publishing.md#test) and [build](authoring-publishing.md#build).                                                                                                                                                                                                                                                  |
| Jobs         | Under `Tenancy:Source=directory`, runs the jobs of `platform.jobs`, see [provisioning API](tenant-lifecycle.md#provisioning-api). It claims each job with a lease, runs one job at a time for each tenant and lets a running job finish when it is stopped. A job that it ends writes one [platform audit record](tenant-setting-commands.md#platform-audit-records) that names the job's actor. |
| Host checks  | Checks the pending [host requests](tenant-lifecycle.md#hosts) against DNS every `Cell:HostCheckInterval`, `00:10:00` by default, and adds a verified host to its tenant. It also creates the host's cert-manager `Certificate` when `Cell:HostCertificateIssuer` is set, and the host's Ingress when `Cell:HostRouteService` is set. The record of that add is named `tenant host verify` and names the operator or the tenant administrator who asked for the host.                     |
| Export sweep | Runs only when the `Cell:ExportStorage` settings are set. Every hour it deletes each ZIP under `exports/` in that bucket that was stored more than 7 days ago, see [exporting a tenant](tenant-lifecycle.md#exporting-a-tenant), and writes one line for each deletion. A failed round writes one line, and the next round tries again.                                                          |

The publisher writes no other audit record of its own, except the `tenant purge` record of the
[purge job](#purge-job) it queues itself: the audit of a publish is the release's own record in the
tenant database. Every loop writes to the same output.

**Settings.** It reads these, with the meaning they have for the other commands:

- `Tenancy:Source`, `configuration` or `directory`. An unset value is `configuration`, as for the
  server and the worker. Any other value exits with code 2 with a message that names the setting.
  - Under `directory` it reads `ConnectionStrings:PlatformSchema` and `Platform:SecretKey`, and
    decrypts the schema role of each tenant, see [database roles](storage.md#database-roles).
  - Under `configuration` it reads the `Tenants` section, as the server does, and connects to each
    tenant with its `SchemaConnectionString`. It seeds over its `ConnectionString`. It also reads
    `DOTNET_ENVIRONMENT`. A `Tenants` section that is not valid, or a tenant with no
    `SchemaConnectionString`, exits with code 2. The message names the setting and never holds a
    value.
- `Tenancy:RefreshInterval`, how often it reads the tenant directory again. It reads and checks it
  only under `directory`. The `Tenants` section never changes while the process runs.
- `Extensions:Path` and `Modules:Path`, as `activate` reads them. An unset path gives an empty
  catalog, and a set path whose folder does not exist is a settings error.
- `Activation:InlineRecomputeLimit` and `Data:ActivationTimeout`, as for `activate`.
- `Extensions:ApplicationPackages` and `Extensions:Runners:Application`, see [an application's packages](package-runners.md#an-applications-packages). With the first `true`, a missing or malformed second exits with code 2.
- `Cell:HostCheckInterval`. A value that is not a positive time span exits with code `2`.
- In a cell (`Cell:Id` set), the settings of the [create](#create-job) and [purge](#purge-job) jobs:
  `Admin:ConnectionString`, the `IdentityProvider` settings, `Cell:BackupKeyFile`,
  `Cell:TenantDomain`, `Studio:ApplicationsPath` and `Templates:Path`. The publisher reads them
  without stopping. When one is missing or wrong, it still starts and runs its other requests, and
  only the create or purge job fails, at the first step that needs the setting, with a message that
  names it. A missing `Studio:ApplicationsPath` does not stop it, but one that cannot be created
  does, as the next item says. It reads the `Email` block anyway.
- The Studio settings, `Studio:Folders`, `Studio:ApplicationsPath` and `Studio:RevisionsPath`, and
  `ConnectionStrings:Platform` for the write lock, as [Studio settings](host-settings.md#studio-settings)
  says. A Studio setting that stops the server exits with code `2`.
- `Scenarios:ServerConnectionString`, the role that creates the scenario databases of `test` requests
  and of the contract scenarios of `publish` and `validate` requests. It is optional, and an empty
  value counts as not set. The publisher starts without it, and a `test` request, or a `publish` or
  `validate` with contract scenarios to run, then fails with `AXC0217`, which names the setting. A
  value that is not a connection string exits with code 2, and the message names the setting and
  never holds the value.

A missing or invalid setting exits with code 2 before any request runs. The publisher polls once a
second, a fixed interval with no setting.

**Which tenants.** It serves only `active` tenants whose schema version is the one of this build, and
it reads the directory again every `Tenancy:RefreshInterval`. A `suspended`, `provisioning` or out of
date tenant is skipped, and its requests stay `queued`. It runs no migration of the tenant schema
itself, and neither does `activate`.

Under `configuration` it serves each tenant of the `Tenants` section once the server has migrated
that tenant's database, as the worker does. A tenant counts as migrated when none of the
configuration, data and processes contexts has a pending migration. Until then it writes
`<tenant> waiting: the database is not migrated yet.` once, skips the tenant and checks again each
poll round. When the database is migrated it writes `<tenant> ready` once, and claims requests from
then on. It does not check a tenant again after that, so a later schema change needs a restart of the
publisher. The `directory` source writes neither line.

### Claiming

Each poll round, for each served tenant, the requests loop claims the oldest `queued` `publish`,
`validate`, `activate` or `accept` request with `FOR UPDATE SKIP LOCKED`, and the tests loop does the same for
`test` and `build` requests. A claim sets the request `running`, `claimed_by` to the publisher's own name and
`lease_until`, and adds 1 to `claims`. A `running` request whose lease has run out is claimed the same
way.

- **Test requests.** `test` and `build` requests form their own queue for each tenant and application. At most
  one of them runs at a time, oldest first, independent of the other kinds. A `test` or `build` does not wait behind a
  running `publish`, `validate`, `activate` or `accept`, and none of them waits behind a `test` or `build`, so a long test
  holds up no publish. The claim that skips an application with a `running` request therefore counts
  only the requests of the same queue.
- **Target.** A request with target `tenant` is claimed as above. A request with target `preview` is
  claimed only when it is a `publish`, and only when the tenant has no preview tenant, or when the
  publisher serves that preview tenant. A `validate`, `activate` or `accept` with target `preview` is never
  claimed, and stays `queued`. A preview request that is not claimed does not block the later
  requests of its application.
- **One at a time.** For one tenant and application it runs one request at a time, oldest first, so
  when two publishes are queued the newer one ends up live, and an `activate` that is queued after a
  publish runs after it. A `validate` waits behind a running `publish` of the same application. A
  claim skips an application that already has a `running` request. Requests of other applications
  and tenants can run at the same time. Each claim takes a lock of the tenant database before it
  reads the queue, so two publishers cannot claim two requests of one application at the same moment.
- **Lease.** The lease is five minutes, fixed. The publisher renews it every third of the lease while
  the request runs, so a long request is not claimed twice. A `running` request whose lease has run
  out is claimed again by any publisher, because its publisher died. A request whose publisher lost
  its tenant database connection is claimed again in the next round, without waiting for the lease. A
  request that has been claimed three times and whose lease runs out again ends `failed` with
  `AXC0215`, see the [lifecycle](authoring-publishing.md#lifecycle). A `publish` gets a
  `release.publishFailed` audit record with it, an `activate` gets a `release.activateFailed` one, and
  a `validate` and an `accept` get none.
- **Fencing.** The `claims` count is the fencing token. Renewal and the final update change the
  request only while its status is `running` and `claims` is still the value this publisher claimed.
  A publisher that lost its claim writes a `claim failed` line and no audit record.
- **Two publishers.** More than one publisher is safe, because the claim skips rows that another
  publisher holds. The production installs run one.

### Steps for each request

Each step writes its own line, as in [output and exit codes](administration.md#output-and-exit-codes), and runs as the
tenant's schema role unless it says otherwise.

1. `claim`: claim the request, as above. A tenant with nothing to claim writes no line. A claim that
   fails writes `<tenant> claim failed: <message>`, and the publisher goes on with the next tenant.
2. `version`: check that the tenant's schema version is the one of this build.
3. `contracts`, for `publish`, `validate` and `accept` only: run the contract scenarios of every pinned module.
   The step compiles the snapshot, or for an `accept` the applied draft, in memory to find them. A snapshot that cannot be read or does not
   compile, and an application whose modules have no contract scenario, run nothing and write no
   `contracts` line, so the next step reports the problem. Otherwise the step fails with `AXC0217`
   when the setting is empty and at least one contract scenario is left to run.
   - A contract scenario that covers a point its module has relaxed, see
     [relax entries](module-contracts.md#relax-entries), is skipped. It writes
     `<tenant> scenario <name> skipped: …` with the point, and its result is
     `{ name, file, outcome: "skipped", point }`. When every scenario is skipped, the step needs no
     scenario server.
   - The others run, each in its own database, as for a `test`, whatever the outcome of the ones
     before. Each writes a `<tenant> scenario <name> passed` or `failed` line, and its outcome goes in
     the request's `scenario_results`. Each failing scenario adds one `AXC0342`, and the step writes
     `<tenant> contracts failed: AXC0342 …` for each, then ends the request with nothing stored or
     activated.
   - The scenarios may run for 10 minutes, counted from the first one of each claim, and past that
     the request fails with `AXC0219`. A stop of the publisher does not cut them short.
4. For `publish`: `compile` the snapshot against the tenant database and store its release, `activate` the release, then `seed` and `indexes`. These are
   the steps of [activate](administration.md#activate) and report the same diagnostics. `seed` runs as the runtime role, and `indexes` as the schema role. The `activate`
   step also updates the tenant's realm after the activation has committed, as in [activate](administration.md#activate). The compile reads the snapshot's files, never
   the application folder on any disk. With `Extensions:ApplicationPackages` on, `compile` first builds each package in `application-package-host`, see
   [an application's packages](package-runners.md#an-applications-packages). A snapshot that cannot be read, because it is not an array of
   `{ file, version, content }` or names a file twice, fails at `compile` and stores no release. Activation adds a row to the [release
   history](publish-request-storage.md#release-history) with the requesting user, the label, the note and the request id, unless the release is already
   active.
5. For `validate`: the step `validate`, the only one after `version` and `contracts`. It runs the
   `compile` and every check of `activate` against the tenant inside one transaction, then rolls the
   transaction back. It stores no release, adds no history row, seeds nothing, starts no schedule
   trigger, builds no index and prints no `indexes` line. It reports the diagnostics of activation,
   so a clean `validate` means an activation with the same input would pass its checks, apart from
   the seed. A snapshot that cannot be read fails at `validate`. The transaction holds the schema
   lock until it rolls back, so other activations of the tenant wait for it.
6. For `activate`: the steps are `version`, `load`, `activate`, `seed` and `indexes`, and none of
   them runs a contract scenario. The `load` step is the one that loads the stored release, and
   the `activate` step is the one that activates it.
   - The `load` step reads the stored release the request names and rebuilds its model from the
     stored files, as the server does when it serves a release. It reads no folder, compiles nothing
     and stores no new release. It refuses a release that pins a package version that is not
     installed under `Extensions:Path`, with `AXC0146`. A release that is missing, or that belongs to
     another application, fails the step too.
   - The `activate` step then runs every activation check, so a migration that was applied after the
     release and is missing from it is `AXC0174`, and a field that the release lacks but the tables
     hold is `AXC0015`. A refused `activate` request changes nothing. The activation adds a row to the
     [release history](publish-request-storage.md#release-history) with the requesting user and the request id, no
     label and no note, unless the release is already active. The step then updates the tenant's
     realm to match the activated release.
   - `seed` then sets the schedule triggers and the seed records of that release, and `indexes`
     builds the indexes of that release that its tables lack. See
     [activating a release](authoring-publishing.md#activating-a-release).
7. For `test`: `version`, then the step `test`. It checks, in this order, that the setting is set
   (`AXC0217`), that the snapshot can be read, that it compiles, and that every name in the request
   is a scenario of the application (`AXC0218`, once for each unknown name). The first check that
   fails ends the request with its diagnostics, and no database is created. Then, for each chosen
   scenario in file order, or for every scenario when the request names none, one after another:
   1. create the database `axis_scenario_<uuid>` over `Scenarios:ServerConnectionString`;
   2. migrate it, compile and activate the snapshot there, and apply its seed data;
   3. run the steps with the stubs, as `Axis.Scenarios` does;
   4. drop the database, pass or fail.

   Every step of a scenario runs as the scenario role, and the publisher drops only databases it
   created. The limit on work items in one step applies to each scenario, see
   [test scenarios](test-scenarios.md). Every compile diagnostic, warnings included, is stored in
   `diagnostics`, and a failing scenario is stored only in `scenarios`. The step writes
   `<tenant> scenario <name> passed` or `<tenant> scenario <name> failed: <failure>` for each
   scenario, and fails when one failed. It then stores the outcomes of the scenarios and the request's
   status over the tenant's schema role, the only use of the tenant database by a `test`. The
   scenarios of a request may run for 10 minutes, counted from the first scenario of each claim. Past
   that, the scenario that runs fails with a message that names the limit, its database is dropped,
   and the request ends `failed` with `AXC0219`. The scenarios that finished keep their outcomes, and
   those that had not started get none. The step writes `<tenant> test failed: AXC0219 …`. A `test`
   gets no audit record and no release id.

   A `test` request that names a package runs only the step `package`, in place of `version` and
   `test`, and needs no `Scenarios:ServerConnectionString`. It sends that package to
   `application-package-host` as a `test`, see [test](authoring-publishing.md#test). It writes
   `<tenant> package ok`, `<tenant> package failed: <n> of <m> tests failed.` or, for each
   diagnostic, `<tenant> package failed: <code> <message>`, then stores the outcomes in
   `package_tests` and the status over the tenant's schema role.

   A `build` request runs only the step `build`. It sends the named package to `application-package-host` as a
   `build`, see [build](authoring-publishing.md#build), and stores no release and no audit record. It writes
   `<tenant> build ok` or, for each diagnostic, `<tenant> build failed: <code> <message>`, then stores `output` and
   the status over the tenant's schema role. It needs no scenario server.

8. For `accept`: the steps are `draft`, `version`, `contracts`, `validate`, `test` and `accept`, in the order
   of [accepting a draft](revisions-and-drafts.md#accepting-a-draft), and the publisher holds the
   folder's write lock from `draft` to `accept`. It reads the folder named by `Studio:Folders` and
   writes it, so it needs the Studio settings and `ConnectionStrings:Platform`.
   - `draft` finds the folder, takes the lock, records any outside change and loads the draft as the
     requester, then checks the draft's versions against `main`. It writes `<tenant> draft ok`. A
     missing Studio setting or lock setting, an unknown folder, a folder whose manifest `id` is not the
     request's application, a draft that is not the requester's or does not exist, and a lock that
     cannot be taken write `<tenant> draft failed: <message>` with no diagnostic code. Each resource
     or package file that changed on `main` since the draft used it writes
     `<tenant> draft failed: AXC0377 <message>`, and the step ends the request.
   - `version`, `contracts` and `validate` are as for a `validate`, and run on the folder's files
     with the draft applied, in memory. They store no release and change nothing.
   - `test` runs only the application's own scenarios on those files, as for a `test` request and with
     the same 10-minute limit, which ends the request `failed` with `AXC0219`. An application with no
     scenario has no `test` step and needs no scenario server. Otherwise the step fails with
     `AXC0217` when the setting is empty. A stop never cuts the scenarios short. Each outcome goes in
     the request's `scenario_results` after the contract scenarios, and a failing scenario writes
     `<tenant> test failed: …` and ends the request with nothing written.
   - `accept` writes the changed files to the folder, commits them as one revision and deletes the
     draft. It writes `<tenant> accept ok`, or `<tenant> accept failed: <message>` when a file cannot
     be written or the revision cannot be recorded.
   - An `accept` has no release audit record and no release id. A failure before the `accept` step leaves the
     folder and `main` as they were.
9. Write the audit record of a `publish` or an `activate`, with the request's final status, then set the
   request `succeeded` or `failed` and store its diagnostics and its release id, in one transaction. A
   `validate` and an `accept` get their final status and diagnostics the same way, and neither has a release
   id. A `validate` has no audit record. When an `accept` succeeds, the same transaction also moves the
   `ready` authoring request of its draft to `accepted`, erases its refresh token and writes an
   `authoringRequest.accepted` audit record, see [accepting](authoring-requests.md#accepting). The
   diagnostics are all of them, warnings included, as in the [authoring API](authoring.md#diagnostics). A
   request that fails with an exception rather than diagnostics, such as a lost connection, stores only the
   diagnostics gathered so far, which can be none.

Activating the same content hash twice changes nothing, so a request that runs again does no harm,
and an activation of the release that is already active adds no history row.

### Preview requests

A request with target `preview` is stored in the main tenant's database, see
[preview](authoring-publishing.md#preview), and only a `publish` is claimed with that target.

- **Where it runs.** The publisher runs its steps against the main tenant's preview tenant, with that
  tenant's database, schema role and bindings. It compiles, activates and seeds there, and adds the
  history row to the preview tenant's release history with the request id. A preview tenant has no
  activation secret, so the `activate` step updates no realm. The step lines name the preview tenant,
  for example `default-preview compile ok`.
- **What it leaves.** It writes no audit record. It sets the request `succeeded` or `failed` in the
  main tenant's database, with the renewal of the lease, and leaves the main tenant's release, release
  history and audit records as they were.
- **Refusals.** It fails the request with `AXC0216` when the main tenant has no preview tenant, and
  writes `<tenant> preview failed: AXC0216 <message>`. A preview tenant that exists but is not served,
  because it is not `active`, not at this build's schema version, or, under `configuration`, not
  migrated yet, leaves its requests `queued` until it is served. A preview request that runs out of
  claims ends `failed` with `AXC0215` and gets no audit record.
- **Queue.** Preview and main tenant requests of one application share the one-at-a-time queue of the
  main tenant's database.

A `validate` and a `test` never update a realm.

### Failures and stopping

A request fails on its own and does not stop the others, and a failed request is not an exit code,
because it is stored on the request.

- **Failures.** The previously active release stays active after a failed `compile` or `activate`. A
  `seed` failure happens after activation has committed, so the new release stays active and the
  request ends `failed` with its `releaseId`, as with `activate`. So does an `indexes` failure, and so
  does a failure of the realm update: the new release stays active, and `seed` and `indexes` do not
  run until the next activation of the tenant. The publisher calls the identity provider of each
  tenant that has a realm, so it must reach it and trust its certificate.
- **Stopping.** On `SIGTERM` or Ctrl+C the publisher stops claiming, finishes the request it is
  running, then exits. A `publish`, `validate`, `activate` or `accept` finishes first, and so do the contract
  scenarios of a `publish`, `validate` or `accept`, and the test scenarios of an `accept`. A running `test` or `build` request is the exception, because a test
  may run for 10 minutes, which is longer than the stop grace period of both installs: the publisher
  stops it at once, the running scenario fails, its scenario database is dropped, and the request
  ends `failed` with `AXC0220`. The step writes `<tenant> test failed: AXC0220 …`, or `<tenant> build failed: AXC0220 …` for a `build`.
- **A stop mid-request.** It leaves a lease that runs out, and another publisher then claims the
  request again. So the platform's shutdown grace period must be longer than the longest `publish`,
  `validate`, `activate` or `accept`, or the request is claimed twice.
- **Leftover databases.** A publisher that crashes in the middle of a scenario leaves its
  `axis_scenario_<uuid>` database behind. The publisher does not clean it up, because with several
  publishers none can tell which leftover database is still in use. The operator drops the
  `axis_scenario_*` databases that the scenario role owns, once no publisher runs a `test`.

**Exit codes.** `0` after a stop on `SIGTERM` or Ctrl+C. `1` when it cannot read its tenants at start
or stops on an error that it cannot recover from. `2` for a usage error or a missing or bad setting,
which includes a tenant under `configuration` that has no `SchemaConnectionString`.

### Create job

In a cell, the publisher runs the `create` job that `POST /api/cell/tenants` queues, see
[creating a tenant](tenant-lifecycle.md#creating-a-tenant). Outside a cell it knows no `create` kind,
because the provisioning API answers `404` there. The job has five steps, and each can run again:

1. `tenant`: runs the code of `tenant create` for the host `<id>.<Cell:TenantDomain>`, with no
   administrator and the cell's backup key. The tenant stays `provisioning`, so no host serves it.
   When the job's input has `quotas`, the step writes them as the tenant's quota rows in the entry's
   transaction, and a rerun replaces the rows with them. The step also records the tenant's
   application on its `platform.tenants` row in that transaction: the job's `application`, or, when
   it has none, the name of the blank application.
   The step writes no audit record of its own, because the job writes one when it ends. A rerun on a
   `provisioning` entry reuses the realm, the secrets and the keys. A refusal that `tenant create`
   gives before it does any work, such as a taken host or an id that is already an active tenant,
   fails the job at once. Any other failure ends the claim.
2. `application`: copies the newest version of the template in the job's `application` into
   `<Studio:ApplicationsPath>/<id>/<kebab-case name>`, as Studio creates an application from a
   template. The copy keeps the template's application name and gets new resource ids. It is written
   to a hidden temporary folder and then renamed. The step is skipped when the folder already holds
   an application with that name. A template that is not installed fails the job at once. A job with
   no `application` writes the blank application of Studio's Blank instead, named as
   [creating a tenant](tenant-lifecycle.md#creating-a-tenant) says, and needs no template.
3. `activate`: compiles that folder and activates its release in the tenant, as `activate` does,
   while the tenant is still `provisioning`. It writes no `activate` audit record. A failed activation
   fails the job at once.
4. `invite`: invites the administrator in the job's `administrator`, as
   `tenant create --administrator` does. It finds the account by its email before it creates one. The
   invite records name `tenant create` as their actor.
5. `resume`: sets the tenant `active`. A tenant that is already `active` is ok.

- **No application.** Without an `application`, the `application` step creates a blank one with the
  code of Studio's Blank, and `activate` activates its first release.

A step that throws ends the claim, and the next claim runs the job again from the first step that has
not succeeded. A job gets three claims, and an operator runs a failed job again through a new job, see
[running a failed job again](tenant-lifecycle.md#jobs).

### Export job

The publisher runs the `export` job that `POST /api/cell/tenants/{id}/exports` queues. It knows the
kind in a cell or not, because only the provisioning API queues it. The job has one step, `export`.
It reads the tenant's active releases and records in one snapshot, writes the ZIP of
[exporting a tenant](tenant-lifecycle.md#exporting-a-tenant) to a folder that only its owner can use,
uploads it to the `Cell:ExportStorage` bucket and deletes the folder.

- A tenant that is `provisioning`, unknown or a preview tenant fails the job at once. A missing
  `Cell:ExportStorage` setting does the same and names the setting.
- In a cell the job takes a `concurrentImports` lease of the tenant before it reads anything and holds
  it until the ZIP is stored. When none is free it waits and runs in a later round, without using one
  of its three claims, see [exporting a tenant](tenant-lifecycle.md#exporting-a-tenant).
- An upload that fails ends the claim, and the job runs again.
- The export sweep deletes the ZIP 7 days after it was stored, see [publisher](#publisher).

### Purge job

In a cell, the publisher queues and runs the `purge` job, which carries out step 3 of
[deleting a tenant](tenant-lifecycle.md#deleting-a-tenant). Outside a cell it knows no `purge` kind,
because the provisioning API answers `404` there, so no `delete` job and no `delete_after` exist.

- **Queueing.** No route queues the job. In each round of its jobs loop, in the same transaction and
  under the same lock as a claim, the publisher queues one `purge` job for each main tenant, never a
  preview tenant, whose `delete_after` has passed and that has no queued or running `purge` job. The
  job's actor is the actor of the tenant's latest succeeded `delete` job, and it is also the `actor`
  in the job's input, which the tombstone names. A tenant with no succeeded `delete` job gets none. A
  tenant whose deletion was cancelled has no `delete_after`, so it is never purged and keeps all its
  data.
- **Steps.** The job has the eight steps of `TenantPurge`, in order: `hosts`, `database`, `realm`,
  `files`, `studio`, `keys`, `rows` and `directory`. The `hosts` step also deletes the cert-manager
  `Certificate` of each host in the tombstone, and reads `Cell:HostCertificateIssuer` to know whether the
  cell has any. Blank, it deletes none. In a cell with `Cell:HostRouteService` set, `hosts` also deletes
  the Ingress of each host of the tombstone, and a `404` counts as done. When the job ends it writes one
  platform audit record, `tenant purge`, with the actor of the job.
- **Settings.** It reads the admin database role and the identity provider admin client of the
  [create job](#create-job), and `Studio:ApplicationsPath`. When one of them is missing, the job fails
  at its first step with a message that names the setting, removes nothing, and the publisher keeps
  running. A missing `Studio:ApplicationsPath` is never read as no folder. A missing
  `Cell:BackupKeyFile` fails the job too, because the purge reads the create job's settings, though
  it needs no backup key.
- **Failures.** A failed `purge` job is queued again no sooner than an hour after it ended, so a
  lasting fault adds one failed job and one audit record for the tenant each hour until an operator
  fixes it. An operator cannot run a failed `purge` job again, see
  [running a failed job again](tenant-lifecycle.md#jobs). A step that throws ends the claim, as for the other jobs.
