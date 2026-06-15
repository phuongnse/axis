# Scripts

> **Navigation**: [← docs/README.md](../README.md) · [← agent-checklist.md](./agent-checklist.md) · [← AGENTS.md](../../AGENTS.md)

`scripts/axis.py` is the source of truth for repository maintenance commands.
Top-level maintenance scripts under `scripts/` should be Python only.

## Commands

```bash
python scripts/axis.py doctor
python scripts/axis.py verify
python scripts/axis.py check policy-tests
python scripts/axis.py check text-encoding
python scripts/axis.py check doc-drift
python scripts/axis.py check scripts-standard
python scripts/axis.py check doc-navigation
python scripts/axis.py test unit
python scripts/axis.py generate api-contracts
python scripts/axis.py generate buf-yaml
python scripts/axis.py generate domain-readme-index
python scripts/axis.py register avro-schemas --dry-run
```

## Rules

- Keep new top-level maintenance scripts in Python.
- Add subcommands to `scripts/axis.py` for new workflows.
- Put shared repository discovery in `scripts/axis_repo.py` or small Python helpers.
- Do not add Bash/PowerShell maintenance scripts under `scripts/`.
- Use Python JSON/path/process APIs instead of shell string manipulation when parsing
  Markdown, OpenAPI, Avro, YAML-like config, or Git output.

`python scripts/axis.py check scripts-standard` enforces the script-standard rule and is
included in doc drift.
