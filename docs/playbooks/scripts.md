# Scripts

> **Navigation**: [← docs/README.md](../README.md) · [← agent-checklist.md](./agent-checklist.md) · [← AGENTS.md](../../AGENTS.md)

`scripts/axis.py` is the source of truth for repository maintenance commands.
Documented repo workflows and CI `run:` steps go through Axis wrappers. Native
tools such as Docker, .NET, npm, Buf, Lychee, OpenSSL, and grpcurl stay behind
the wrapper as implementation details.

## Tool Versions

Application/runtime versions live in [TECH_STACK.md](../TECH_STACK.md) and
manifest files such as [`frontend/.nvmrc`](../../frontend/.nvmrc). This section
owns repo-maintenance CLI versions and records which source each local gate uses.
Local tools must match the documented source instead of adapting config at
runtime.

| Tool | Required version source | Used by | Local enforcement |
|---|---:|---|---|
| .NET SDK | `8.x` in [TECH_STACK.md](../TECH_STACK.md), selected by [`global.json`](../../global.json) | `python scripts/axis.py verify`, `python scripts/axis.py dotnet ...`, `test unit`, package scans, API contract generation | `global.json` must select major `8`; Axis checks the resolved SDK major before any wrapped .NET workflow runs. |
| Node.js | [`frontend/.nvmrc`](../../frontend/.nvmrc) | `python scripts/axis.py verify`, `python scripts/axis.py frontend ...`, API type generation, legacy wireframe export | Axis checks the resolved Node major and package-manager availability before wrapped frontend workflows run, and resolves matching nvm installs when non-interactive shells do not load nvm into `PATH`. |
| Lychee | `0.23.0` exactly in this table | `python scripts/axis.py check markdown-links`, CI Markdown link check | `lychee.toml` is written for the 0.23 config schema; Axis fails fast when another version resolves. |
| Buf CLI | `1.50.0` exactly in this table | `python scripts/axis.py check buf-lint`, `python scripts/axis.py check buf-breaking-against-base`, CI protobuf checks | Axis checks the resolved Buf version before wrapped protobuf workflows run. |

Install Lychee and Buf from their upstream releases at the documented versions,
then verify the active toolchain with `python scripts/axis.py doctor` or the
specific Axis checks below. Do not document raw installer/version commands as a
repo workflow.

## Commands

```bash
python scripts/axis.py doctor
python scripts/axis.py verify
python scripts/axis.py dotnet restore
python scripts/axis.py dotnet build
python scripts/axis.py dotnet test
python scripts/axis.py dotnet format --check
python scripts/axis.py dotnet run-api
python scripts/axis.py dotnet ef migrations add <Name> ...
python scripts/axis.py frontend install
python scripts/axis.py frontend ci
python scripts/axis.py frontend test
python scripts/axis.py frontend gen-api-types --check
python scripts/axis.py docs sync-mermaid-theme
python scripts/axis.py docs mermaid-init
python scripts/axis.py buf list-breaking-rules
python scripts/axis.py grpc list
python scripts/axis.py grpc call <method> --data '{"example":"value"}'
python scripts/axis.py local-dev certs
python scripts/axis.py local-dev up
python scripts/axis.py local-dev status
python scripts/axis.py local-dev e2e
python scripts/axis.py check policy-tests
python scripts/axis.py check codex-skills
python scripts/axis.py check text-encoding
python scripts/axis.py check docker
python scripts/axis.py check dotnet-sdk
python scripts/axis.py check frontend-toolchain
python scripts/axis.py check vulnerable-packages
python scripts/axis.py check ef-domain-mapping
python scripts/axis.py check frontend-api-contracts
python scripts/axis.py check frontend-style
python scripts/axis.py check frontend-component-composition
python scripts/axis.py check frontend-quality
python scripts/axis.py check doc-drift
python scripts/axis.py check markdown-links
python scripts/axis.py check buf-cli
python scripts/axis.py check buf-lint
python scripts/axis.py check buf-modules
python scripts/axis.py check buf-breaking-against-base
python scripts/axis.py check local-dev-docs
python scripts/axis.py check doc-link-targets
python scripts/axis.py check scripts-standard
python scripts/axis.py check doc-navigation
python scripts/axis.py check doc-size-budgets
python scripts/axis.py check doc-code-fences
python scripts/axis.py check use-case-docs
python scripts/axis.py check kafka-wiring
python scripts/axis.py check domain-readme-index
python scripts/axis.py check pr
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
- Document repo-owned workflows through `scripts/axis.py`; raw underlying
  commands belong inside the wrapper, not in docs, repo skills, or CI `run:` steps.
- Put shared repository discovery in `scripts/axis_repo.py` or small Python helpers.
- Do not add ad hoc utility scripts in any runtime under top-level `scripts/`,
  `docs/scripts/`, `docs/wireframes/`, or `docs/diagrams/`.
- Native ecosystem tooling belongs beside the owning package, with a package
  script and a `scripts/axis.py` entrypoint for any repo workflow.
- Top-level `scripts/*.py` files are run through `python scripts/axis.py` and
  should stay non-executable.
- `scripts/hooks/pre-push` is the committed source for the local Git hook. Keep
  it non-executable in the worktree; `python scripts/axis.py install-hooks`
  writes the executable copy to `.git/hooks/pre-push`.
- Use Python JSON/path/process APIs instead of shell string manipulation when parsing
  Markdown, OpenAPI, Avro, YAML-like config, or Git output.
- New deterministic guards must encode a reusable invariant, not memorialize one
  incident. Avoid exact route names, translation keys, CSS class fragments, or
  screen-specific component names unless those strings are the source-of-truth
  contract being checked. If the finding depends on visual judgment or a single
  historical case, keep it in [REVIEW_FINDINGS.md](../REVIEW_FINDINGS.md) as
  review-only guidance instead of adding a repo gate.
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
`python scripts/axis.py check doc-drift` enforces documented repo workflows
through Axis wrappers and rejects raw workflow commands in docs.
`python scripts/axis.py check doc-size-budgets` enforces reference/playbook size
budgets and is included in doc drift.
