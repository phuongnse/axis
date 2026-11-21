# Extension packages

An extension package holds custom code that an application calls where configuration is not
enough. The code may be written in C#, TypeScript or JavaScript on Node.js, or Python. This page
covers what a package is, its manifest, the contracts it builds on, how it is installed, versioned,
pinned and loaded, and what its operations may reach. What those limits do not protect against is in
[extension package limits](extension-package-limits.md). An
application's own packages are in [An application's packages](package-runners.md#an-applications-packages). How a
process calls a package operation is in [package operations](connector-and-package-calls.md#package-operations),
and how an application declares its packages is in
[declaring packages](declaring-packages.md#declaring-packages-in-an-application). The reason for keeping custom code out of
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
- **Three languages.** A package is written in C#,
  TypeScript or JavaScript on Node.js, or Python.
- **A folder with a lock file.** A package of any of
  these languages is a folder with a manifest, a thin adapter in that
  language and a lock file of its dependencies.
- **Reuses a library.** The dependencies of a package may
  be the business's own libraries, from a public registry or as
  built files. The adapter maps an operation to a function of the library,
  and the library's code does not change.
- **Private registries and git commits** _(planned for M37)_. The dependencies
  may also come from a private registry, or as a commit of a git repository.
- **Built once.** A package is built once, when it is installed, see
  [Building a package](#building-a-package). Its dependencies are installed from the lock file and
  scanned for known vulnerabilities. A runner downloads nothing at run time.
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
- **`runner`.** Where the package runs: `axis`, the default, or `customer`. Another value is `AXC0132`. Only an
  application's own package may say `customer`, and its folder then holds the manifest alone, see
  [customer-hosted runners](package-runners.md#customer-hosted-runners). An installed package that says it is `AXC0132` at
  `/runner`: only the application's `packages` entry decides where an installed package runs.
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
    [Call outcomes](connector-and-package-calls.md#package-call-outcomes).
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
  [Call outcomes](connector-and-package-calls.md#package-call-outcomes).
- **The Node.js SDK.** `sdk/node/` holds the Node.js runner and a small SDK that implements the operation
  context over [the runner protocol](package-runners.md#the-runner-protocol), which no language owns. An adapter written as
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
  | [`file`](connector-and-package-calls.md#files-in-calls) _(planned for M40)_ | `AxisFile` | `AxisFile` |

  A missing value is `null` in C# and `None` in Python.

- **Published** _(planned for M22)_. `Axis.Extensions` is published as a NuGet package, at a 0.x
  version with no compatibility promise, see [the SDK](custom-widgets.md#the-sdk).

## Installation

The operator installs an installation's packages once for the whole installation, in a folder that
every host reads. An installation's package is trusted code that the operator reviews.

- **Inside a module** _(planned for M37)_. A module installed for the installation can ship a package,
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
- **The package hosts.** The Compose example's containers `package-host-dotnet`, `package-host-python` and `package-host-node` and the Helm chart's pods
  `<release>-package-host-dotnet`, `<release>-package-host-python` and `<release>-package-host-node` mount the same folder read-only, and
  each runner runs the packages of its language, see [Package runners](package-runners.md#package-runners).
- **The Python host image.** The image `axis-package-host-python` holds the Python runner, see
  [Package runners](package-runners.md#package-runners).
- **Restart.** Each host reads the folder at startup. Installing a new
  version needs a restart of both hosts.
- **Keep pinned versions.** Keep every version that an active release, or a
  release with a running or waiting instance, pins. Removing one stops the
  start of both hosts, see [Versions and pinning](#versions-and-pinning).

### Building a package

The operator builds a C#, Node.js or Python package once, with `Axis.PackageHost build <source>`. The source is the
folder that the developer published in locked mode. For `node` it holds the compiled adapter, `package.json` and
`package-lock.json`. For Python it is the folder of the adapter and `requirements.txt`. The build writes the folder
that `Extensions:Path` names and compiles nothing, except that it checks that each `.py` file of a Python package compiles.
A [`build` message](package-runners.md#the-runner-protocol) runs the same checks in a package host, on files that a caller sends. A `test` message runs them too, before the tests.

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
  4. For Python, the syntax check.
  5. For `node` and Python, the install.
  6. The scan.
- **Lock file.** A missing lock file is refused. A lock file is stale when the runtime dependency list,
  the `.deps.json` beside the assembly, names a NuGet package whose version the lock file does not
  resolve, and a stale lock file is refused. A lock file entry that has no runtime asset, such as an
  analyzer, is allowed. A missing `.deps.json` is refused too, because the lock file cannot be checked
  without it. For `node`, a missing `package.json` or `package-lock.json` is refused, and so is a
  `package-lock.json` that does not list the dependencies of `package.json` at the same ranges. The
  check compares `dependencies`, `devDependencies`, `optionalDependencies` and `peerDependencies`
  with the lock file's root entry, in both directions, before npm runs.
- **Syntax check** (Python). It copies the source to a temporary folder and byte-compiles each `.py` file of the
  copy with Python's `compile()`, before the install, so a dependency's files are never compiled. It runs the
  `PackageBuild:Python` command with `-I -B` and an empty environment, so it runs no package code and writes no
  `.pyc` file. It refuses each file that does not compile, at its path, `line` and `column` counted from 1, with
  Python's own message. A column that Python does not give is 1. A Python that cannot start is a setup error.
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
  argument, a missing source or extensions folder, a Python or an install that cannot start, or a scan that did not
  complete.
- **Python hashes.** Every requirement line of `requirements.txt` must carry a `--hash`, and a line without
  one is refused before anything is installed, with the line number and the dependency. The file is read as
  pip reads it: a comment and a blank line are skipped and a line that ends in a backslash continues on the
  next line. An option line such as `--index-url` needs no hash. A line that starts with `-r`, `-c` or `-e`,
  or their long forms, is refused, because it brings in requirements that the check never sees.
- **Registry credentials** _(planned for M37)_. They are mounted for the build alone, and the runners
  never see them.

The build reads these settings, and a package host reads `PackageBuild:Trivy` and `PackageBuild:Python` too, for a `build` or `test` message:

| Setting | Environment variable | Default | Meaning |
| --- | --- | --- | --- |
| `Extensions:Path` | `Extensions__Path` | none | The existing folder that the build writes the package to |
| `PackageBuild:Trivy` | `PackageBuild__Trivy` | `trivy` | The Trivy command that scans the dependencies |
| `PackageBuild:Python` | `PackageBuild__Python` | `python3` | The Python command that checks the syntax of a Python package, runs pip for it, and runs its tests |

## Versions and pinning

A release pins one exact version of each package it uses, so a running instance keeps the version
that its release was compiled against.

- **Ranges.** An application's `packages` entry gives a version range: an exact version, a `^`
  range or a `~` range, as [declaring packages](declaring-packages.md#declaring-packages-in-an-application) says.
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
  version's manifest, or the manifest of an application's own package, see
  [Package step shape](connector-and-package-calls.md#package-step-shape), and the compiled
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
- **An application's packages.** The release holds every file of the application's own packages under
  `packages/<id>/`, and the manifest alone for a customer-hosted one, so running work keeps the contract it
  started with, see [An application's packages](package-runners.md#an-applications-packages). Their versions are not
  pins: the compiled step holds the version in the package's manifest.
- **A built package in the release.** A release that a `publish` stores also holds every file of each built
  package of the application, the installed dependencies included, in `axis.release_package_files`, see
  [storage](storage.md#system-tables). The content hash leaves them out, so a changed package gives a new
  release and the older release keeps its built files. A release that `activate` or the server's start stores
  holds none, and a release that is already stored gets none from a later `publish` of the same content.

## Loading

Each host checks every installed manifest at startup. The server reads only the metadata of the installed
C# packages. The worker does too, and loads them into its own process only with `Extensions:Isolation` set
to `inProcess`, see [Package runners](package-runners.md#package-runners).

- **The server runs no package code.** For a C# package, the server reads the assembly through a
  metadata-only load context, to run the manifest checks and to compile against the package. It
  finds assemblies by the rules of the shared contracts below. It runs no static constructor, module
  initializer or other package code, and it keeps no load context that can run the package.
- **Development only.** `inProcess` is allowed only in Development. Outside Development neither the
  server nor the worker starts with it, and the error names `Extensions:Isolation`, see
  [Package runners](package-runners.md#package-runners).
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
    does not match its schema, or it says `"runner": "customer"`, reported at `/runner`;
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
  [egress proxy](package-runners.md#package-runners) refuses names only the operation and the
  package, because a package's own client has no binding to name.
- **Activation check.** The binding check at activation also covers the
  bindings that package operations declare. A binding that is missing for the
  tenant, or of the wrong kind, fails activation with `AXC0100`, and an `http`
  `url` binding outside Development fails it with `AXC0101`, see
  [Deployment bindings](integration-resources.md#deployment-bindings).
- **A customer-hosted package and the activation check.** The check skips the bindings that a customer-hosted
  package's operations declare, so a tenant without them activates the release.
- **A customer-hosted package and the binding resolution.** The worker resolves no binding for its calls,
  because it runs no attempt of them, see
  [staging a package call](connector-and-package-calls.md#staging-a-package-call). The [runner](package-runners.md#customer-hosted-runners)
  keeps its own bindings.
- **Secret values stay hidden.** A secret value never appears in history,
  audit records or logs. History and audit records hold only the type of an
  exception, never its message, because a message can hold input values or
  secrets.

## Sample and test packages

Three sample packages show a package in use, a C# one, a Python one and a TypeScript one, and five test packages cover
installation, loading, the capability limits, the server's metadata-only load and the Node.js runner.

- **Sample package.** `samples/packages/` holds one C# package, `payment-references`
  1.0.0, with its own unit tests. Its one operation, `creditorReference`, is pure and
  has no capabilities. It turns a request number into an ISO 11649 creditor reference:
  `RF`, two mod-97 check digits, then the number without separators. The purchase
  request sample declares the package and calls the operation in its approval process,
  in the `assignPaymentReference` step after the purchase order is created. The package
  project restores with a NuGet lock file and names `packages.lock.json` in `lock`, as does the
  test package. The package project copies each build to `artifacts/extensions/payment-references/1.0.0/`.
  `scripts/build.sh` and `scripts/dev.sh` install it there, and the Compose image holds it in
  `/extensions`. In every run mode `Extensions:Path` names that folder. The on-premises smoke test builds the
  package with the `build` command of its package host image. It also runs the operation in a runner of the C# package host
  when tenant one's manager approves a request, see [tests of the example](installation-on-premises.md#tests-of-the-example).
- **Python sample package.** `samples/packages/phone-numbers/` holds `phone-numbers` 1.0.0 as plain
  files. Its adapter wraps the PyPI library `phonenumbers` and leaves it unchanged. Its one
  operation, `normalize`, is pure and has no capabilities. It takes a `number` and an optional
  `region` and returns the `number` in E.164 form, such as `+16502530000`. A national number such as
  `(650) 253-0000` needs a `region` such as `US`. A text that is no number fails the call with
  `phonenumbers.phonenumberutil.NumberParseException` and no message, see [the runner protocol](package-runners.md#the-runner-protocol).
  Its `requirements.txt` pins one `phonenumbers` wheel with its SHA-256 hash.
  `scripts/setup.sh` downloads that wheel into `~/.cache/axis-tools/wheels`, checked against the hash, and
  `scripts/build.sh` and `scripts/integration.sh` install the package offline from there into
  `artifacts/extensions/phone-numbers/1.0.0/` with the library beside the adapter, using the pip flags of the
  [build](#building-a-package). They skip the scan that the build runs, as the project build of the C#
  sample does, and `scripts/build.sh` fails unless the folder holds `axis-package.json`, `adapter.py` and
  `phonenumbers/__init__.py`. No sample application calls it, and Compose and `scripts/dev.sh` run no Python host.
  The integration tests run it from a process step through a Python package host.
- **TypeScript sample package.** `samples/packages/payment-terms/` holds `payment-terms` 1.0.0, a `node`
  package whose adapter, `index.ts`, wraps the npm library dayjs 1.11.23 and leaves it unchanged. The compiled
  `index.js` is committed beside it. dayjs is committed as `vendor/dayjs-1.11.23.tgz` and locked in
  `package-lock.json` with the same `integrity` that `web/package-lock.json` records for it, so an install
  reaches no registry and `npm ci` refuses a tarball that does not match. Its one operation, `dueDate`, is pure
  and has no capabilities. It takes a `from` date-time and returns a `dueDate` date: 30 days after `from` in
  UTC, moved to the last day of that month. `2026-01-31T10:00:00Z` gives `2026-03-31`, and
  `2028-01-30T00:00:00Z` gives `2028-02-29`.
  `scripts/lib/install-sample-packages.sh` installs it into `artifacts/extensions/payment-terms/1.0.0/` with the
  flags of the [build](#building-a-package) and `--offline`. `scripts/build.sh`, `scripts/test.sh`,
  `scripts/dev.sh` and `scripts/integration.sh` run it, and so does a `node:24-slim` stage of the `Dockerfile`,
  whose output the development server, worker and publisher images hold in `/extensions`. The install skips the
  scan that the build runs, and `scripts/build.sh` fails unless the folder holds `axis-package.json`, `index.js`
  and `node_modules/dayjs/package.json`. `scripts/test.sh` runs the package's `node --test` unit test in the
  installed copy, and an integration test starts a Node.js runner of the package host for it. The test
  projects copy only its manifest, adapter and lock files, so a server lists it as metadata. The purchase
  request sample declares it and calls `dueDate` in its approval process, in the `assignPaymentDueDate` step after
  `assignPaymentReference`, on the request's `submittedAt`. The integration tests that use the sample fixture run
  that step through a Node.js runner of a package host over an installed copy of the sample packages.
  `scripts/dev.sh`, `scripts/e2e.sh` and the root `compose.yaml` each run a Node.js package
  host that holds it, and point their worker at it, see [development](../development.md). The on-premises smoke test
  builds the package with the `build` command of its package host image. It also runs the operation in a runner of the
  Node.js package host when tenant one's manager approves a request, see
  [tests of the example](installation-on-premises.md#tests-of-the-example).
- **Test package.** A test-only package, `test-package`, lives under
  `tests/packages/`. One source is built as versions 1.0.0, 1.1.0 and 2.0.0, and the
  tests install it to cover installation and loading. Its `probe` operation
  in version 1.0.0 covers the capability limits: it uses its declared `url`
  and `secret` bindings, and in the other modes it asks for a host or a secret
  it does not declare. The on-premises smoke test builds 1.0.0 with the `build` command of the C# package host
  image and runs `probe` in the `ownOtherHost` mode in a runner, see
  [tests of the example](installation-on-premises.md#tests-of-the-example). Its `ExitOperation` type ends its own process only when it runs in a
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
- **Python test package.** `python-test-package` lives under `tests/packages/PythonPackage/` as plain files: the package's manifest
  and adapter, and one small dependency. The package build tests zip the dependency into a wheel, lock it in `requirements.txt` with
  its SHA-256 hash, and build the package offline, so the repository holds no binary file. Its operations cover each outcome of the
  Python runner: `greet` maps onto the dependency, `echo` returns all six field types, `probe` uses its declared bindings, asks for
  an undeclared one or, in the mode `ownOtherHost`, requests one with `urllib`, `pid` returns its process ID and the folder it runs
  from, `spin` uses the CPU milliseconds that its input names and declares a `timeout` of 10 seconds, `flaky` raises
  `RetryableError` and `fail` raises `ValueError`. The package host tests install the package by copying it, so they need no pip.
