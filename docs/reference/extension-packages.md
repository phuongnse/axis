# Extension packages

Detailed reference for extension packages: what a package is, its manifest,
the contracts it builds on, installation, versions and pinning, loading,
capabilities and what the capability limits do not protect against. The
manifest, the contracts, installation and loading are built. Version ranges
and pinning in releases are built too, and so is checking a process step
against the pinned manifest. Running a package operation from a process is
built too, and so are its retries under the step's retry policy. So are the
capability limits of the operation context. How a process calls a package
operation is in
[Package operations](process-execution.md#package-operations), and how an application
declares its packages is in
[Extension packages](#declaring-packages-in-an-application). Package runners
are _(planned for M21)_, see [Package runners](#package-runners). Widgets in
packages and the published SDK are _(planned for M22)_, see
[custom widgets](custom-widgets.md).

Dn refers to [decisions.md](../decisions.md). The design follows
[D7](../decisions/d7-custom-code-lives-in-extension-packages.md) and
[D28](../decisions/d28-extension-packages.md). The reason for keeping
custom code out of configuration is in
[knowledge](../domain/knowledge.md#logic-in-configuration).

## What a package is

- **Custom C# code.** A package holds logic that does not fit expressions,
  such as file parsing or a complex calculation. It adds typed operations
  that a process step calls.
- **A class library.** A package is a .NET class library with its own unit
  tests and a SemVer version.
- **One reference.** It references only `Axis.Extensions`, the contracts
  assembly described in [Contracts](#contracts).
- **Build artifact.** Its build gives a folder with the package's assemblies
  and a manifest, `axis-package.json`.
- **No data access.** A package cannot read or write entities. Data access
  comes later.
- **Widgets** _(planned for M22)_. A package may also hold custom widgets,
  React components that a page names. A custom widget builds on Axis widgets
  and Axis tokens, never on Ant Design directly
  ([D58](../decisions/d58-ant-design-is-a-detail-of-the-spa-without-procomponents.md)).
  See [custom widgets](custom-widgets.md) and
  [D88](../decisions/d88-custom-widgets-in-the-designer-palette-and-a-published-sdk.md).

## Manifest

The manifest, `axis-package.json`, sits at the root of the
package folder. This example uses an illustrative package,
`purchasing-tools`. The [sample package](#sample-and-test-packages),
`payment-references`, has a manifest of the same shape.

```json
{
  "id": "purchasing-tools",
  "version": "1.0.0",
  "assembly": "PurchasingTools.dll",
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
  `1.2.0`. Pre-release versions come later.
- **`assembly`.** The file name of the package's main assembly, in the
  package folder. _(planned for M22)_ It is optional for a package that holds
  only widgets.
- **`widgets`** _(planned for M22)_. The custom widgets the package offers,
  each with a `name`, an `entry`, a `label`, `properties` and an `sdk` range.
  See [widgets in a package manifest](custom-widgets.md#widgets-in-a-package-manifest).
- **`operations`.** The operations the package offers. Each has these:
  - `name` is the operation's name, unique in the package.
  - `type` is the full name of a class in the main assembly that implements
    `IOperation`. The class is not abstract and has a public parameterless
    constructor, because the host creates each instance itself.
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

`Axis.Extensions` is the only assembly a package
references. _(planned for M22)_ It is published as a NuGet package, at a 0.x
version with no compatibility promise, see [the SDK](custom-widgets.md#the-sdk).
It holds only these types:

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
- **Value types.** Input and output values use these .NET types:

  | Field type | .NET type |
  | --- | --- |
  | `text` | `string` |
  | `integer` | `long` |
  | `decimal` | `decimal` |
  | `boolean` | `bool` |
  | `date` | `DateOnly` |
  | `date-time` | `DateTimeOffset` |

  A missing value is `null`.

## Installation

- **Per installation.** Packages are installed once for the installation,
  not per tenant. Every tenant sees the same installed packages. An
  application uses only the packages it declares.
- **Where.** The server and the worker both read the setting
  `Extensions:Path`. Each package version is installed in
  `<Path>/<id>/<version>/`, which holds `axis-package.json` and the
  assemblies. A relative path is resolved from the host's content root.
- **No setting, no packages.** Without `Extensions:Path`, a host has no
  packages. If the setting names a folder that does not exist, the host does
  not start.
- **Both hosts.** The server and the worker must hold the same packages. The
  server compiles and activates against them, and the worker runs them.
  _(planned for M21)_ The `package-host` container holds them too, and its
  runners run them, see [Package runners](#package-runners).
- **Restart.** Each host reads the folder at startup. Installing a new
  version needs a restart of both hosts.
- **Keep pinned versions.** Keep every version that an active release, or a
  release with a running or waiting instance, pins. Removing one stops the
  start of both hosts, see [Versions and pinning](#versions-and-pinning).

## Versions and pinning

- **Ranges.** An application's `packages` entry gives a version range, see
  [Extension packages](#declaring-packages-in-an-application). A range is an
  exact version `1.2.0`, `^1.2.0` for any version of the same major version
  from `1.2.0`, or `~1.2.0` for any version of the same minor version from
  `1.2.0`. Any other form is `AXC0140`.
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
  migrated or cannot be reached. See
  [D29](../decisions/d29-release-pinning-and-text-resources.md).
- **Pinned version missing on the worker.** A call to a version that the
  worker does not hold is a retryable attempt. Nothing ran, as with a missing
  binding on a connector call. The call is retried under the step's retry
  policy, and the instance fails when attempts run out. The call stores the
  policy when it is staged, because the worker cannot rebuild the release
  without the version.

## Loading

_(planned for M21)_ Outside Development, the worker no longer loads package
code and the server only reads its metadata, see
[Package runners](#package-runners). This section describes in-process
loading, which stays for Development.

- **Own load context.** Each package version loads in its own
  `AssemblyLoadContext`. Two versions of the same package can be loaded at
  once.
- **Shared contracts.** The load context shares only `Axis.Extensions` with
  the host. Every other assembly the package needs must ship in its folder.
  The .NET framework assemblies come from the host. No other Axis assembly
  of the host can be loaded into the package's context.
- **Manifest check.** Each host checks every installed manifest when it
  starts. The codes are in the diagnostic table of the
  [configuration pipeline](configuration.md#configuration-pipeline):
  - `AXC0132`: the manifest is missing, cannot be read, is not valid JSON or
    does not match its schema;
  - `AXC0133`: the folder names differ from `id` or `version`;
  - `AXC0134`: the `assembly` file is missing or cannot be loaded;
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

An operation gets resources only through its context, and
the context hands out only what the operation's manifest declares, for the
tenant of the call.

- **Resolved before the run.** The worker resolves every declared binding for
  the current tenant before it runs the operation. A declared binding that the
  tenant lacks, or holds with the wrong kind, makes the attempt retryable and
  the operation does not run, as for a connector call.
- **HTTP.** `GetHttpClient(name)` returns a client whose base address is the
  value of the declared `url` binding `name`, with a trailing `/` added when
  it has none, so a relative path extends the base path. The client follows no
  redirects, as a connector call does
  ([D25](../decisions/d25-connectors-deployment-bindings-and-secrets.md)).
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
  host the package builds can carry input data.
- **Activation check.** The binding check at activation also covers the
  bindings that package operations declare. A binding that is missing for the
  tenant, or of the wrong kind, fails activation with `AXC0100`, and an `http`
  `url` binding outside Development fails it with `AXC0101`, see
  [Deployment bindings](integration-resources.md#deployment-bindings).
- **Secret values stay hidden.** A secret value never appears in history,
  audit records or logs. History and audit records hold only the type of an
  exception, never its message, because a message can hold input values or
  secrets.

## Package runners

_(planned for M21)_ Packages run outside the worker, in package runners, as
[D90](../decisions/d90-extension-packages-run-out-of-process.md) agrees.

- **The runner host.** `Axis.PackageHost` runs package operations. The worker
  no longer loads package code. The runners run in their own container,
  `package-host`, beside the worker, in Compose and in the Helm chart.
- **One runner per version.** Each installed package version gets its own
  runner processes, shared by every tenant. Each runner process runs one call
  at a time. The worker keeps a small pool for each version, and starts a new
  runner when one exits.
- **The request.** For each attempt the worker sends a runner one request
  over a local socket: the operation name, the operation identity, the input,
  and the values of the bindings the operation declares, for the call's
  tenant. The runner implements the same `IOperationContext`, so package code
  and manifests do not change.
- **Environment.** A runner starts with an empty environment, a read-only
  file system apart from a temporary folder, and CPU and memory limits. It
  sees no tenant's secrets except the bindings of the call it runs.
- **Network.** A runner's only network route is an egress proxy in the
  worker. The proxy lets through only the scheme, host and port of the `url`
  bindings the operation declares, and follows no redirects. The context's
  HTTP client sends through it. A socket the package opens itself reaches
  nothing else.
- **Timeout.** At the operation's `timeout` the worker kills the runner
  process. The attempt's outcome is unknown, see
  [package call outcomes](process-execution.md#package-call-outcomes).
- **Crash.** A runner that exits during an attempt gives an unknown outcome
  too. It is retried only when the operation declares `idempotent: true`.
- **The server.** The server reads package assemblies through a
  metadata-only load context to run the [manifest checks](#loading), and
  never executes them.
- **`Extensions:Isolation`.** A server and worker setting. `process`, the
  default, uses the runners. `inProcess` keeps the in-process
  [loading](#loading), and is allowed only in Development. Elsewhere the host
  does not start.

## Not a sandbox

A package loads in the host's process, so loading it is not
isolation. The capability limits stop mistakes. They do not stop a package
that sets out to get around them. Packages are trusted code that the operator
installs and reviews. _(planned for M21)_ Outside Development, packages run in
[package runners](#package-runners). There, the runner's network route, empty
environment, read-only file system, resource limits and kill at the timeout
stop the items in the list below, and the runner holds nothing of the host to
reach by reflection. Static state shared across tenants is the one item still
possible, because one runner serves every tenant.

**Limits that hold:**

- The operation context hands out only the bindings that the operation
  declares.
- The context's HTTP client reaches only the declared base addresses, and
  follows no redirects.
- An attempt that asks the context for anything else fails.
- The platform stops waiting for an operation when its `timeout` passes.

**Limits that do not hold.** A package that uses .NET APIs directly can do
all of these:

- create its own `HttpClient`, or open sockets, and reach any host;
- read environment variables, including every tenant's secrets;
- read and write the file system, and start processes;
- use reflection to reach into the host;
- keep static state that is shared across tenants;
- use as much CPU and memory as it likes;
- ignore the cancellation token. On a timeout the worker stops waiting, but a
  runaway thread keeps running.

## Sample and test packages

- **Sample package.** `samples/packages/` holds one package, `payment-references`
  1.0.0, with its own unit tests. Its one operation, `creditorReference`, is pure and
  has no capabilities. It turns a request number into an ISO 11649 creditor reference:
  `RF`, two mod-97 check digits, then the number without separators. The purchase
  request sample declares the package and calls the operation in its approval process,
  in the `assignPaymentReference` step after the purchase order is created. The package
  project copies each build to `artifacts/extensions/payment-references/1.0.0/`.
  `scripts/build.sh` and `scripts/dev.sh` install it there, and the Compose image holds it in
  `/extensions`. In every run mode `Extensions:Path` names that folder.
- **Test package.** A test-only package, `test-package`, lives under
  `tests/packages/`. One source is built as versions 1.0.0, 1.1.0 and 2.0.0, and the
  tests install it to cover installation and loading. Its `probe` operation
  in version 1.0.0 covers the capability limits: it uses its declared `url`
  and `secret` bindings, and in the other modes it asks for a host or a secret
  it does not declare.

## Declaring packages in an application

This section follows
[D28](../decisions/d28-extension-packages.md). An application
declares the [extension packages](extension-packages.md) it uses in its
manifest, `application.json`. Each entry names a package id and a version
range:

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
  `major.minor.patch` without leading zeros, and pre-release versions come
  later. Any other form is `AXC0140`.
  - `1.0.0` is that exact version.
  - `^1.0.0` is any version from `1.0.0` with the same major version, so
    `1.4.2` but not `2.0.0`. `^0.2.0` also means the same major version, so
    `0.9.0` but not `1.0.0`.
  - `~1.0.0` is any version from `1.0.0` with the same minor version, so
    `1.0.3` but not `1.1.0`.
- **Pinning.** Compilation picks the highest installed version in each range
  and pins that exact version in the release, as step 4, **Release**, of the
  pipeline describes. The server compiles against the packages installed
  under `Extensions:Path`. A process step is
  checked against the pinned version's manifest. A module's `packages` share
  the release's pins, which hold one version of each package id. A module
  uses the application's pin when the application lists the package too, and
  its entry is `AXC0141` in `module.json` when that pin is outside its range.
  Otherwise the release pins the highest installed version that meets the
  range of every module that lists the package, and a package with no such
  version is `AXC0141` in each of those modules' `module.json`. An application
  process sees only the application's own packages.
- **Diagnostics.** A package id with no installed version is `AXC0139`. A
  range that no installed version meets is `AXC0141`, and its message lists
  the installed versions. Both are compile errors in `application.json`.
  Compilation sees only the versions that loaded at host startup. A manifest
  that does not match its package is reported at startup, as `AXC0132` to
  `AXC0138`, see [Loading](#loading).
  At the start of the server and the worker, every active release and every
  release with a running or waiting instance is rebuilt. A pinned version
  that is not installed is `AXC0146` and stops the start. The diagnostic names
  the tenant, application, release and package version. See
  [Versions and pinning](#versions-and-pinning) and
  [D29](../decisions/d29-release-pinning-and-text-resources.md).
- An application uses only the packages it declares, even though every
  tenant sees the same installed packages. Installation is in
  [Installation](#installation).
- How a process calls a package operation is in
  [Package operations](process-execution.md#package-operations).
