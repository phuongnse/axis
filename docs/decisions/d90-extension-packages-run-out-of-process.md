# D90. Extension packages run out of process

Status: **Agreed**

## Context

[D28](d28-extension-packages.md) loads package code into the worker, and
[D7](d7-custom-code-lives-in-extension-packages.md) left isolation for later. There a package can read
every tenant's secrets, reach any host and run past its timeout, so its declared capabilities stop
mistakes but not a package that sets out to get around them.

## Decision

- **Package runners run package operations.** They are processes of their own host,
  `Axis.PackageHost`, in a container beside the worker, one set per package version, shared by
  tenants, each running one call at a time.
- **A call hands over only what it declares:** the input and the bindings of the call's tenant. Package
  code and manifests do not change.
- **The capabilities become limits:** an empty environment, a read-only file system, CPU and memory
  limits, and one network route, through a proxy that admits only the declared hosts.
- **A timeout kills the runner,** and the outcome is unknown, under the rules of D28.
- **The server never executes package code.**
- **In-process loading stays for Development only.**

The detail is in [package runners](../reference/extension-packages.md#package-runners).

## Consequences

- A package sees only its call's secrets and reaches only its declared hosts, and a runaway call stops
  at its timeout.
- A runner per version costs less than one per tenant, but static state can still mix tenants' data,
  so packages stay trusted code that the operator reviews.
- Each installation runs one more container.
