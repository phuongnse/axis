# Extension packages

Detailed reference for extension packages: what a package is, its manifest,
the contracts it builds on, installation, versions and pinning, loading,
capabilities and what the capability limits do not protect against. None of
this is built yet. How a process calls a package operation is in
[Package operations](processes.md#package-operations), and how an application
declares its packages is in
[Extension packages](configuration.md#extension-packages).

Dn refers to [decisions.md](../decisions.md). The design follows
[D7](../decisions.md#d7-custom-code-lives-in-extension-packages--agreed) and
[D28](../decisions.md#d28-extension-packages--agreed). The reason for keeping
custom code out of configuration is in
[knowledge](../domain/knowledge.md#logic-in-configuration).

## What a package is

*(planned for M5)*

- **Custom C# code.** A package holds logic that does not fit expressions,
  such as file parsing or a complex calculation. It adds typed operations
  that a process step calls.
- **A class library.** A package is a .NET class library with its own unit
  tests and a SemVer version.
- **One reference.** It references only `Axis.Extensions`, the contracts
  assembly described in [Contracts](#contracts).
- **Build artifact.** Its build gives a folder with the package's assemblies
  and a manifest, `axis-package.json`.
- **No data access.** A package cannot read or write entities in M5. Data
  access comes later.
- **No UI.** React components in packages come later
  ([D8](../decisions.md#d8-custom-ui-widgets-are-packaged-react-components--agreed-deferred)).

## Manifest

*(planned for M5)* The manifest, `axis-package.json`, sits at the root of the
package folder. This example uses an illustrative package,
`purchasing-tools`. It is not the sample package, whose operation is chosen
in the issue that builds it.

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
  package folder.
- **`operations`.** The operations the package offers. Each has these:
  - `name` is the operation's name, unique in the package.
  - `type` is the full name of a class in the main assembly that implements
    `IOperation`.
  - `idempotent` says whether running the operation twice with the same
    operation identity has the same effect as running it once. It decides
    whether an unknown outcome is retried, see
    [Call outcomes](processes.md#package-call-outcomes).
  - `timeout` is an ISO 8601 duration. It defaults to `PT30S` and is at most
    `PT2M`, as for a [connector](configuration.md#connector-resource).
  - `input` and `output` list typed fields. Their types are the
    [rule parameter types](configuration.md#resource-file-shape): `text`,
    `integer`, `decimal`, `boolean`, `date` and `date-time`. A field may be
    `required`.
  - `capabilities` lists what the operation may ask its context for:
    `urls` names `url` bindings and `secrets` names `secret` bindings. Both
    are empty when absent. See [Capabilities](#capabilities).

## Contracts

*(planned for M5)* `Axis.Extensions` is the only assembly a package
references. It holds only these types:

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
  [Call outcomes](processes.md#package-call-outcomes).
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

*(planned for M5)*

- **Per installation.** Packages are installed once for the installation,
  not per tenant. Every tenant sees the same installed packages. An
  application uses only the packages it declares.
- **Where.** The server and the worker both read the setting
  `Extensions:Path`. Each package version is installed in
  `<Path>/<id>/<version>/`, which holds `axis-package.json` and the
  assemblies.
- **Both hosts.** The server and the worker must hold the same packages. The
  server compiles and activates against them, and the worker runs them.
- **Restart.** Each host reads the folder at startup. Installing a new
  version needs a restart of both hosts.
- **Keep pinned versions.** Keep every version that an active release, or a
  running instance's release, pins. Removing one makes its calls retry until
  it is back, see [Versions and pinning](#versions-and-pinning).

## Versions and pinning

*(planned for M5)*

- **Ranges.** An application's `packages` entry gives a version range, see
  [Extension packages](configuration.md#extension-packages). A range is an
  exact version `1.2.0`, `^1.2.0` for any version of the same major version
  from `1.2.0`, or `~1.2.0` for any version of the same minor version from
  `1.2.0`.
- **Pinning.** Compilation picks the highest installed version in each range
  and pins that exact version in the release. Steps are checked against the
  pinned version's manifest. A running instance keeps the version its
  release pins, even after a newer version is installed.
- **No version in range.** A range that no installed version meets is a
  diagnostic. Its code comes with the build issue.
- **Pinned version missing at activation.** Activating a release whose
  pinned version is not installed fails with a diagnostic.
- **Pinned version missing on the worker.** A call to a version that the
  worker does not hold, or cannot load, is a retryable attempt. Nothing ran,
  as with a missing binding on a connector call.

## Loading

*(planned for M5)*

- **Own load context.** Each package version loads in its own
  `AssemblyLoadContext`. Two versions of the same package can be loaded at
  once.
- **Shared contracts.** The load context shares only `Axis.Extensions` with
  the host. Every other assembly the package needs must ship in its folder.
- **Manifest check.** A manifest that does not match the package stops
  activation with a diagnostic. Its code comes with the build issue. A
  mismatch is any of these:
  - the folder names differ from `id` or `version`;
  - the `assembly` file is missing;
  - the assembly's informational version differs from `version`;
  - an operation's `type` is missing from the assembly, or does not
    implement `IOperation`.

## Capabilities

*(planned for M5)* An operation gets resources only through its context, and
the context hands out only what the operation's manifest declares.

- **HTTP.** `GetHttpClient(name)` returns a client whose base address is the
  value of the declared `url` binding `name`. The client refuses a request to
  any other host, and follows no redirects, as a connector call does
  ([D25](../decisions.md#d25-connectors-deployment-bindings-and-secrets--agreed)).
- **Secrets.** `GetSecret(name)` returns the value of the declared `secret`
  binding `name`.
- **Anything else throws.** Asking for a binding the operation did not
  declare throws. The attempt then fails, even if the operation catches the
  exception and goes on.
- **Activation check.** The binding check at activation also covers the
  bindings that package operations declare. A binding that is missing for the
  tenant, or of the wrong kind, fails activation, see
  [Deployment bindings](configuration.md#deployment-bindings).
- **Secret values stay hidden.** A secret value never appears in history,
  audit records or logs. History and audit records hold only the type of an
  exception, never its message, because a message can hold input values or
  secrets.

## Not a sandbox

*(planned for M5)* A package loads in the host's process, so loading it is not
isolation. The capability limits stop mistakes. They do not stop a package
that sets out to get around them. Packages are trusted code that the operator
installs and reviews. Out-of-process isolation comes with multi-tenant
hosting ([D7](../decisions.md#d7-custom-code-lives-in-extension-packages--agreed)).

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

*(planned for M5)*

- **Sample package.** `samples/packages/` holds one package with a pure
  operation and no capabilities. The purchase request sample uses it. Its
  operation is chosen in the issue that builds it.
- **Test package.** A test-only package under `tests/packages/` covers the
  capability limits: a declared binding is handed out, and an undeclared one
  fails the attempt.
