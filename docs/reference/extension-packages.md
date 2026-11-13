# Extension packages

An extension package holds custom code that an application calls where configuration is not
enough. The code may be written in C#, TypeScript or JavaScript on Node.js, or Python. This page
covers what a package is, its manifest, the contracts it builds on, how it is installed, versioned,
pinned and loaded, what its operations may reach, and what those limits do not protect against. An
application's own packages are in [An application's packages](#an-applications-packages). How a
process calls a package operation is in [package operations](process-execution.md#package-operations),
and how an application declares its packages is in
[declaring packages](#declaring-packages-in-an-application). The reason for keeping custom code out of
configuration is in [knowledge](../domain/knowledge.md#logic-in-configuration).

## What a package is

A package is a versioned unit of code that adds typed operations. An installation's package is built
and installed apart from any application, see [Installation](#installation).

- **Custom code.** A package holds logic that does not fit expressions,
  such as file parsing or a complex calculation. It adds typed operations
  that a process step calls.
- **A C# package is a class library.** It is built as a .NET class library,
  with its own unit tests and a SemVer version.
- **One reference.** A C# package references only `Axis.Extensions`, the
  contracts assembly described in [Contracts](#contracts).
- **Build artifact.** A C# package's build gives a folder with its assemblies
  and a manifest, `axis-package.json`.
- **Three languages** _(planned for M21)_. A package is written in C#,
  TypeScript or JavaScript on Node.js, or Python.
- **A folder with a lock file** _(planned for M21)_. A package of any of
  these languages is a folder with a manifest, a thin adapter in that
  language and a lock file of its dependencies.
- **Reuses a library** _(planned for M21)_. The dependencies of a package may
  be the business's own libraries, from a public or private registry, as
  built files or as a commit of a git repository. The adapter maps an
  operation to a function of the library, and the library's code does not
  change.
- **Built once** _(planned for M21)_. A package is built once, when it is
  installed or published, see [Building a package](#building-a-package). Its
  dependencies are installed from the lock file and scanned for known
  vulnerabilities. A runner downloads nothing at run time.
- **No data access.** A package cannot read or write entities.
- **Widgets** _(planned for M22)_. A package may also hold custom widgets,
  React components that a page names, which build on Axis widgets and Axis tokens, never on Ant
  Design directly. See [custom widgets](custom-widgets.md).

## Manifest

The manifest, `axis-package.json`, sits at the root of the package folder and declares the package's
identity, its operations and, for a C# package, its main assembly. This example uses an illustrative package,
`purchasing-tools`. The [sample package](#sample-and-test-packages),
`payment-references`, has a manifest of the same shape.

```json
{
  "id": "purchasing-tools",
  "version": "1.0.0",
  "label": "Purchasing tools",
  "assembly": "PurchasingTools.dll",
  "lock": "packages.lock.json",
  "operations": [
    {
      "name": "normalizeSupplierCode",
      "type": "PurchasingTools.NormalizeSupplierCode",
      "idempotent": true,
      "timeout": "PT10S",
      "input": [{ "name": "code", "type": "text", "required": true }],
      "output": [{ "name": "code", "type": "text", "required": true }],
      "capabilities": { "urls": [], "secrets": [] }
    }
  ]
}
```

- **`id`.** The package id. It has lower-case ASCII letters, digits and
  hyphens, and starts with a letter. A process step names an operation as
  `<id>/<operation>`.
- **`version`.** A SemVer version in plain `major.minor.patch` form, such as
  `1.2.0`.
- **`label`.** The package's name for people, 1 to 100 characters and
  required, as a module's is, so Studio never names a package by its id. A
  manifest without one is refused with `PackageManifestInvalid`, and the package
  is not loaded.
- **`assembly`.** For a C# package, the file name of its main assembly, in
  the package folder.
- **`language`.** `csharp`, `node` or `python`. It defaults to `csharp`, and
  `assembly` is required exactly when it is `csharp`. `axis-package.schema.json` holds the rules of
  this field, `adapter` and `lock`.
- **`adapter`.** The adapter file in the package folder: a JavaScript module for
  `node`, where TypeScript is compiled before the build, or a `.py` module for `python`. It is refused
  for `csharp`, whose adapter is its `assembly`.
- **`lock`.** Required. The language's lock file: `packages.lock.json`,
  `package-lock.json`, or `requirements.txt` with a `--hash` on every line. The schema refuses the name of
  another language's lock file.
- **`assembly` left out** _(planned for M22)_. A package that holds only widgets has no
  `assembly`.
- **`widgets`** _(planned for M22)_. The custom widgets the package offers,
  each with a `name`, an `entry`, a `label`, `properties` and an `sdk` range.
  See [widgets in a package manifest](custom-widgets.md#widgets-in-a-package-manifest).
- **`operations`.** The operations the package offers. Each has these:
  - `name` is the operation's name, unique in the package.
  - `type` is, for a C# package, the full name of a class in the main assembly
    that implements `IOperation`. The class is not abstract and has a public parameterless
    constructor, because the host creates each instance itself.
    - **For `node`.** `type` names a function that the adapter exports, see [contracts](#contracts).
    - **For `python`.** `type` names a function that the adapter module defines, see
      [Contracts](#contracts).
  - `idempotent` says whether running the operation twice with the same
    operation identity has the same effect as running it once. It decides
    whether an unknown outcome is retried, see
    [Call outcomes](process-execution.md#package-call-outcomes).
  - `timeout` is an ISO 8601 duration. It defaults to `PT30S` and is at most
    `PT2M`, as for a [connector](integration-resources.md#connector-resource).
    Operation names are unique in the package, and field names are unique in
    each operation's `input` and in its `output`, all ignoring letter case.
  - `input` and `output` list typed fields. Their types are the
    [rule parameter types](resource-files.md#resource-file-shape): `text`,
    `integer`, `decimal`, `boolean`, `date` and `date-time`. A field may be
    `required`.
  - `capabilities` lists what the operation may ask its context for:
    `urls` names `url` bindings and `secrets` names `secret` bindings. Both
    are empty when absent. See [Capabilities](#capabilities).

## Contracts

For a C# package, `Axis.Extensions` is the only assembly the package references, and it holds only
these types:

```csharp
public interface IOperation
{
    Task<IReadOnlyDictionary<string, object?>> RunAsync(
        IReadOnlyDictionary<string, object?> input,
        IOperationContext context,
        CancellationToken cancellationToken);
}

public interface IOperationContext
{
    Guid OperationId { get; } // the idempotency key, the same on every attempt
    HttpClient GetHttpClient(string urlBinding);
    string GetSecret(string secretBinding);
}

public class RetryableException : Exception { }
```

- **`IOperation`.** The class named by an operation's `type`. `RunAsync`
  gets the evaluated input and returns the output, both keyed by field name.
- **`IOperationContext`.** What the platform gives an operation. Its
  `OperationId` is the operation identity of the call, so an operation can
  pass it on as an idempotency key.
- **`RetryableException`.** Thrown by an operation to say that the attempt
  may succeed later. Any other exception fails the call, see
  [Call outcomes](process-execution.md#package-call-outcomes).
- **The Node.js SDK.** `sdk/node/` holds the Node.js runner and a small SDK that implements the operation
  context over [the runner protocol](#the-runner-protocol), which no language owns. An adapter written as
  CommonJS or as an ES module exports one function for each operation `type`, and the runner calls it with
  `(input, context)`. The context has `operationId`, `fetch(urlBinding, path, init)` and
  `getSecret(secretBinding)`. `fetch` resolves `path` against the base of the declared `url` binding and
  sends no request to an origin that none of the operation's `url` bindings declares. It follows no redirect.
  Asking for an undeclared binding throws, and the call fails with that binding even when the adapter catches
  the error. An error named `RetryableError` says that the attempt may succeed later, as
  `RetryableException` does, and any other error fails the call. Values keep their protocol form, so
  `integer` and `decimal` are strings and a JavaScript number does not fit an `integer` field.
- **A Python package.** `sdk/python/` holds the Python runner and a small SDK, the module `axis_sdk`, which uses only the
  Python standard library. An operation's
  `type` names a synchronous function of the adapter. It gets the input and the context, and returns the output,
  both dicts keyed by field name. It maps the operation onto a library that does not change:

  ```python
  from axis_sdk import RetryableError
  from purchasing import normalize

  def normalize_supplier_code(input, context):
      try:
          return {"code": normalize(input["code"])}
      except ConnectionError:
          raise RetryableError()
  ```

  `context.operation_id` is a `uuid.UUID`, `context.get_secret(name)` returns a secret and
  `context.get_http_client(name).request(method, url, headers=, body=)` returns the `urllib` response. The
  client resolves `url` against the binding's value, follows no redirects and returns a response with any
  status instead of raising. Binding names match ignoring letter case.
- **Value types.** For a C# or Python package, input and output values use these types:

  | Field type | .NET type | Python type |
  | --- | --- | --- |
  | `text` | `string` | `str` |
  | `integer` | `long` | `int`, in the 64-bit range |
  | `decimal` | `decimal` | `Decimal`, or `int` for an output |
  | `boolean` | `bool` | `bool` |
  | `date` | `DateOnly` | `datetime.date` |
  | `date-time` | `DateTimeOffset` | `datetime` with a time zone |

  A missing value is `null` in C# and `None` in Python.

- **Published** _(planned for M22)_. `Axis.Extensions` is published as a NuGet package, at a 0.x
  version with no compatibility promise, see [the SDK](custom-widgets.md#the-sdk).

## Installation

The operator installs an installation's packages once for the whole installation, in a folder that
every host reads. An installation's package is trusted code that the operator reviews.

- **Inside a module** _(planned for M21)_. A module installed for the installation can ship a package,
  such as a built-in module or a partner's module, and the operator reviews that package too.
- **Per installation.** Packages are installed once for the installation,
  not per tenant. Every tenant sees the same installed packages. An
  application uses only the packages it declares.
- **Where.** The server and the worker both read the setting
  `Extensions:Path`. Each package version is installed in
  `<Path>/<id>/<version>/`, which holds `axis-package.json` and the package's
  built files, such as the assemblies of a C# package. A relative path is
  resolved from the host's content root.
- **No setting, no packages.** Without `Extensions:Path`, a host has no
  packages. If the setting names a folder that does not exist, the host does
  not start.
- **Both hosts.** The server and the worker must hold the same packages. The
  server compiles and activates against them, and the worker runs them.
- **The package hosts.** The Compose example's container `package-host-dotnet` and the Helm chart's pods
  `<release>-package-host-dotnet` and `<release>-package-host-node` mount the same folder read-only, and
  each runner runs the packages of its language, see [Package runners](#package-runners).
- **The other package hosts** _(planned for M21)_. Compose's container `package-host-node` and the containers `package-host-python` do the same
  for the other languages.
- **The Python host image.** The image `axis-package-host-python` holds the Python runner, see
  [Package runners](#package-runners).
- **Restart.** Each host reads the folder at startup. Installing a new
  version needs a restart of both hosts.
- **Keep pinned versions.** Keep every version that an active release, or a
  release with a running or waiting instance, pins. Removing one stops the
  start of both hosts, see [Versions and pinning](#versions-and-pinning).

### Building a package

The operator builds a C#, Node.js or Python package once, with `Axis.PackageHost build <source>`. The source is the
folder that the developer published in locked mode. For `node` it holds the compiled adapter, `package.json` and
`package-lock.json`. For Python it is the folder of the adapter and `requirements.txt`. The build writes the folder
that `Extensions:Path` names and compiles nothing.

- **In the host image.** The build runs in the package's own host image,
  `axis-package-host-<language>`, because dependencies, native modules included, install with the runtime
  and system libraries that run them, and install-time code never runs in the admin host.

  ```sh
  docker run --rm -v <source>:/source:ro -v <extensions>:/extensions \
    axis-package-host-<language> build /source
  ```

  Each image holds `Axis.PackageHost`, Trivy at the version that `scripts/lib/trivy.sh` pins, and one runtime:
  .NET for `dotnet`, Node.js 24 with npm for `node`, and Python 3 with pip for `python`. The image sets
  `Extensions__Path=/extensions`, so the command needs no other setting, and runs as UID 1654, so the
  `<extensions>` folder must be writable by that user. The build needs a writable root file system, see
  [images](installation.md#images).

- **Checks, in this order.** The first refusal stops the build, so a later step never runs after it.
  1. The manifest, as [Loading](#loading) checks it, with `AXC0132` to `AXC0138`. Each problem is printed
     with its code, file and JSON Pointer. The folder name check is left out, because the build names the
     folder from the manifest.
  2. The lock file, which the manifest's `lock` names: `packages.lock.json` for C#, `package-lock.json` for
     `node` and `requirements.txt` for Python.
  3. An existing `<id>/<version>/` folder.
  4. For `node` and Python, the install.
  5. The scan.
- **Lock file.** A missing lock file is refused. A lock file is stale when the runtime dependency list,
  the `.deps.json` beside the assembly, names a NuGet package whose version the lock file does not
  resolve, and a stale lock file is refused. A lock file entry that has no runtime asset, such as an
  analyzer, is allowed. A missing `.deps.json` is refused too, because the lock file cannot be checked
  without it. For `node`, a missing `package.json` or `package-lock.json` is refused, and so is a
  `package-lock.json` that does not list the dependencies of `package.json` at the same ranges. The
  check compares `dependencies`, `devDependencies`, `optionalDependencies` and `peerDependencies`
  with the lock file's root entry, in both directions, before npm runs.
- **Install** (`node`). It copies the source to a temporary folder and runs `npm ci --ignore-scripts` there,
  with `npm` from the `PATH`. The install reads the lock file alone, and no install script of a
  dependency ever runs. The temporary folder is deleted afterwards, so the source never changes. A
  failed install is refused and prints npm's error output.
- **Install** (Python). It copies the source to a temporary folder and runs
  `python3 -m pip install --require-hashes --no-deps --target <copy> -r requirements.txt` there, so the
  installed modules and their `.dist-info` folders sit in the package folder next to the adapter. pip's own
  `--require-hashes` is a second check: a dependency whose file does not match its hash is refused, with a
  message that names `requirements.txt`. pip's output is never printed, because it holds absolute paths. The
  scan and the write then use the copy, and the copy is deleted at the end. A pip run that fails otherwise is
  a refusal too. The setting `PackageBuild:Python` names the Python command that runs pip, which defaults to
  `python3`. A pip that cannot start is a setup error.
- **Scan.** It scans the dependencies with Trivy and refuses a vulnerability of high or critical
  severity, naming the dependency. For `node` and Python it scans the installed copy. The setting
  `PackageBuild:Trivy` names the Trivy command, which defaults to `trivy`. A scan that cannot run or
  prints no valid report is a setup error and never a pass.
- **Writes.** After every check passes, it copies the package to `<Extensions:Path>/<id>/<version>/`.
  It never overwrites an existing folder. Then it loads the extensions folder as the hosts do, and if
  that load rejects the new version it deletes the version again and refuses.
- **Exit code.** `0` when the package is built, `1` when it is refused and `2` for a setup error: a wrong
  argument, a missing source or extensions folder, an install that cannot start, or a scan that did not
  complete.
- **Python hashes.** Every requirement line of `requirements.txt` must carry a `--hash`, and a line without
  one is refused before anything is installed, with the line number and the dependency. The file is read as
  pip reads it: a comment and a blank line are skipped and a line that ends in a backslash continues on the
  next line. An option line such as `--index-url` needs no hash. A line that starts with `-r`, `-c` or `-e`,
  or their long forms, is refused, because it brings in requirements that the check never sees.
- **Registry credentials** _(planned for M21)_. They are mounted for the build alone, and the runners
  never see them.

The build reads these settings:

| Setting | Environment variable | Default | Meaning |
| --- | --- | --- | --- |
| `Extensions:Path` | `Extensions__Path` | none | The existing folder that the build writes the package to |
| `PackageBuild:Trivy` | `PackageBuild__Trivy` | `trivy` | The Trivy command that scans the dependencies |
| `PackageBuild:Python` | `PackageBuild__Python` | `python3` | The Python command that runs pip for a Python package |

## Versions and pinning

A release pins one exact version of each package it uses, so a running instance keeps the version
that its release was compiled against.

- **Ranges.** An application's `packages` entry gives a version range: an exact version, a `^`
  range or a `~` range, as [declaring packages](#declaring-packages-in-an-application) says.
- **Pinning.** Compilation picks the highest installed version in each range
  and pins that exact version in the release. The pin is part of the
  release's content hash. So once a newer version in range is installed,
  compiling the same folder again gives a new release pinned to it, while the
  old release keeps its pin. A restart of the server compiles its startup
  folders again, so it can activate such a new release.
- **Unknown package.** A package id with no installed version is `AXC0139`.
- **No version in range.** A range that no installed version meets is
  `AXC0141`. Its message lists the installed versions.
- **Steps.** A package operation step is checked against the pinned
  version's manifest, see
  [Package step shape](process-execution.md#package-step-shape), and the compiled
  step holds that version. A running instance keeps the version its release
  pins, even after a newer version is installed. A host rebuilds a stored
  release against the manifests of its installed packages, so a release with
  a package operation step cannot be rebuilt on a host that lacks its pinned
  version. The content hash covers the pins and not the manifest contents.
- **Pinned version missing at start.** Activation compiles the folder
  against the versions that loaded, so the pin it activates is always
  installed. At the start of the server and the worker, every active release
  and every release with a running or waiting instance is rebuilt. A pinned
  version that is not installed is `AXC0146` and stops the start with a
  diagnostic naming the tenant, application, release and package version. The
  worker process then exits with a non-zero code. An older release with no
  running or waiting instance is not checked. The worker checks a tenant once
  its database is migrated, and the server skips a tenant database that is not
  migrated or cannot be reached.
- **Pinned version missing on the worker.** A call to a version that the
  worker does not hold is a retryable attempt. Nothing ran, as with a missing
  binding on a connector call. The call is retried under the step's retry
  policy, and the instance fails when attempts run out. The call stores the
  policy when it is staged, because the worker cannot rebuild the release
  without the version.
- **An application's packages** _(planned for M37)_. The release holds the
  built packages of the application, so running work keeps the code it
  started with, see [An application's packages](#an-applications-packages).

## Loading

Each host checks every installed manifest at startup. The server reads only the metadata of the installed
C# packages. The worker does too, and loads them into its own process only with `Extensions:Isolation` set
to `inProcess`, see [Package runners](#package-runners).

- **The server runs no package code.** For a C# package, the server reads the assembly through a
  metadata-only load context, to run the manifest checks and to compile against the package. It
  finds assemblies by the rules of the shared contracts below. It runs no static constructor, module
  initializer or other package code, and it keeps no load context that can run the package.
- **Development only.** `inProcess` is allowed only in Development. Outside Development neither the
  server nor the worker starts with it, and the error names `Extensions:Isolation`, see
  [Package runners](#package-runners).
- **Node and Python.** A host reads only the manifest of a `node` or `python` package and checks that
  its `adapter` file exists. It loads none of the package's code. The package host reads each version this way
  before it starts a runner, so it never loads the code of a C# package either.
- **Own load context.** In a worker with `inProcess`, each C# package version loads in its own
  `AssemblyLoadContext`. Two versions of the same package can be loaded at
  once.
- **Shared contracts.** The load context of a C# package shares only
  `Axis.Extensions` with the host. Every other assembly the package needs must ship in its folder.
  The .NET framework assemblies come from the host. No other Axis assembly
  of the host can be loaded into the package's context.
- **Manifest check.** Each host checks every installed manifest when it
  starts. The codes are in the diagnostic table of the
  [configuration pipeline](configuration.md#configuration-pipeline):
  - `AXC0132`: the manifest is missing, cannot be read, is not valid JSON or
    does not match its schema;
  - `AXC0133`: the folder names differ from `id` or `version`;
  - `AXC0134`: the `assembly` file is missing or cannot be loaded, or the `adapter` file of a `node`
    or `python` package is missing;
  - `AXC0135`: the assembly's informational version differs from
    `version`, ignoring build metadata after a `+`;
  - `AXC0136`: an operation's `type` is missing from the assembly, does not
    implement `IOperation`, is abstract, or has no public parameterless
    constructor;
  - `AXC0137`: an operation name, or a field name in one operation's
    `input` or `output`, is used twice, ignoring letter case;
  - `AXC0138`: an operation's `timeout` is not a positive ISO 8601 duration
    of at most `PT2M`.
- **A broken package is left out.** The host still starts. It logs each
  diagnostic as a warning, with its code, file and JSON Pointer, and leaves
  the package version out. The file is relative to `Extensions:Path`, such
  as `purchasing-tools/1.0.0/axis-package.json`, and the message names the
  package id and version.
- **Activation.** A version that was left out is not installed as far as
  compilation is concerned. If it was the only version of a package, or the
  only one in a range, compilation reports `AXC0139` or `AXC0141`, and the
  application does not activate. Otherwise compilation pins another installed
  version in the range.

## Capabilities

An operation gets resources only through its context, and the context hands out only what the
operation's manifest declares, for the tenant of the call. The member names below are the C# contract's,
see [Contracts](#contracts).

- **Resolved before the run.** The worker resolves every declared binding for
  the current tenant before it runs the operation. A declared binding that the
  tenant lacks, or holds with the wrong kind, makes the attempt retryable and
  the operation does not run, as for a connector call.
- **HTTP.** `GetHttpClient(name)` returns a client whose base address is the
  value of the declared `url` binding `name`, with a trailing `/` added when
  it has none, so a relative path extends the base path. The client follows no
  redirects, as a [connector call](integration-resources.md#connector-resource) does.
  It refuses a request whose scheme, host and port are not those of a `url`
  binding the operation declares. The path on a declared host is not limited.
  The clients share one connection pool for all tenants, which keeps no cookies.
- **HTTPS.** The value of a `url` binding must be `https`. `http` is allowed
  only in Development, as for connector base URLs. A worker outside
  Development treats an `http` value as a retryable error and runs nothing.
- **Secrets.** `GetSecret(name)` returns the value of the declared `secret`
  binding `name`.
- **Anything else throws.** Asking for a binding the operation did not
  declare, or sending a request to a host no declared `url` binding names,
  throws. The attempt then fails at once, even if the operation catches the
  exception and goes on, because a retry cannot fix a package that asks for
  more than it declares.
- **The error names the binding.** The error in the step history and on the call
  names the operation, the package and the binding the operation asked for, or
  the client it sent through. It holds no secret value and no URL, because a
  host the package builds can carry input data. A request that the worker's
  [egress proxy](#package-runners) refuses names only the operation and the
  package, because a package's own client has no binding to name.
- **Activation check.** The binding check at activation also covers the
  bindings that package operations declare. A binding that is missing for the
  tenant, or of the wrong kind, fails activation with `AXC0100`, and an `http`
  `url` binding outside Development fails it with `AXC0101`, see
  [Deployment bindings](integration-resources.md#deployment-bindings).
- **A customer-hosted package** _(planned for M37)_. Neither the binding resolution nor the activation
  check applies to a package that the application runs in a customer-hosted runner, because the runner
  keeps its own bindings, see [declaring packages in an application](#declaring-packages-in-an-application).
- **Secret values stay hidden.** A secret value never appears in history,
  audit records or logs. History and audit records hold only the type of an
  exception, never its message, because a message can hold input values or
  secrets.

## Package runners

Outside Development, package operations run outside the worker, in package runners, so that a
package reaches nothing of the host and no binding of another call.

- **The runners** _(planned for M21)_. Each runtime has its own container, whose host,
  `Axis.PackageHost`, starts a runner process when the worker asks and stops
  it when told. The images and containers are in [images](installation.md#images).
- **The C# host in Compose.** The Compose example runs `package-host-dotnet` on an internal network `runners` that only it and
  the worker share. It gets no settings or secrets and no published port, a read-only root file system apart from `/tmp`, and
  CPU and memory limits, see [on premises](installation.md#on-premises).
- **The C# and Node.js hosts in the Helm chart.** The chart runs `<release>-package-host-dotnet` and `<release>-package-host-node`, each as a Deployment, a Service on port 5310 and a
  NetworkPolicy, with the command `host` and a read-only root file system apart from `/tmp`. It has no env entry, no secret and no
  service link, and the worker is pointed at its Service with `Extensions__Runners__Dotnet` and `Extensions__Runners__Node`. Its CPU and memory limits come from
  `packageHost.dotnet.resources` and `packageHost.node.resources`, and rendering fails without them. Each NetworkPolicy admits only worker pods, on TCP 5310, lets the host send traffic only to worker
  pods and allows no DNS. It holds only where the network plugin enforces NetworkPolicy, see
  [in the cloud](installation.md#in-the-cloud).
- **The Python runner.** For a `start` that names a `python` version, the host runs
  `python3 -I -B <sdk>/axis_runner.py <version folder>`. The host picks the runner from the manifest's `language`, and
  a version whose manifest is missing or does not match the manifest schema starts no runner. The
  image `axis-package-host-python` holds the SDK at `/app/sdk/python`. The runner moves
  file descriptor 1 aside for the protocol and points it at standard error, so a `print` at import or while an
  operation runs, and a library that writes to the descriptor, never reach the protocol stream. The first line
  after `start` is `ready`.
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
  that runs in the worker, see [package call outcomes](process-execution.md#package-call-outcomes).
  A `failure` that names a binding fails the call at once, and its error names the operation, the
  package and the binding.
- **Environment, for a runner that Axis hosts.** _(planned for M21)_ The environment is empty
  apart from `HTTP_PROXY` and `HTTPS_PROXY`, their lower-case forms, and
  `NODE_USE_ENV_PROXY=1` for Node.js. The container gets no tenant or
  installation settings, a read-only root file system apart from a temporary
  folder, and CPU and memory limits. It sees no tenant's secrets except the
  bindings of the call it runs.
- **Network, for a runner that Axis hosts.** _(planned for M21)_ The proxy variables name the
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
  redirect to another host is a new request that the proxy checks again. Compose puts the worker and the three
  hosts on an `internal` network of their own. The Helm chart gives a
  NetworkPolicy that admits traffic to the hosts only from the worker and lets
  them reach only the proxy port. Every other route is closed, DNS included,
  because the proxy resolves hosts. A library that ignores the proxy variables
  reaches nothing.
- **Timeout.** The operation's `timeout` starts when the worker sends the `run`. At the `timeout` the
  worker closes the connection, which makes the host kill the runner process. The attempt's outcome is
  unknown, see [package call outcomes](process-execution.md#package-call-outcomes).
- **Crash.** A runner that closes its connection during an attempt gives an unknown outcome
  too. It is retried only when the operation declares `idempotent: true`.
- **No host.** A host that cannot be reached, that starts no runner, or that is not set makes the
  attempt retryable, because the operation never ran. The error names the setting of the call's runtime.

The server reads `Extensions:Isolation`, and the worker reads all five settings:

| Setting | Environment variable | Default | Meaning |
| --- | --- | --- | --- |
| `Extensions:Isolation` | `Extensions__Isolation` | `process` | `process` sends C# attempts to the runners. `inProcess` keeps the in-process [loading](#loading) of C# packages and is allowed only in Development. A `node` or `python` attempt goes to a runner either way. Outside Development `inProcess`, or any other value, stops the server and the worker at start |
| `Extensions:Runners:Dotnet` | `Extensions__Runners__Dotnet` | none | The `host:port` of the C# package host, with a port from 1 to 65535, or `[address]:port` for an IPv6 address. Unset, every C# attempt is retryable. A value that is not a `host:port` stops the worker at start |
| `Extensions:Runners:Node` | `Extensions__Runners__Node` | none | The `host:port` of the Node.js package host, with the same forms. Unset, every `node` attempt is retryable, with `inProcess` too. A value that is not a `host:port` stops the worker at start |
| `Extensions:Runners:Python` | `Extensions__Runners__Python` | none | The `host:port` of the Python package host, with the same forms. Unset, every `python` attempt is retryable, with `inProcess` too. A value that is not a `host:port` stops the worker at start |
| `Extensions:Runners:ProxyPort` | `Extensions__Runners__ProxyPort` | `5311` | The port of the worker's egress proxy for the runners, a whole number from 0 to 65535. `0` picks a free port. Any other value stops the worker at start |

### The runner protocol

The worker and every runner speak one protocol that no language owns. A small SDK for each language
implements the operation context over it.

| Message | Sent by | Fields |
| --- | --- | --- |
| `start` | The worker, to the host | `package`, `version`, `proxy`, the `host:port` of the worker's egress proxy, and `proxyToken` |
| `ready` | The runner | None |
| `run` | The worker | `operation`, `operationId`, `input`, and `bindings` with `urls` and `secrets`, each mapping a binding name to its value |
| `result` | The runner | `output` |
| `retryable` | The runner | `error`, the name of the error's type |
| `failure` | The runner | `error`, the name of the error's type, or `binding`, the undeclared binding it asked for |
| `stop` | The worker | None |

- **Framing.** Each message is one line of JSON in UTF-8, over TCP on the runner network. The connection
  is neither encrypted nor authenticated, and a `run` carries binding secrets, so only the operator's
  runner network protects it. The host listens on port 5310 and reads the installed versions from
  `Extensions:Path`. It does not start without that setting, or when the folder does not exist.
- **One connection, one runner.** The worker opens a connection for each runner and sends `start`. The
  host starts the runner of the manifest's `language`, the C# runner, the Node.js runner or the Python runner, and pipes the
  connection to the runner's standard input and output. A Node.js runner also gets `NODE_USE_ENV_PROXY=1`,
  so that its own HTTP calls go through the proxy.
- **Start.** A `start` for a version that is not installed, or whose `package`, `version` or `proxy` is not
  well formed, closes the connection and starts no runner. So does a `start` for a version whose runtime
  is missing from the host's image. The runner answers `ready` once it has
  loaded the version.
- **Standard output.** The runner's standard output holds only protocol lines. What a package writes to
  the console goes to standard error.
- **Runner errors.** An operation that is not in the manifest, an input that does not fit its field
  types, and an output whose type does not fit its field are each a `failure` with the name of
  the error's type. The name is the full type name for C#, such as `System.FormatException`. The Node.js runner
  names an error by its JavaScript `name`, such as `RangeError`, and answers these three with `TypeError`. For
  Python the name is `module.QualName` with the module left out for a built-in exception, such as `ValueError`,
  `LookupError` and `axis_sdk.RetryableError`.
- **Closing kills the runner.** When the worker closes a connection, at a timeout or at `stop`, the host
  kills that runner. A runner that exits closes its connection, and the worker takes that as an
  unknown outcome.
- **Values.** `text` is a string. `integer` and `decimal` are strings, so that no language loses
  precision, and an `integer` is a whole number in the signed 64-bit range. `boolean` is a JSON boolean. `date` is `YYYY-MM-DD` and `date-time` is ISO 8601 with an
  offset. A missing value is `null`.
- **No exception message.** No message carries an exception message, because history keeps only the
  type of an exception, see [Capabilities](#capabilities).
- **The schema.** `sdk/package-runner-protocol.schema.json` holds the exact shape of each message.
  Every SDK's tests read it.

### Customer-hosted runners

_(planned for M37)_

A customer-hosted runner runs in the customer's own network and connects out to the installation with
a credential of its own. It keeps the staging, retries and history of the engine, but none of the runner
limits above, because the customer owns the machine.

- **Created by the administrator.** The tenant's administrator creates and revokes a runner, see
  [package runners](user-administration.md#package-runners).
- **Its credential.** A confidential client of the tenant's realm that uses the client credentials grant,
  like a service account. Its access token's audience is `axis-package-runner`, so it reaches only the
  runner's routes, and a service account route refuses it because it lacks `Oidc:ServiceAudience`. Axis
  keeps no copy of the secret.
- **Declared by the application.** An application says that a package runs in a customer-hosted runner, see
  [declaring packages in an application](#declaring-packages-in-an-application).
- **Registering.** The runner names the package ids and versions it runs. It takes only calls of its
  tenant to a registered id and to the exact version the release pins.
- **Calls out only.** The runner connects to the tenant's host over HTTPS. No connection goes from Axis
  into the customer's network.
- **Taking a call and posting its result.** The runner takes a call under a lease and posts the result, see
  [staging a package call](process-execution.md#staging-a-package-call) for the lease and the outcomes.
- **Its own secrets.** The take carries no binding value. The SDK answers `GetHttpClient` and `GetSecret`
  from the runner's own settings, by the binding names that the manifest declares.
- **Revoked.** Every runner request reads the runner's row, so the next request of a revoked runner answers
  `401`, even with a token that has not expired.

#### Routes

_(planned for M37)_

| Route | Body | Answer |
| --- | --- | --- |
| `PUT /api/package-runner/packages` _(planned for M37)_ | `{ "packages": [{ "id": "purchasing-tools", "version": "1.0.0" }] }` | `204`. It replaces the registered list |
| `POST /api/package-runner/calls/take` _(planned for M37)_ | None | `200` with `{ "leaseId", "package", "version", "operation", "operationId", "input", "leaseUntil" }`, or `204` when no call waits |
| `POST /api/package-runner/leases/{leaseId}/result` _(planned for M37)_ | `{ "outcome": "succeeded", "output": {...} }`, `{ "outcome": "retryable" }` or `{ "outcome": "failed", "error": "<exception type>" }` | `204` |

- **The order of the checks.** A request is checked in this order: the tenant of the host, then the token
  (`401` for the signature, the issuer, the expiry, the audience, or a runner row that is missing or
  revoked, with DPoP under `fapi2` as for service accounts), then the body (`400`). A result is then
  checked for its lease: a lease of another runner or an unknown one answers `404`, and a lease that has
  ended or already holds a result answers `409`.

## Not a sandbox

A package loaded in the host's process is not isolated, because it shares that process. The capability
limits stop mistakes, but they do not stop a package that sets out to get around them. An
installation's packages are trusted code that the operator installs and reviews.

### Limits that hold

These limits hold for every package:

- The operation context hands out only the bindings that the operation
  declares.
- The context's HTTP client reaches only the declared base addresses, and
  follows no redirects.
- An attempt that asks the context for anything else fails.
- The platform stops waiting for an operation when its `timeout` passes.

### Limits that do not hold

A C# package that loads in the host's process and uses .NET APIs directly can do all of these:

- create its own `HttpClient`, or open sockets, and reach any host;
- read environment variables, including every tenant's secrets;
- read and write the file system, and start processes;
- use reflection to reach into the host;
- keep static state that is shared across tenants;
- use as much CPU and memory as it likes;
- ignore the cancellation token. On a timeout the worker stops waiting, but a
  runaway thread keeps running.

### In package runners

_(planned for M21)_

Outside Development, an installation's packages run in [package runners](#package-runners). There, the
runner's network route, empty environment, read-only file system, resource limits and kill at the
timeout stop the items in the list above, and the runner holds nothing of the host to reach by
reflection. Static state shared across tenants is the one item still possible, because one set of
runners serves every tenant.

- **Runners for each tenant** _(planned for M37)_. An application's packages run in runners that serve
  only their tenant, which closes that gap, see [An application's packages](#an-applications-packages).

## An application's packages

_(planned for M37)_

An application's package sits in the application's folder, and the tenant's author brings it. It is not
trusted code, so the platform gives it more limits than an installation's package gets.

- **The sandbox.** The runners run under gVisor (`runsc`), in their own container,
  `application-package-host`, beside the worker. The Helm chart sets that pod's `runtimeClassName` and
  Compose sets that service's `runtime`, so the `package-host-*` containers keep the plain runtime.
  gVisor runs on an ordinary Kubernetes node, while a microVM needs nodes that allow nested
  virtualization.
- **`Extensions:ApplicationPackages`.** A server, worker and publisher setting, `false` by default.
  With `true`, the worker checks at start that `application-package-host` runs under gVisor, and does
  not start otherwise. With `false`, an application's package is a compile error at its
  `packages/<id>/axis-package.json`, and the tenant's switch answers `403`. So an installation without
  gVisor refuses an application's package and runs its own packages as before.
- **Its own runners.** One set for each tenant and package version, with the limits of
  [package runners](#package-runners). The package host starts a set when a call comes and stops it
  once it is idle, so a tenant that calls nothing holds no runner.
- **Quotas.** The CPU seconds of its runners count against the tenant's
  [package run time a day](tenant-quotas.md#package-run-time-a-day). A call also holds a
  `concurrentConnectorCalls` lease, as any package call does.
- **Plans.** In Axis SaaS only a paid plan allows an application's packages, see
  [the catalogue](plans.md#the-catalogue).
- **Switched on by the tenant.** The tenant's administrator turns them on, see
  [application packages](user-administration.md#application-packages), and gives each author the
  `developer` role, see [roles](user-administration.md#roles).
- **In the folder.** `packages/<id>/` holds the package's manifest, adapter, lock file and tests. `<id>`
  is the folder's name, and an id that an installation's package also uses is a compile error. The
  application's `packages` list does not name it. Steps call it as `<id>/<operation>`.
- **Built and scanned on publish.** The publisher builds and scans each package as
  [built once](#what-a-package-is) says, but in the tenant's sandbox, because a dependency's install
  script is the tenant's code. A failed build or scan fails the publish with diagnostics. A publish
  needs the publisher role only.
- **In the release.** The release holds the built package, see
  [versions and pinning](#versions-and-pinning).
- **Writing.** A write to a package's files needs the author role, the developer role and the tenant's
  switch on, see [packages in Studio](studio-packages.md).
- **No change to a module's code.** A customization layer cannot change a module's code. An
  application that needs other code adds a package of its own and calls it, or
  [ejects](modules-and-templates.md#upgrades-with-module-migrations) the module.

### How a package is written

- **By hand.** An author edits the package's files in Studio, or in their own tools through the
  authoring API.
- **By an agent.** An agent writes the package's adapter, manifest and tests over a library that the
  author names. It does so only through an [authoring request](authoring-requests.md#states) and the
  [MCP server's tools](mcp-server.md#tools), under the plan, review and accept of
  [D125](../decisions/d125-how-ai-authoring-connects-runs-and-keeps-its-changes.md), and it never
  publishes.

## Sample and test packages

One sample package shows a package in use, and five test packages cover installation, loading, the
capability limits, the server's metadata-only load and the Node.js runner.

- **Sample package.** `samples/packages/` holds one C# package, `payment-references`
  1.0.0, with its own unit tests. Its one operation, `creditorReference`, is pure and
  has no capabilities. It turns a request number into an ISO 11649 creditor reference:
  `RF`, two mod-97 check digits, then the number without separators. The purchase
  request sample declares the package and calls the operation in its approval process,
  in the `assignPaymentReference` step after the purchase order is created. The package
  project restores with a NuGet lock file and names `packages.lock.json` in `lock`, as does the
  test package. The package project copies each build to `artifacts/extensions/payment-references/1.0.0/`.
  `scripts/build.sh` and `scripts/dev.sh` install it there, and the Compose image holds it in
  `/extensions`. In every run mode `Extensions:Path` names that folder.
- **Test package.** A test-only package, `test-package`, lives under
  `tests/packages/`. One source is built as versions 1.0.0, 1.1.0 and 2.0.0, and the
  tests install it to cover installation and loading. Its `probe` operation
  in version 1.0.0 covers the capability limits: it uses its declared `url`
  and `secret` bindings, and in the other modes it asks for a host or a secret
  it does not declare. Its `ExitOperation` type ends its own process only when it runs in a
  package runner and throws anywhere else, so tests can stand in for a runner that crashes.
- **Initializer package.** A test-only package, `initializer-package` 1.0.0, lives under
  `tests/packages/`. Its module initializer and its operation's static constructor each write a
  marker file beside the assembly, so a test can tell whether a host ran the package's code. The
  server must leave no marker, see [Loading](#loading).
- **Node.js test package.** A test-only `node` package, `node-test-package`, lives under
  `tests/packages/node-test-package/`. Its adapter is TypeScript, `index.ts`, with its compiled
  `index.js` committed. Its one dependency, `axis-test-dependency`, installs from the committed tarball
  `vendor/axis-test-dependency-1.0.0.tgz`, so a build reaches no registry. The dependency declares a
  `postinstall` script, and the tests use it to show that the build never runs install scripts. Its `greet`
  function takes the protocol's input object, as the runner calls every function, and the tests start the built
  package through the Node.js runner.
- **Node.js probe package.** A test-only `node` package, `node-probe-package`, lives under
  `tests/packages/node-probe-package/`. Its adapter is a plain ES module, `index.mjs`, with no dependency, so
  together with the CommonJS `node-test-package` the tests cover both module forms. Its operations mirror the
  C# test package's: `sum`, `fail`, `flaky` and `probe`, with the same modes. It adds `badOutput`, which returns an
  `integer` as a JavaScript number, and `log`, which writes to the console.
- **Python test package.** `python-test-package` lives under `tests/packages/PythonPackage/` as plain files:
  the package's manifest and adapter, and one small dependency. The package build tests zip the
  dependency into a wheel, lock it in `requirements.txt` with its SHA-256 hash, and build the package
  offline, so the repository holds no binary file. Its operations cover each outcome of the Python
  runner: `greet` maps onto the dependency, `echo` returns all six field types, `probe` uses its declared
  bindings or asks for an undeclared one, `flaky` raises `RetryableError` and `fail` raises `ValueError`.
  The package host tests install the package by copying it with its dependency folder, so they need no pip.

## Declaring packages in an application

An application declares the extension packages it uses in its manifest, `application.json`. Each
entry names a package id and a version range:

```json
{
  "id": "219102bc-864f-42ed-874a-c19eaee0d1ba",
  "kind": "application",
  "name": "PurchaseRequests",
  "formatVersion": 1,
  "packages": [{ "id": "purchasing-tools", "version": "^1.0.0" }]
}
```

`purchasing-tools` is an illustrative package. The purchase request sample
declares the real sample package the same way, with
`{ "id": "payment-references", "version": "^1.0.0" }`. Without that package
installed, the sample does not compile: `AXC0139` at `/packages/0/id` names it.

- `packages` is optional. Each entry has an `id`, lower-case letters, digits
  and `-`, starting with a letter, and a `version`. The schema checks this
  shape, so breaking it is `AXC0004`. Each package id appears at most once:
  `AXC0142`.
- `version` is a version range in one of three forms. Versions are plain
  `major.minor.patch` without leading zeros. Any other form is `AXC0140`.
  - `1.0.0` is that exact version.
  - `^1.0.0` is any version from `1.0.0` with the same major version, so
    `1.4.2` but not `2.0.0`. `^0.2.0` also means the same major version, so
    `0.9.0` but not `1.0.0`.
  - `~1.0.0` is any version from `1.0.0` with the same minor version, so
    `1.0.3` but not `1.1.0`.
- **`runner`** _(planned for M37)_. An entry of `application.json`'s `packages` may say where the package
  runs: `axis`, the default, or `customer`. With `customer`, the release still pins the version, but it
  resolves no bindings for the package's operations and starts no Axis runner for it, see
  [customer-hosted runners](#customer-hosted-runners). A module's entry has no `runner`, and the
  application's entry decides for every process of the release, because the release holds one pin per
  package id. A wrong value is `AXC0004`.
- **Pinning.** Compilation picks the highest installed version in each range and pins it in the
  release, in the Release step of the [pipeline](configuration.md#configuration-pipeline), as
  [versions and pinning](#versions-and-pinning) says. A module's `packages` share
  the release's pins, which hold one version of each package id. A module
  uses the application's pin when the application lists the package too, and
  its entry is `AXC0141` in `module.json` when that pin is outside its range.
  Otherwise the release pins the highest installed version that meets the
  range of every module that lists the package, and a package with no such
  version is `AXC0141` in each of those modules' `module.json`. An application
  process sees only the application's own packages.
- **Diagnostics.** `AXC0139`, a package id with no installed version, and `AXC0141`, a range that
  no installed version meets, are compile errors in `application.json`. Compilation sees only the
  versions that loaded at host startup. A manifest that does not match its package is reported at
  startup, as `AXC0132` to `AXC0138`, see [Loading](#loading), and a pinned version missing at
  startup is `AXC0146`, see [versions and pinning](#versions-and-pinning).
- An application uses only the packages it declares, even though every
  tenant sees the same installed packages. Installation is in
  [Installation](#installation).
- How a process calls a package operation is in
  [Package operations](process-execution.md#package-operations).
