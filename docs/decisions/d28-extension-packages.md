# D28. Extension packages

Status: **Agreed**

## Context

[D7](d7-custom-code-lives-in-extension-packages.md) puts custom code in extension packages. A
package needs a format, a way to be installed and versioned, and a way for a process to call it that
keeps the guarantees of [D11](d11-durable-process-engine.md): no external effect inside a
transaction, and no blind retry.

## Decision

- **A package is a .NET class library** with its own unit tests and a SemVer version. It references
  only `Axis.Extensions`, a small contracts assembly, and its build gives a folder with its
  assemblies and a manifest.
- **The manifest declares each operation:** its typed input and output, whether it is idempotent,
  its timeout and its capabilities, which are the `url` bindings it may call and the `secret`
  bindings it may read.
- **Packages are installed once per installation,** shared by every tenant. An application uses only
  the packages it declares, each with a version range.
- **A release pins an exact version,** the highest installed one in each range, so a running instance
  keeps the code it was compiled against. A host that lacks a version that running work pins does
  not start, as [D29](d29-release-pinning-and-text-resources.md) says.
- **Every package operation runs like a connector call,** staged outside the step's transaction as in
  [D25](d25-connectors-deployment-bindings-and-secrets.md), even a pure one. The operation identity
  is its idempotency key, it follows the step's retry policy, and an unknown outcome is retried only
  when the operation declares `idempotent: true`.
- **The context hands out only what the operation declares.** Asking for anything else fails the
  attempt, even when the operation catches the error.
- **Packages run in package runners outside the worker,** as
  [D90](d90-extension-packages-run-out-of-process.md) agrees. Loading a package into the host's
  process is allowed only in Development.
- **Packages are trusted code** that the operator installs and reviews.

The detail is in [extension packages](../reference/extension-packages.md) and
[package operations](../reference/process-execution.md#package-operations).

## Consequences

- Custom code stays out of database transactions, and retries and unknown outcomes have one rule for
  connectors and packages.
- A package is still reviewed before it is installed, because the limits cannot stop every misuse,
  as D90 says.
- An operator must keep installed every version that running work pins.

## Alternatives

- **Run a pure operation inside the step's transaction.** It would save a staged call, but give
  packages two rules instead of one.
