# Scripts

> **Navigation**: [← docs/README.md](../README.md) · [← agent-checklist.md](./agent-checklist.md) · [← AGENTS.md](../../AGENTS.md)

`scripts/axis.py` is the default source of truth for repository maintenance
commands. Prefer Python for repo-level policy, docs checks, and orchestration.
Use ecosystem-native tooling when the underlying tool is native to that
ecosystem, such as legacy Excalidraw SVG export in `frontend/scripts/`.

## Tool Versions

Application/runtime versions live in [TECH_STACK.md](../TECH_STACK.md) and
manifest files such as [`frontend/.nvmrc`](../../frontend/.nvmrc). This section
owns repo-maintenance CLI versions and records which source each local gate uses.
Local tools must match the documented source instead of adapting config at
runtime.

| Tool | Required version source | Used by | Local enforcement |
|---|---:|---|---|
| .NET SDK | `8.x` in [TECH_STACK.md](../TECH_STACK.md), selected by [`global.json`](../../global.json) | `python scripts/axis.py verify`, `test unit`, package scans, API contract generation | `global.json` must select major `8`; `dotnet --version` major must be `8`; another major fails before any `dotnet` command runs. |
| Node.js | [`frontend/.nvmrc`](../../frontend/.nvmrc) | `python scripts/axis.py verify`, frontend checks/tests, API type generation, legacy wireframe export | `node --version` major must match `.nvmrc`; `npm` must resolve from `PATH`. |
| Lychee | `0.23.0` exactly in this table | `python scripts/axis.py check markdown-links`, CI Markdown link check | `lychee.toml` is written for the 0.23 config schema. `lychee --version` must print `lychee 0.23.0`; newer versions fail fast until this row and config are intentionally upgraded together. |
| Buf CLI | `1.50.0` exactly in this table | `python scripts/axis.py check buf-lint`, `python scripts/axis.py check buf-breaking-against-base`, CI protobuf checks | `buf --version` must print `1.50.0`; local protobuf commands must go through `scripts/axis.py` wrappers, not raw `buf`, so the version check always runs first. |

Install Lychee from the upstream release for `v0.23.0`, or via Cargo if Rust is
already available:

```bash
cargo install lychee --version 0.23.0 --locked --force
lychee --version
```

If `lychee --version` reports another version, fix `PATH` or reinstall before
running the markdown link check.

Install Buf CLI from the upstream release for `v1.50.0`, then verify:

```bash
buf --version
```

If `buf --version` reports another version, fix `PATH` or reinstall before
running protobuf checks.

## Commands

```bash
python scripts/axis.py doctor
python scripts/axis.py verify
python scripts/axis.py check policy-tests
python scripts/axis.py check codex-skills
python scripts/axis.py check text-encoding
python scripts/axis.py check dotnet-sdk
python scripts/axis.py check frontend-toolchain
python scripts/axis.py check doc-drift
python scripts/axis.py check markdown-links
python scripts/axis.py check buf-cli
python scripts/axis.py check buf-lint
python scripts/axis.py check scripts-standard
python scripts/axis.py check doc-navigation
python scripts/axis.py check doc-size-budgets
python scripts/axis.py test unit
python scripts/axis.py generate api-contracts
python scripts/axis.py generate wireframes
python scripts/axis.py generate buf-yaml
python scripts/axis.py generate domain-readme-index
python scripts/axis.py register avro-schemas --dry-run
```

## Rules

- Keep new repo-level maintenance and docs policy scripts in Python.
- Add subcommands to `scripts/axis.py` for new repo workflows.
- Put shared repository discovery in `scripts/axis_repo.py` or small Python helpers.
- Do not add ad hoc utility scripts in any runtime under top-level `scripts/`,
  `docs/scripts/`, `docs/wireframes/`, or `docs/diagrams/`.
- Native ecosystem tooling belongs beside the owning package, with a package
  script and, when useful for repo workflows, a `scripts/axis.py` entrypoint.
- Top-level `scripts/*.py` files are run through `python scripts/axis.py` or
  explicit `python ...` commands and should stay non-executable.
- `scripts/hooks/pre-push` is the committed source for the local Git hook. Keep
  it non-executable in the worktree; `python scripts/axis.py install-hooks`
  writes the executable copy to `.git/hooks/pre-push`.
- Use Python JSON/path/process APIs instead of shell string manipulation when parsing
  Markdown, OpenAPI, Avro, YAML-like config, or Git output.
- Diff-aware local checks must include the PR range plus staged, unstaged, and
  untracked files. They must not report "no diff" while the working tree is dirty.
- Repo-scoped Codex skills under `.agents/skills/` must have valid `SKILL.md`
  frontmatter, concise bodies, concrete wording, existing doc references, required
  skill chaining, matching UI metadata, and a default prompt that invokes the
  skill by `$skill-name`.

`python scripts/axis.py check scripts-standard` enforces the no-ad-hoc-script
rule and is included in doc drift.
`python scripts/axis.py check codex-skills` enforces repo-scoped skill structure
and is included in doc drift.
`python scripts/axis.py check doc-size-budgets` enforces reference/playbook size
budgets and is included in doc drift.
