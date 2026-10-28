# D7. Custom code lives in extension packages

Status: **Agreed**

Logic that does not fit expressions is written in C# as an extension package,
for example file parsing, complex calculations or system integrations.

- **Repository and build:** a package is a separate project with its own unit
  tests, a version and a build artifact.
- **Typed operations:** each operation in a package declares its inputs and
  outputs.
- **Declared behaviour:** each operation also declares the capabilities it
  needs (data access, outbound hosts, secrets) and whether it is idempotent.
- **How it runs:** processes call these operations through the same operation
  catalog as built-in operations. The engine keeps control of transactions,
  retries, history and inspection.
- **Isolation:** the first version loads packages in-process through
  `AssemblyLoadContext` and gives them only the platform APIs they declared.
  Out-of-process isolation comes with multi-tenant hosting.
  - [D90](d90-extension-packages-run-out-of-process.md) replaces this point:
    packages run out of process from M21, without waiting for multi-tenant
    hosting.

_Why:_ real applications always need some custom code. Without an official
place for it, code ends up as unsafe inline scripts or as
application-specific platform code.
