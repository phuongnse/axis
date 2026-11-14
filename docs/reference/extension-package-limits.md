# Extension package limits

This page says what the limits on an extension package's operations do not protect against. The
package, its manifest and its runners are in [extension packages](extension-packages.md).

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

Outside Development, an installation's packages run in [package runners](extension-packages.md#package-runners). There, the
runner's network route, empty environment, read-only file system, resource limits and kill at the
timeout stop the items in the list above, and the runner holds nothing of the host to reach by
reflection. Static state shared across tenants is the one item still possible, because one set of
runners serves every tenant.

- **Runners for each tenant** _(planned for M37)_. An application's packages run in runners that serve
  only their tenant, which closes that gap, see [An application's packages](extension-packages.md#an-applications-packages).
