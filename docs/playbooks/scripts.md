# Scripts

> **Navigation**: [← docs/README.md](../README.md) · [← agent-checklist.md](./agent-checklist.md) · [← AGENTS.md](../../AGENTS.md)

`scripts/axis.py` is the default source of truth for repository maintenance
commands. Prefer Python for repo-level policy, docs checks, and orchestration.
Use ecosystem-native tooling when the underlying tool is native to that
ecosystem, such as Excalidraw SVG export in `frontend/scripts/`.

## Commands

```bash
python scripts/axis.py doctor
python scripts/axis.py verify
python scripts/axis.py check policy-tests
python scripts/axis.py check codex-skills
python scripts/axis.py check text-encoding
python scripts/axis.py check doc-drift
python scripts/axis.py check markdown-links
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
- `scripts/hooks/pre-push` has no extension because Git expects that filename, but
  its entrypoint must be Python and delegate to `scripts/axis.py pre-push`.
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
