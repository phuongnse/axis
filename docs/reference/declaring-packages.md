# Declaring packages

How an application declares the extension packages it uses, and how the release pins their versions.

## Declaring packages in an application

An application declares the extension packages it uses in its manifest, `application.json`. Each
entry names a package id and a version range:

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
`{ "id": "payment-references", "version": "^1.0.0" }`, and also declares
`payment-terms` the same way. Without a package installed, the sample does not compile:
`AXC0139` at `/packages/0/id` or `/packages/1/id` names it.

- `packages` is optional. Each entry has an `id`, lower-case letters, digits
  and `-`, starting with a letter, and a `version`. The schema checks this
  shape, so breaking it is `AXC0004`. Each package id appears at most once:
  `AXC0142`.
- `version` is a version range in one of three forms. Versions are plain
  `major.minor.patch` without leading zeros. Any other form is `AXC0140`.
  - `1.0.0` is that exact version.
  - `^1.0.0` is any version from `1.0.0` with the same major version, so
    `1.4.2` but not `2.0.0`. `^0.2.0` also means the same major version, so
    `0.9.0` but not `1.0.0`.
  - `~1.0.0` is any version from `1.0.0` with the same minor version, so
    `1.0.3` but not `1.1.0`.
- **`runner`.** An entry of `application.json`'s `packages` may say where the package
  runs: `axis`, the default, or `customer`. With `customer`, the release still pins the version, the pin and
  each step that calls the package are marked customer-hosted, and activation checks no binding of its
  operations, see [capabilities](extension-packages.md#capabilities). A module's entry has no `runner`, and the
  application's entry decides for every process of the release, because the release holds one pin per
  package id. A wrong value is `AXC0004`. An application's own package says it in its manifest instead.
- **A customer-hosted package's calls** _(planned for M37)_. They start no Axis runner and go to a
  [customer-hosted runner](extension-packages.md#customer-hosted-runners).
- **Pinning.** Compilation picks the highest installed version in each range and pins it in the
  release, in the Release step of the [pipeline](configuration.md#configuration-pipeline), as
  [versions and pinning](extension-packages.md#versions-and-pinning) says. A module's `packages` share
  the release's pins, which hold one version of each package id. A module
  uses the application's pin when the application lists the package too, and
  its entry is `AXC0141` in `module.json` when that pin is outside its range.
  Otherwise the release pins the highest installed version that meets the
  range of every module that lists the package, and a package with no such
  version is `AXC0141` in each of those modules' `module.json`. An application
  process sees only the application's own packages.
- **Diagnostics.** `AXC0139`, a package id with no installed version, and `AXC0141`, a range that
  no installed version meets, are compile errors in `application.json`. Compilation sees only the
  versions that loaded at host startup. A manifest that does not match its package is reported at
  startup, as `AXC0132` to `AXC0138`, see [Loading](extension-packages.md#loading), and a pinned version missing at
  startup is `AXC0146`, see [versions and pinning](extension-packages.md#versions-and-pinning).
- An application uses only the packages it declares, even though every
  tenant sees the same installed packages. Installation is in
  [Installation](extension-packages.md#installation).
- How a process calls a package operation is in
  [Package operations](process-execution.md#package-operations).
