# D7. Custom code lives in extension packages

Status: **Agreed**

## Context

Real applications always need some logic that expressions cannot hold, such as file parsing or
system integrations. Without an official place for it, code ends up as unsafe scripts in
configuration or as application-specific platform code, as
[logic in configuration](../domain/knowledge.md#logic-in-configuration) describes.

## Decision

- **A package is C# in a project of its own,** with unit tests, a version and a build artifact.
- **Each operation declares** its typed inputs and outputs, the capabilities it needs and whether it
  is idempotent.
- **Processes call package operations as they call built-in ones,** and the engine keeps control of
  transactions, retries, history and inspection.
- **Packages run out of process** and get only what they declare, as
  [D90](d90-extension-packages-run-out-of-process.md) agrees.

The design is in [D28](d28-extension-packages.md) and
[extension packages](../reference/extension-packages.md).

## Consequences

- Custom code is tested and versioned like any other code, and configuration and the platform stay
  free of it.
- Custom code needs a developer and an installed package.
