# D90. Extension packages run out of process

Status: **Agreed**

The owner agreed this on 2026-10-25. It replaces the isolation point of
[D7](d7-custom-code-lives-in-extension-packages.md), that out-of-process
isolation comes with multi-tenant hosting, and the loading and "Not a sandbox"
points of [D28](d28-extension-packages.md) for every host outside
Development. Hosted SaaS stays out of scope, as
[D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)
says, so isolation no longer waits for it. Everything here is _(planned for
M18)_. See [package runners](../reference/extension-packages.md#package-runners).

- **A package runner:**
  - A new host, `Axis.PackageHost`, runs package operations. The worker no
    longer loads package code.
  - Each installed package version gets its own runner processes, shared by
    every tenant. Each runner process runs one call at a time. The worker
    keeps a small pool for each version and starts a new runner when one
    exits.
  - The runners run in their own container, `package-host`, beside the worker.
- **One call, one request:**
  - The worker sends the runner one request per attempt, over a local socket.
    It holds the operation name, the operation identity, the input, and the
    values of only the bindings the operation declares, for the call's tenant.
  - The runner implements the same `IOperationContext`. Package code and
    manifests do not change.
- **Limits that now hold:**
  - The runner starts with an empty environment. It sees no tenant's secrets
    except the bindings of the call it runs.
  - It has a read-only file system, apart from a temporary folder, and CPU
    and memory limits.
  - Its only network route is an egress proxy in the worker. The proxy lets
    through only the scheme, host and port of the `url` bindings the
    operation declares, and follows no redirects.
  - At the operation's `timeout` the worker kills the runner process. A
    runaway thread no longer keeps running.
- **Outcomes stay the same:** the staging, retry and unknown-outcome rules
  of D28 hold. A runner that crashes or is killed during an attempt gives an
  unknown outcome. It is retried only when the operation declares
  `idempotent: true`.
- **The server runs no package code:** it reads assemblies through a
  metadata-only load context to check a manifest against its assembly, and
  never executes them.
- **Setting:** `Extensions:Isolation` is `process` by default. `inProcess`
  keeps the in-process loading of D28, and is allowed only in Development.
  Elsewhere startup fails.
- **Still trusted code:** the operator still installs and reviews packages.
  One runner serves every tenant, so static state in a package can still mix
  tenants' data, and a package can still misuse the data a call hands it.

_Why:_ in-process loading lets any package read every tenant's secrets and
reach any host, which the docs could only warn about. A separate process with
an empty environment and one network route makes the declared capabilities
real limits. A runner for each package version, shared by tenants, costs
less than one for each tenant. Running one call at a time lets a timeout kill
only that call.
