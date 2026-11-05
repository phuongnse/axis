# D5. Configuration is authored as files first

Status: **Agreed**

## Context

Configuration has to be reviewed, diffed and tested in CI like code, and AI agents have to be able
to author it. A visual editor built before the contracts of the resources settle would have to be
rewritten.

## Decision

- **JSON files.** A resource is a JSON file, one resource per file. Each file has a stable ID and a
  human-readable name, and is validated against a published JSON Schema.
- **A folder in git.** An application is a folder of resource files in git, and Axis compiles that
  folder into a release.
- **The same contracts everywhere.** The authoring APIs use the same contracts as the files and
  update a resource in part, and the visual Studio writes the same resources through them, as
  [D38](d38-studio-edits-application-folders-through-an-authoring-api.md) says.

The file shape is in [resource files](../reference/resource-files.md#resource-file-shape), and the
compile in the [configuration pipeline](../reference/configuration.md).

## Consequences

- Configuration is reviewed, diffed and tested in CI, and an agent can author it.
- Studio cannot store anything that a file cannot hold.
