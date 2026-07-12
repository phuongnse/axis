# Docs Style

> **Navigation**: [docs/README.md](../README.md) · [AGENTS.md](../../AGENTS.md)

Docs are scanned, not read. Use `$axis-doc-hygiene` for docs edits; keep workflow in skills and deterministic checks in scripts.

## Ownership

Every fact has one owner: version, path, command, process, table shape, or rule. Other docs link to the owner inline, in the sentence that uses it.

Do not add reference-owner sections or bottom reference lists for repo docs.

## Prose

- Lead with the rule.
- Use path-labeled repo links such as [docs/TECH_STACK.md](../TECH_STACK.md).
- Keep reasons to one clause.
- Prefer tables for matrices.
- Classify by responsibility, not current file inventory.
- Delete sentences that do not carry a rule, fact, or decision.
- Use examples only when they generalize.

## Avoid

| Avoid | Use |
|---|---|
| Long workflow prose | A repo skill |
| Duplicate commands or versions | Link to the owner |
| Reference-style links for repo docs | Inline markdown links |
| Owner dumps | Inline owner links |
| Display labels like `[Tech Stack]` | Path labels like `[docs/TECH_STACK.md]` |
| Old-name warning lists | One-time sweep plus current owner links |
| Review-only guidance called a gate | [docs/ENFORCEMENT.md](../ENFORCEMENT.md) status terms |

## Size

`AGENTS.md`, [docs/ARCHITECTURE.md](../ARCHITECTURE.md), playbooks, and pattern routers stay under 100 lines. Move workflow into skills before adding prose.

## Use-Case Files

Use-case READMEs own product behavior and ACs. Keep purpose/actor/trigger, flows, ACs, Acceptance Test Matrix, Out Of Scope, optional Screen flow, optional Diagrams, and implementation status.

Use `$axis-use-case-spec` for spec shape, `$axis-use-case-implementation` for status, and `$axis-doc-hygiene` for diagrams or committed visual artifacts.

Foundation READMEs use the same spec/status shape under [docs/foundations/README.md](../foundations/README.md); use `$axis-frontend-foundation` for app shell or shared SPA foundation contracts.

Use-case and foundation docs marked complete keep exact proof in the sibling `{slug}.evidence.md` file. Spec files keep `Acceptance Test Matrix` high-level; sidecars keep `Acceptance Evidence` rows with committed proof paths and Axis wrapper commands. A row may list comma-separated AT IDs only when the proof paths and commands are identical.

## Implementation Status

Use one row per layer. Status values are `Done`, `Partial`, `Not started`, and `N/A`.

Each status block must include `Gaps vs spec`, `Deferred follow-ups`, `Verification`, and `Decisions`. Use `N/A` only when there is no current item.

Never combine `Done` with pending work, and do not keep historical "Done" logs unless they describe current status.

## New Doc Files

Create a new doc only for a separate topic large enough to justify another file.

Every `docs/**/*.md` file starts with an H1 and a navigation line; targets are relative to the current file:

```markdown
> **Navigation**: [docs/README.md](../README.md) · [AGENTS.md](../../AGENTS.md)
```

Labels are repo paths ending in `.md`; separate multiple links with ` · `; do not use arrows, pipes, or prose labels in navigation.
