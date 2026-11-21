# Package runners and application packages

Outside Development, package operations run in package runners, outside the worker. This page holds
the runners, their protocol, customer-hosted runners and their routes, and the packages that an
application keeps in its own folder. What a package is, its manifest, and how it is installed and
loaded are in [extension packages](extension-packages.md).

## Package runners

Outside Development, package operations run outside the worker, in package runners, so that a
package reaches nothing of the host and no binding of another call.

- **The runners.** Each runtime has its own container, whose host, `Axis.PackageHost`, starts a runner process when the worker asks
  and stops it when told. The images and containers are in [images](installation.md#images).
- **The hosts in Compose.** The Compose example runs `package-host-dotnet`, `package-host-python` and `package-host-node` on an internal network `runners` that only they, the worker and
  the publisher share, and the publisher calls only `application-package-host` there. Each gets no settings or secrets and no published port, a read-only root file system apart from `/tmp`, and
  CPU and memory limits, see [on premises](installation-on-premises.md#on-premises).
- **The C#, Node.js and Python hosts in the Helm chart.** The chart runs `<release>-package-host-dotnet`, `<release>-package-host-node` and `<release>-package-host-python`, each as a Deployment, a Service on port 5310 and a
  NetworkPolicy, with the command `host` and a read-only root file system apart from `/tmp`. It has no env entry, no secret and no
  service link, and the worker is pointed at its Service with `Extensions__Runners__Dotnet`, `Extensions__Runners__Node` and `Extensions__Runners__Python`. Its CPU and memory limits come from
  `packageHost.dotnet.resources`, `packageHost.node.resources` and `packageHost.python.resources`, and rendering fails without them. Each NetworkPolicy admits only worker pods, on TCP 5310, lets the host send traffic only to worker
  pods and allows no DNS. It holds only where the network plugin enforces NetworkPolicy, see
  [in the cloud](installation-in-the-cloud.md#in-the-cloud). With `applicationPackageHost.enabled`, the chart also runs
  `<release>-application-package-host` from the same templates, under gVisor, see
  [the sandbox](#an-applications-packages).
- **The Python runner.** For a `start` that names a `python` version, the host runs `python3 -I -B <sdk>/axis_runner.py <version
  folder>`. The host picks the runner from the manifest's `language`, and a version whose manifest is missing or does not match the
  manifest schema starts no runner. The image `axis-package-host-python` holds the SDK at `/app/sdk/python`. The runner moves file
  descriptor 1 aside for the protocol and points it at standard error, so a `print` at import or while an operation runs, and a
  library that writes to the descriptor, never reach the protocol stream. The first line after `start` is `ready`.
- **A worker route to a Python host.** With `Extensions:Isolation` `process`, the worker sends each attempt of a
  `python` version to the host that `Extensions:Runners:Python` names. It uses the same runner pool, egress proxy,
  timeouts and outcomes as for C#.
- **No package code in the worker.** With `Extensions:Isolation` `process`, the worker reads the
  manifests and metadata of the installed C# packages, as the server does, and loads no package
  code. It sends each attempt of a C# call to a runner of the host at `Extensions:Runners:Dotnet`. It sends
  each attempt of a `node` call to a runner of the host at `Extensions:Runners:Node`, and each attempt of a
  `python` call to a runner of the host at `Extensions:Runners:Python`, with `inProcess` too, because the worker
  cannot load JavaScript or Python. The three runtimes share the worker's one egress proxy.
- **One set for each version.** An installation's packages share runners
  across tenants, one set for each installed package version. Each runner
  process runs one call at a time. The worker keeps the runners of a version that it has started, and
  starts a runner only when an attempt needs one. A runner that exits, times out or fails is dropped, and
  the next attempt starts a new one. A runner that the worker finds closed before it uses it is dropped
  too, so an attempt that never ran is not an unknown outcome.
- **The request.** For each attempt the worker sends a runner one `run`
  message, see [The runner protocol](#the-runner-protocol). It holds the stored input and only the
  `url` and `secret` bindings that the operation declares, with the values of the call's tenant. Package
  code and manifests do not change with the language.
- **The answer.** A `result`, a `retryable` and a `failure` give the same outcomes as an operation
  that runs in the worker, see [package call outcomes](connector-and-package-calls.md#package-call-outcomes).
  A `failure` that names a binding fails the call at once, and its error names the operation, the
  package and the binding.
- **Environment, for a runner that Axis hosts.** The environment is empty
  apart from `HTTP_PROXY` and `HTTPS_PROXY`, their lower-case forms, and
  `NODE_USE_ENV_PROXY=1` for Node.js. The container gets no tenant or
  installation settings, a read-only root file system apart from a temporary
  folder, and CPU and memory limits. It sees no tenant's secrets except the
  bindings of the call it runs.
- **Network, for a runner that Axis hosts.** The proxy variables name the
  egress proxy in the worker, with a token for each runner that the worker
  sends in `start`, together with the address of the proxy as the host reaches
  it. The proxy listens on `Extensions:Runners:ProxyPort` and starts with the
  worker's first runner. So the context's HTTP client sends through the proxy, and
  so does a library's own HTTP call, and the library needs no change. The
  proxy admits a `CONNECT` only to the scheme, host and port of a `url`
  binding that the runner's current call declares, and plain `http` only in
  Development. A runner that has no call reaches nothing, and one runner's
  token never admits the hosts of another runner's call. A request without a
  known token gets `407`. The proxy refuses any other request, and a refused
  request fails the attempt at once, even if the package catches the error. The
  error names the operation and the package. It follows no redirects, so a
  redirect to another host is a new request that the proxy checks again. Compose puts the worker, the publisher and the hosts
  on an `internal` network of their own. The Helm chart gives a
  NetworkPolicy that admits traffic to the hosts only from the worker, and to
  the application package host from the publisher too, and lets them reach
  only the proxy port. Every other route is closed, DNS included,
  because the proxy resolves hosts. A library that ignores the proxy variables
  reaches nothing.
- **Timeout.** The operation's `timeout` starts when the worker sends the `run`. At the `timeout` the
  worker closes the connection, which makes the host kill the runner process. The attempt's outcome is
  unknown, see [package call outcomes](connector-and-package-calls.md#package-call-outcomes).
- **Crash.** A runner that closes its connection during an attempt gives an unknown outcome
  too. It is retried only when the operation declares `idempotent: true`.
- **No host.** A host that cannot be reached, that starts no runner, or that is not set makes the
  attempt retryable, because the operation never ran. The error names the setting of the call's runtime.

The server reads `Extensions:Isolation`, and the worker reads all seven settings:

| Setting | Environment variable | Default | Meaning |
| --- | --- | --- | --- |
| `Extensions:Isolation` | `Extensions__Isolation` | `process` | `process` sends C# attempts to the runners. `inProcess` keeps the in-process [loading](extension-packages.md#loading) of C# packages and is allowed only in Development. A `node` or `python` attempt goes to a runner either way. Outside Development `inProcess`, or any other value, stops the server and the worker at start |
| `Extensions:Runners:Dotnet` | `Extensions__Runners__Dotnet` | none | The `host:port` of the C# package host, with a port from 1 to 65535, or `[address]:port` for an IPv6 address. Unset, every C# attempt is retryable. A value that is not a `host:port` stops the worker at start |
| `Extensions:Runners:Node` | `Extensions__Runners__Node` | none | The `host:port` of the Node.js package host, with the same forms. Unset, every `node` attempt is retryable, with `inProcess` too. A value that is not a `host:port` stops the worker at start |
| `Extensions:Runners:Python` | `Extensions__Runners__Python` | none | The `host:port` of the Python package host, with the same forms. Unset, every `python` attempt is retryable, with `inProcess` too. A value that is not a `host:port` stops the worker at start |
| `Extensions:Runners:Application` | `Extensions__Runners__Application` | none | The `host:port` of `application-package-host`, with the same forms. With `Extensions:ApplicationPackages` on, the worker and the publisher need it, see [an application's packages](#an-applications-packages). The worker sends each attempt of an application's package there. Unset, every such attempt is retryable and names the setting, and with the setting off the worker never contacts the host otherwise. A value that is not a `host:port` stops the worker at start, with the setting on or off |
| `Extensions:ApplicationPackages` | `Extensions__ApplicationPackages` | `false` | `true` makes the worker ask `application-package-host` at start whether it runs under gVisor, and stop with exit code 1 when it does not, see [an application's packages](#an-applications-packages). `false` never asks. Any other value stops the worker at start |
| `Extensions:Runners:ProxyPort` | `Extensions__Runners__ProxyPort` | `5311` | The port of the worker's egress proxy for the runners, a whole number from 0 to 65535. `0` picks a free port. Any other value stops the worker at start |

The package host in host mode reads two settings of its own:

| Setting | Environment variable | Default | Meaning |
| --- | --- | --- | --- |
| `PackageHost:Port` | `PackageHost__Port` | `5310` | The port the host listens on, a whole number from 1 to 65535. Any other value stops the host at start with exit code 2 |
| `PackageHost:Address` | `PackageHost__Address` | every interface | The IP address the host listens on. A value that is not an IP address stops the host at start with exit code 2 |

### The runner protocol

The worker and every runner speak one protocol that no language owns. A small SDK for each language
implements the operation context over it.

| Message | Sent by | Fields |
| --- | --- | --- |
| `start` | The worker, to the host | `package`, `version`, `proxy`, the `host:port` of the worker's egress proxy, and `proxyToken`. Optional `tenant` and `files`, as in `build`, and `files` needs `tenant` |
| `ready` | The runner | None |
| `run` | The worker | `operation`, `operationId`, `input`, and `bindings` with `urls` and `secrets`, each mapping a binding name to its value |
| `result` | The runner | `output` |
| `retryable` | The runner | `error`, the name of the error's type |
| `failure` | The runner | `error`, the name of the error's type, or `binding`, the undeclared binding it asked for |
| `usage` | The host, for a runner started from files | `cpuMilliseconds`, a whole number, the runner process's CPU time from the `run` to the answer |
| `stop` | The worker | None |
| `sandbox` | The worker, to the host, and the host's answer | None in the question. `gvisor`, a boolean, in the answer |
| `build` | A caller, to the host | `files`, mapping each relative path of the package, written with `/`, to its content in base64 |
| `built` | The host | `files`, as in `build`, for every file of the built package, and `output`, the lines the build steps wrote |
| `test` | A caller, to the host | `files`, as in `build` |
| `tested` | The host | `tests`, each with the `name` of the test, its `outcome`, `passed` or `failed`, and its `message`, the last line of a failed test's traceback cut at 1000 characters, or null |
| `refused` | The host | `problems`, each with the `file` it is about, a `message` and, when the problem has a place in the file, a `line` and a `column` counted from 1, and `output`, the lines the build steps wrote |

- **Framing.** Each message is one line of JSON in UTF-8, over TCP on the runner network. The connection is neither encrypted nor
  authenticated, and a `run` carries binding secrets, so only the operator's runner network protects it. The host listens on port
  5310, or on `PackageHost:Port`, on every interface, or on the IP address of `PackageHost:Address`, and reads the installed
  versions from `Extensions:Path`, unless a `start` carries `files`. It does not start without that setting, or when the folder does
  not exist, or when `PackageHost:Port` is not a whole number from 1 to 65535, or when `PackageHost:Address` is not an IP address,
  and it exits with code 2 and a message that names the setting. `scripts/dev.sh` and `scripts/e2e.sh` set `PackageHost:Address` to
  `127.0.0.1`, because the protocol has no authentication.
- **One connection, one runner.** The worker opens a connection for each runner and sends `start`. The host starts the runner of the
  manifest's `language`, the C# runner, the Node.js runner or the Python runner, and pipes the connection to the runner's standard
  input and output, line by line for a `start` with `files`, see Usage below. A Node.js runner also gets `NODE_USE_ENV_PROXY=1`, so
  that its own HTTP calls go through the proxy.
- **Start.** A `start` for a version that is not installed, or whose `package`, `version` or `proxy` is not well formed, closes the
  connection and starts no runner. So does a `start` for a version whose runtime is missing from the host's image. The runner
  answers `ready` once it has loaded the version. A `start` with `files` may be as long as a `build`. The host refuses a path by the
  rule of the Build bullet, a missing `tenant`, and a manifest whose id or version differs from the `start`. It then closes the
  connection and starts no runner. Otherwise it writes the files to `/tmp/axis-runner-<random>/<package>/<version>/`, starts the
  runner from there and deletes the folder when the runner ends. It logs the tenant with the runner's process ID, and no file names.
- **Usage.** For a `start` with `files`, the host reads the lines both ways and sends `usage` after each `result`, `retryable` or
  `failure`. It holds the runner process's own CPU time since the `run`, so start and idle time belong to no call, and not the CPU
  of processes that the runner starts. The host drops a `usage` line that the runner writes, because only the host writes one, and a
  runner that has exited gets none. A worker that gets an answer with no `usage` closes the connection. The runner of an installed
  version gets no `usage`. The worker counts the seconds as [package run time a day](tenant-quotas.md#package-run-time-a-day).
- **Sandbox.** A `sandbox` may be the first line of a connection instead of a `start`. The host answers `sandbox` with `gvisor` set,
  `true` only when its kernel log, as `dmesg` reads it, begins with gVisor's `Starting gVisor` line. A kernel log that begins
  otherwise, or a read that is refused, answers `false`. Then the host closes the connection and starts no runner. A `sandbox` that
  already holds `gvisor` closes the connection without an answer.
- **Build.** A `build` may be the first line of a connection instead of a `start`. It may be longer than the other first lines, as
  may a `start` with `files`, up to the limit in `RunnerHost`, because a package with a wheel needs megabytes, and a longer line
  closes the connection. The host writes the files into a temporary copy and runs steps 1, 2, 4, 5 and 6 of [building a
  package](extension-packages.md#building-a-package) on it, reading the manifest as metadata only, so no package code runs in the host. It answers
  `built` with every file of the built copy, or `refused` with each problem. Then it closes the connection. The `message` of a
  problem is the line that the `build` command prints for it. Its `file` is `axis-package.json` for a manifest problem and the
  manifest's lock file for a lock file, install or scan problem. For a Python source that does not compile, it is the `.py` path
  of the file, and the problem has its `line` and `column`. A problem at the manifest or the lock file has neither. The `output`
  of both answers is one line for each step that passed and, for `refused`, then the `message` of each problem. The host writes
  those lines itself, so none holds an absolute path or pip's or Trivy's output. A path that is empty, absolute, holds `\` or an empty, `.` or `..`
  segment is refused, naming the path, before the host writes anything. The `built` answer leaves out symbolic links, because one
  could point outside the copy. The host writes nothing to `Extensions:Path` and deletes its copy whatever the answer. A build that
  cannot run, such as a pip or Trivy that cannot start, is the operator's problem and not the author's, so the host logs why and
  closes the connection with no answer.
- **Test.** A `test` may be the first line of a connection instead of a `start`. Its line limit and its path rules are those of
  `build`. The host builds the package exactly as for a `build`, and a build that fails answers `refused`, with the same `problems`
  and `output`, and runs no test. Then it
  runs the tests of a `python` package in the built copy. A `csharp` or `node` package answers `refused` with one problem at
  `axis-package.json`, saying that its tests do not run yet. The tests are the files that the caller sent whose name matches
  `test_*.py`, in any folder, and the standard library's `unittest` loads and runs them. The host runs them with the
  `PackageBuild:Python` command, `-I -B` and a runner script embedded in `Axis.PackageHost`. The script puts the built copy and then
  `sdk/python` beside the host on `sys.path`, so that a test can import the adapter and the SDK. The process runs in the built copy
  with an empty environment, so it has no proxy and no outbound network. The host stops it after 2 minutes and kills its process
  tree, and a stopped run answers `refused` with one problem at `axis-package.json`. A package with no test files answers `tested`
  with no tests. The answer is `tested`, whose `name` is the `unittest` id of the test, such as
  `test_sample.SampleTests.test_fails`. A skipped test is left out, an expected failure is `passed` and an unexpected success is
  `failed`. A run that ends with no valid result, such as a test that exits the process, answers `refused` at `axis-package.json`. A
  run that cannot start is the operator's problem and gets no answer, as for a `build`. The host logs `Tested package <id>
  <version>.` and deletes its copies whatever the answer. The tests run the author's code in the host's own container, so a caller
  sends `test` only to a host that runs under gVisor, such as `application-package-host`.
- **Standard output.** The runner's standard output holds only protocol lines. What a package writes to the console goes to standard
  error.
- **Runner errors.** An operation that is not in the manifest, an input that does not fit its field types, and an output whose type
  does not fit its field are each a `failure` with the name of the error's type. The name is the full type name for C#, such as
  `System.FormatException`. The Node.js runner names an error by its JavaScript `name`, such as `RangeError`, and answers these
  three with `TypeError`. For Python the name is `module.QualName` with the module left out for a built-in exception, such as
  `ValueError`, `LookupError` and `axis_sdk.RetryableError`.
- **Closing kills the runner.** When the worker closes a connection, at a timeout or at `stop`, the host kills that runner. A runner
  that exits closes its connection, and the worker takes that as an unknown outcome.
- **Values.** `text` is a string. `integer` and `decimal` are strings, so that no language loses precision, and an `integer` is a
  whole number in the signed 64-bit range. `boolean` is a JSON boolean. `date` is `YYYY-MM-DD` and `date-time` is ISO 8601 with an
  offset. A missing value is `null`.
- **No exception message.** No message carries an exception message, because history keeps only the type of an exception, see
  [Capabilities](extension-packages.md#capabilities).
- **The schema.** `sdk/package-runner-protocol.schema.json` holds the exact shape of each message. Every SDK's tests read it.

### Customer-hosted runners

A customer-hosted runner runs in the customer's own network and connects out to the installation with
a credential of its own. It keeps the staging, retries and history of the engine, but none of the runner
limits above, because the customer owns the machine. The code, its secrets and the systems it reaches
never leave the customer, see [D148](../decisions/d148-packages-in-c-typescript-or-python-run-in-runners-that-axis-or-the-customer-hosts.md).

- **Created by the administrator.** The tenant's administrator creates and revokes a runner, see
  [package runners](user-administration.md#package-runners).
- **Its credential.** A confidential client of the tenant's realm that uses the client credentials grant,
  like a service account. Its access token's audience is `axis-package-runner`, so it reaches only the
  runner's routes, and a service account route refuses it because it lacks `Oidc:ServiceAudience`. Axis
  keeps no copy of the secret.
- **The contract stays with Axis.** The worker reads `idempotent` and `timeout` from the manifest, and checks a
  posted result against `output`.
- **The program.** The customer runs `docker run ... axis-package-host-<language> connect`, Axis's own
  [runner image](installation.md#images) with the `connect` command. It reads the built packages from
  `Extensions:Path`, where `build` writes them, and the customer builds each package with the same `build`
  command, which scans its dependencies, see [building a package](extension-packages.md#building-a-package). An instance runs one
  call at a time. Several instances may share one credential, because each take is its own lease. A missing or
  malformed setting, also one of a binding that an installed operation declares, stops it with exit code 2 and a
  message that names the setting. `http` is allowed only in Development.
- **Registering.** The runner sends the manifest of every installed version. The server compares its `id`, `version` and
  `operations` as JSON values with the manifest pinned by every release that is active or has a running or waiting
  instance. A difference, or no such release that pins the version as customer-hosted, answers `409` naming the package
  and the version, and the instance prints that and exits with code 1. So a mismatch shows when the runner starts, and
  not at a call. The author publishes first, and the calls wait `pending` until the runner starts.
- **Upgrades.** _(planned for M37)_ During an upgrade the customer runs both versions side by side until the old instances end.
- **Calls out only.** The runner connects to the tenant's host over HTTPS, and no connection goes from Axis into the
  customer's network. It gets its token from `Connect:TokenUrl` with the client credentials grant and keeps it until a
  minute before it expires. A `401` gets one fresh token and one retry, and a second `401` exits with code 1. It sends no
  DPoP proof, so a tenant under `fapi2` answers its requests `401`.
- **Taking a call and posting its result.** The runner takes a call under a lease and posts the result, see
  [staging a package call](connector-and-package-calls.md#staging-a-package-call) for the lease and the outcomes. It takes only
  calls of its tenant to a package it registered and to the exact version whose manifest the release pins, and waits
  2 seconds after a take that gives no call. It runs the call in the runner of the manifest's language, one for each
  version, with no proxy variables, and posts `result` as `succeeded`, `retryable` as `retryable` and `failure` as
  `failed`. A runner still running at the end of the lease, or one that exits, is killed and no result is posted.
- **Its own secrets.** The take carries no binding value. A `run` carries only the `url` and `secret` bindings that the
  operation declares, answered by `GetHttpClient` and `GetSecret` from the runner's own settings, `Connect:Urls:<binding>`
  and `Connect:Secrets:<binding>`. A `failure` that names a binding posts the error text that the worker records.
- **Revoked.** Every runner request reads the runner's row, so the next request of a revoked runner answers
  `401`, even with a token that has not expired, and every instance that shares the credential exits with code 1.

The `connect` command reads these settings, as environment variables with the names in the table:

| Setting | Environment variable | Default | Meaning |
| --- | --- | --- | --- |
| `Connect:Url` | `Connect__Url` | none | The tenant's `https` address |
| `Connect:TokenUrl` | `Connect__TokenUrl` | none | The `https` token endpoint of the tenant's realm |
| `Connect:ClientId` | `Connect__ClientId` | none | The client id of the runner's credential |
| `Connect:ClientSecret` | `Connect__ClientSecret` | none | The client secret of the runner's credential |
| `Connect:Urls:<binding>` | `Connect__Urls__<binding>` | none | The base address of the `url` binding with that name |
| `Connect:Secrets:<binding>` | `Connect__Secrets__<binding>` | none | The value of the `secret` binding with that name |

#### Routes

| Route | Body | Answer |
| --- | --- | --- |
| `POST /api/package-runner/packages` | `{ "packages": [{ "manifest": {...} }] }` | `204`. It adds to the registered list, or `409` |
| `POST /api/package-runner/calls/take` | `{ "packages": [{ "id": "purchasing-tools", "version": "1.0.0" }] }`, the registered packages that this instance holds | `200` with `{ "leaseId", "package", "version", "operation", "operationId", "input", "leaseUntil" }`, or `204` when no call waits |
| `POST /api/package-runner/leases/{leaseId}/result` | `{ "outcome": "succeeded", "output": {...} }`, `{ "outcome": "retryable" }` or `{ "outcome": "failed", "error": "<exception type>" }` | `204`, also when the output does not fit and the call fails, or when the result is `failed`, or `retryable` with no attempt left. `404` or `409` |

- **The order of the checks.** A request is checked in this order: the tenant of the host, then the token
  (`401` for the signature, the issuer, the expiry, the audience, or a runner row that is missing or
  revoked, with DPoP under `fapi2` as for service accounts), then the body (`400`). A result is then
  checked for its lease: a lease of another runner or an unknown one answers `404`, and a lease that has
  ended or already holds a result answers `409`. A registration is checked against the releases, and a
  manifest that differs from a pin, or has none, answers `409`, see [registering](#customer-hosted-runners).
- **What a take gives.** _(planned for M37)_ A take gives only calls whose release pins the registered manifest of the package.

## An application's packages

An application's package sits in the application's folder, and the tenant's author brings it. It is not
trusted code, so the platform gives it more limits than an installation's package gets.

- **The sandbox.** The runners run under gVisor (`runsc`), in their own container,
  `application-package-host`, beside the worker. The Helm chart sets that pod's `runtimeClassName` and
  Compose sets that service's `runtime`, so the `package-host-*` containers keep the plain runtime. Compose's
  service sits behind the `application-packages` profile, so an installation without gVisor runs the rest.
  In the chart, `applicationPackageHost.enabled`, off by default, runs `<release>-application-package-host`
  from `axis-package-host-node` with the templates of the package hosts. It sets `runtimeClassName` from
  `applicationPackageHost.runtimeClassName`, `gvisor` by default, and points the worker and the publisher at it with
  `Extensions__Runners__Application`. Its NetworkPolicy admits publisher pods on TCP 5310 as well as worker pods, and the
  policies of the other hosts admit worker pods only. The chart does not set `Extensions__ApplicationPackages`.
  gVisor runs on an ordinary Kubernetes node, while a microVM needs nodes that allow nested
  virtualization.
- **`Extensions:ApplicationPackages`.** A server, worker, `activate` and publisher setting, `false` by default.
  With `true`, the worker sends `sandbox` to `application-package-host` at `Extensions:Runners:Application`
  before it reads any tenant or claims any work, and does not start otherwise. The worker exits with code 1 when the
  answer is no, or when none comes within 10 seconds, and a refused connection counts as no answer. An unset or malformed
  `Extensions:Runners:Application` stops it at start too, naming the setting. A value other than `true` or `false` stops
  it as well. The check makes one attempt, and the orchestrator's restart tries again. With `false`, the worker never asks.
  With `false`, an application's package is a compile error at its `packages/<id>/axis-package.json`, reported
  once for each package, when the server activates a folder at start, `activate` compiles it or the publisher compiles a snapshot. So an installation without gVisor refuses it and runs its own packages as before.
  A stored release that holds one still rebuilds, whatever the setting says.
  With `true`, the publisher needs the same `Extensions:Runners:Application`, to send it each package to build or test, and exits
  with code 2 at start when it is unset or malformed.
- **A customer-hosted package and the setting.** The tenant's switch and the compile
  check still allow a [customer-hosted](#customer-hosted-runners) package with the setting `false`, which
  needs no gVisor because gVisor guards only code on Axis's machines. A package that Axis runs is still
  refused, and `AXC0148` is never reported for a customer-hosted one.
- **Its own runners.** The worker sends each attempt of an application's package, in any language, to `application-package-host`,
  with the call's tenant and the built files of the call's release in `start`, see [a built package in the
  release](extension-packages.md#versions-and-pinning). The runners are one set for each tenant, package version and build, with the limits of [package
  runners](#package-runners). A build is a SHA-256 digest of the built files, so changed files never reuse a runner. A runner starts
  when that tenant's call needs one. The worker stops a runner after 5 idle minutes, checked every minute, and the host kills it and
  deletes its files. A release with no built files, such as one `activate` stored, fails the call at once, naming the package and no
  path. Unset `Extensions:Runners:Application` makes a call retryable and names the setting.
- **Quotas.** A call also holds a `concurrentConnectorCalls` lease, as any package call does.
- **CPU seconds.** The worker of a cell counts the CPU seconds of each call to its runners in the tenant's
  [package run time a day](tenant-quotas.md#package-run-time-a-day). A customer-hosted package counts for nothing there.
- **Plans** _(planned for M37)_. In Axis SaaS only a paid plan allows an application's packages, see
  [the catalogue](plans.md#the-catalogue).
- **Switched on by the tenant.** The tenant's administrator turns them on, see
  [application packages](user-administration.md#application-packages), and gives each author the
  `developer` role, see [roles](user-administration.md#roles).
- **In the folder.** `packages/<id>/` holds the package's manifest, adapter, lock file and tests. No file
  in it is a [resource](configuration.md#load). `<id>` is the folder's name, and compilation checks the
  manifest as it checks an installation's, see [the manifest](extension-packages.md#manifest), at `packages/<id>/axis-package.json`.
  An id that an installation's package also uses is `AXC0147`. The application's `packages` list does not
  name it. Steps call it as `<id>/<operation>`, see
  [Package step shape](connector-and-package-calls.md#package-step-shape).
- **A customer-hosted package's folder.** An application's own package is customer-hosted when its folder is
  `packages/<id>/axis-package.json` alone, with `"runner": "customer"`. Another file in it is `AXC0132` at that
  file. Compilation types the steps against the manifest and marks them customer-hosted. Publishing builds
  and scans nothing for it.
- **Built and scanned on publish.** With `Extensions:ApplicationPackages` on, a `publish` sends each
  `packages/<id>/` of its snapshot, except a customer-hosted package's, to `application-package-host` as a `build`, one at a time in id order,
  before it compiles, for a `tenant` and a `preview` target alike. The host builds and scans the package in the
  tenant's sandbox, as [built once](extension-packages.md#what-a-package-is) says, because a dependency's install script is the
  tenant's code. The publisher runs no pip, npm or install script itself. A `refused` answer fails the
  `compile` step with an `AXC0149` diagnostic at `packages/<id>/<file>` for each problem, such as
  `packages/<id>/requirements.txt` for a scan finding or a failed install. A host that refuses the connection,
  closes it without an answer or does not answer within 5 minutes fails it with one `AXC0152` diagnostic at
  `packages/<id>/axis-package.json`, which does not name the host's address. Either way no release is stored,
  and a `publish` to a `tenant` writes `release.publishFailed`. A `built` answer lets the compile go on, and a new release
  stores the built files, see [a built package in the release](extension-packages.md#versions-and-pinning). A `test` that names a package sends that package as a `test`, see [test](authoring-publishing.md#test).
  A `build` request sends its package as a `build`, and stores no release, see [build](authoring-publishing.md#build).
  A `validate`, `accept`, any other `test`, `activate` and the activation at server start do not build
  packages yet. A publish needs the publisher role only.
- **In the release.** The release holds every file of the package as written, and the content hash covers
  them, see [versions and pinning](extension-packages.md#versions-and-pinning) and
  [the release](configuration.md#release).
- **Writing.** A write to a package's files needs the author role, the developer role and the tenant's
  switch on, see [packages in Studio](studio-packages.md#the-authoring-api).
- **No change to a module's code.** A customization layer cannot change a module's code. An
  application that needs other code adds a package of its own and calls it, or
  [ejects](modules-and-templates.md#upgrades-with-module-migrations) the module.

### How a package is written

- **By hand.** An author edits the package's files in their own tools through the authoring API.
- **In Studio** _(planned for M37)_. An author edits the package's files in Studio.
- **By an agent.** An agent writes the package's adapter, manifest and tests over a library that the
  author names. It does so only through an [authoring request](authoring-requests.md#states) and the
  [MCP server's tools](mcp-server.md#tools), under the plan, review and accept of
  [D150](../decisions/d150-each-authors-own-ai-with-axis-counting-no-requests.md), and it never
  publishes.
