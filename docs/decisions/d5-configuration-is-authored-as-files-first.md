# D5. Configuration is authored as files first

Status: **Agreed**

- **Format:** resources are JSON files, one resource per file. Each file has a
  stable ID and a human-readable name, and is validated against a published
  JSON Schema.
- **Where they live:** an application folder in git. Axis compiles that folder
  into a release.
- **Authoring APIs:** the APIs use the same contracts and support partial
  updates.
- **Studio:** a visual Studio writes the same resources.

_Why:_ files can be reviewed, diffed and tested in CI, and AI agents can author
them. Building a visual editor before the contracts settle would mean
rewriting it.
