# D28. Extension packages

Status: **Agreed**

M5 lets a process run custom C# code from an extension package. The manifest,
the contracts, installation and loading are built. Version pinning, the
package operation step, its retries and the capability limits are built too.
The planned activation check for a missing pinned version is replaced by
[D29](d29-release-pinning-and-text-resources.md)'s start check.
See [extension packages](../reference/extension-packages.md) for
the manifest, the contracts, installation, loading and capabilities,
[configuration](../reference/extension-packages.md#declaring-packages-in-an-application) for an
application's `packages`, and
[processes](../reference/process-execution.md#package-operations) for the package
operation step. The owner confirmed these points on issue #313 on
2026-10-12.

- **What a package is:** a .NET class library with its own unit tests and a
  SemVer version.
  - It references only `Axis.Extensions`, a small contracts assembly with the
    operation interface and the operation context.
  - Its build artifact is a folder with the assemblies and a manifest,
    `axis-package.json`.
  - Data access from packages comes later.
- **Manifest:** it holds the package id, its version and its operations.
- **Each operation:** it declares typed `input` and `output` fields, whether
  it is idempotent, its `timeout` and its capabilities. The capabilities are
  the `url` bindings it may call and the `secret` bindings it may read.
- **Installation:** packages are installed per installation, in the folder
  that the server and worker setting `Extensions:Path` names. There is one
  subfolder per package id and version. Every tenant sees the same installed
  packages, and an application uses only those it declares.
- **Dependencies:** an application manifest declares its `packages`, each
  with a version range.
  - A range is an exact version `x.y.z`, `^x.y.z` for the same major
    version, or `~x.y.z` for the same minor version.
  - Versions are plain `major.minor.patch`. Pre-release versions come later.
- **Pinning:** compilation picks the highest installed version in each range
  and pins that exact version in the release. A range that no installed
  version meets is a diagnostic.
- **Loading:** each package version loads in its own `AssemblyLoadContext`.
  It shares only `Axis.Extensions` with the host. A manifest that does not
  match the assembly stops activation with a diagnostic.
- **Package operation step:** an `operation` step with
  `operation: "<package>/<operation>"`.
  - Its `input` maps operation inputs to expressions over the subject
    record. Its `output` maps operation outputs to subject fields.
  - It has `next` and an optional `retry`.
  - Its inputs and outputs are type-checked against the manifest of the
    pinned version.
- **Runs like a connector call:** every package operation runs outside the
  step transaction, staged like a connector call in
  [D25](d25-connectors-deployment-bindings-and-secrets.md). This holds
  even for a pure operation, so there is one rule for all package
  operations.
  - The operation identity is its idempotency key, the same on every
    attempt.
  - It follows the same retry policy as a connector call.
  - An output that does not fit the manifest fails the call. So does any
    other exception.
  - A `RetryableException` from `Axis.Extensions` is a retryable attempt. So
    is a pinned version that is missing on the worker.
  - A timeout, or a call found still `sending` after a crash, is an unknown
    outcome. It is retried only when the operation declares
    `idempotent: true`. Otherwise it stays unknown until an operator [resolves](../reference/process-inspection.md#resolve) it.
- **Timeout:** each operation declares its own `timeout`. It defaults to
  `PT30S` and is at most `PT2M`, as for connectors.
- **Capabilities:** the operation context gives the operation an HTTP client
  that reaches only its declared `url` bindings, and only its declared
  secrets.
  - Anything else the context is asked for throws, and the attempt fails.
    The attempt fails even if the operation catches the exception.
  - Secret values never appear in history, audit records or logs.
- **Audit:** each settled call writes one audit record `operation.called`,
  with actor `system`. History and audit records hold the exception type of a
  failed attempt, never its message.
- **Not a sandbox:** loading in-process is not isolation. A package that uses
  .NET APIs directly bypasses these limits.
  - A package can read environment variables, including every tenant's
    secrets.
  - It can also open its own HTTP clients and sockets, and ignore
    cancellation.
  - Packages are trusted code that the operator installs and reviews.
  - Out-of-process isolation comes with multi-tenant hosting
    ([D7](d7-custom-code-lives-in-extension-packages.md)).
  - [D90](d90-extension-packages-run-out-of-process.md) replaces this point
    and the loading point above: from M17, packages run in package runners
    outside the worker, and in-process loading stays only for Development.
- **Sample package:** `samples/packages/` holds one package with a pure
  operation and no capabilities. The purchase request sample uses it. The
  sample package is `payment-references`, whose operation computes an ISO 11649
  creditor reference. A test-only package
  under `tests/` covers the capability limits.

_Why:_ see [knowledge](../domain/knowledge.md#logic-in-configuration) and
[D7](d7-custom-code-lives-in-extension-packages.md). Some logic does not
fit expressions, and a package gives it a tested, versioned home. Pinning the
exact version in the release keeps a running process on the code it was
compiled against. Staging every call like a connector call in
[D25](d25-connectors-deployment-bindings-and-secrets.md) keeps custom
code out of database transactions, and gives retries and unknown outcomes one
rule. The capability limits stop mistakes, not hostile code, so the docs say
plainly that a package is trusted code.
