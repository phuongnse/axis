# D148. Packages in C#, TypeScript or Python run in runners that Axis or the customer hosts

Status: **Agreed**

## Context

[D7](d7-custom-code-lives-in-extension-packages.md) puts custom code in extension packages written in
C#, [D28](d28-extension-packages.md) makes a package a .NET class library that the operator installs
for the whole installation, and [D90](d90-extension-packages-run-out-of-process.md) runs it in package
runners that every tenant shares, because packages are trusted code. Those rules fit a package that
the operator's own developers write from scratch.

The code a business wants to run is seldom new. It already has the logic, often in a library in
TypeScript, Python or C#, published to its own registry, and rewriting it in C# costs more than the
change it serves. A connector reaches a system that runs as a service with an API, but a library that
no service exposes has no API to call. And anyone may write the code now: the operator's developers, a
partner, the business's own developers, or an agent that the business asks in words, as
[D119](d119-ai-authoring-in-studio.md) agrees. In an Axis SaaS cell, code that a tenant brings runs
beside up to 1,000 other tenants, so it cannot be trusted the way an operator's package is.

## Decision

This decision replaces D7, D28 and D90, and restates what still holds from them.

- **Custom code lives in extension packages,** never in configuration, as D7 said. A package adds typed
  operations that process steps call, and it cannot read or write entities.
- **A package is written in C#, TypeScript or JavaScript on Node.js, or Python.** It is a folder with a
  manifest, a thin adapter in one of those languages and a lock file of its dependencies. The manifest declares each
  operation's typed input and output, whether it is idempotent, its timeout and its capabilities: the
  `url` bindings it may call and the `secret` bindings it may read. The package has a SemVer version.
- **A package reuses the code a business already has.** Its dependencies may be the business's own
  libraries, from a public or private registry, as built files or as a commit of a git repository. The
  adapter maps an operation to a function of the library, and the library's code does not change.
- **A package is built once, when it is installed or published.** Its dependencies are installed from
  the lock file and scanned for known vulnerabilities, and a runner downloads nothing at run time.
- **Every package operation runs like a connector call,** as D28 said: staged outside the step's
  transaction, with its operation identity as idempotency key, under the step's retry policy, and an
  unknown outcome is retried only when the operation declares `idempotent: true`.
- **Package runners run package operations, and the server never executes package code.** A runner
  runs one call at a time. The worker and every runner speak one protocol that no language owns, and
  a small SDK for each language implements the operation context over it. For each attempt the worker
  sends the operation, its identity, the input and the values of the bindings the operation declares,
  for the call's tenant. Running a C# package inside the worker is allowed only in Development.
- **A package's source decides how far it is trusted:**
  - **An installation's package** is installed by the operator, by itself or inside a module installed
    for the installation, such as a built-in module or a partner's module. It is trusted code that the
    operator reviews, and its runners are shared by every tenant, one set for each package version, as
    D90 said.
  - **An application's package** sits in the application's folder, and the tenant's author brings it.
    It is not trusted. Its runners serve only its tenant, run in a sandbox stronger than a container,
    and their use counts against the tenant's quotas. In Axis SaaS only a paid plan allows it, and the
    tenant's administrator turns it on and gives the permission to write a package to each author. An
    installation without that sandbox refuses an application's package, and runs its own packages as
    before.
- **Axis or the customer hosts a runner.** A runner that Axis hosts starts with an empty environment, a
  read-only file system apart from a temporary folder, CPU and memory limits, and one network route,
  through a proxy that admits only the hosts of the declared `url` bindings, and it is killed at the
  operation's timeout with an unknown outcome, as D90 said. A library's own HTTP calls go through that
  proxy, so it needs no change. A runner that the customer hosts runs in the customer's own network,
  connects out to the installation with a credential of its own that the tenant's administrator can
  revoke, and takes only that tenant's calls to the packages it registers. It keeps its own secrets,
  and the worker sends it no binding value, so the code, its secrets and the systems it reaches never
  leave the customer.
- **A release pins the code it runs.** It pins the exact version of each installation's package, the
  highest installed one in each declared range, as D28 said, and it holds the built application's
  packages, so running work keeps the code it started with.
- **An author writes a package by hand or asks an agent for it.** By hand, the author edits its files
  in Studio, or in their own tools through the authoring API. An agent writes a package's adapter,
  manifest and tests over a library the author names only through an authoring request, the one place
  in Axis where an agent works, under the plan, review and accept of
  [D125](d125-how-ai-authoring-connects-runs-and-keeps-its-changes.md), and it never publishes.

## Consequences

- A business runs the logic it already has without rewriting it, and without running a service of its
  own for it.
- Axis maintains a runner and an SDK for each of three runtimes, and each runner image carries its
  language's runtime. Another language is a new runner and SDK, not a new design.
- An installation's packages keep D90's limits and its one gap: static state in a shared runner can
  still mix tenants' data, so they stay code the operator reviews.
- An application's packages close that gap with runners per tenant, but cost more to run, and a cell
  needs a sandbox stronger than a container, which on-premises installations may leave out when they
  hold no tenant's code.
- Publishing an application with a package takes longer, because it builds and scans the package.
- A customization layer cannot change a module's code. An application that needs other code adds a
  package of its own and calls it, or ejects the module.
- A customer-hosted runner keeps the engine's guarantees of staging, retries and history, but none of
  the runner limits, because the customer owns the machine.

## Alternatives

- **C# only, as D7 had it.** It is one runtime to run and secure, but every business would rewrite its
  logic in C# first.
- **WebAssembly components** for every language. They give a strong sandbox with a fresh instance for
  each call, but much existing code and many libraries do not compile to WebAssembly, and the support
  for some languages is still experimental.
- **A container for each package.** Any language and library runs, but each call or package needs an
  orchestrator, which is too heavy for a small installation and for a cell of 1,000 tenants.
- **A resource kind for functions** beside the other kinds. Code needs a build, dependencies and a
  runtime that no other resource has, and it would be a second way to run custom code beside packages.
- **Connectors only.** They reach a system that runs a service, but a library with no service would
  need the business to build, host and run one first.
