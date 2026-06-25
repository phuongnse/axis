# Docs Style

> **Navigation**: [<- docs/README.md](../README.md) . [<- AGENTS.md](../../AGENTS.md)

Docs are scanned, not read. Use `$axis-doc-hygiene` for docs edits. Keep policy short, put workflow in skills, and let scripts enforce deterministic checks.

## Single owner per topic

Every fact has one owner: version, path, command, process, table shape, or rule. Other docs link to the owner.

## Prose style

- Lead with the rule.
- Keep the reason to one clause.
- Prefer tables for matrices.
- Classify by responsibility, not by listing current files.
- Prefer high-level strict rules over low-level detail inventories.
- Delete sentences that do not carry a rule, fact, or decision.
- Use examples only when they generalize.

## Anti-patterns

| Avoid | Use instead |
|---|---|
| Long workflow prose | A repo skill |
| Duplicate commands or versions | Link to the owner |
| Inventory of current files/tools | Responsibility taxonomy |
| Placeholder sections | Delete until real |
| Incident detail in rules | Use-case, `PROGRESS.md`, or PR retro |
| Review-only guidance called a gate | [REVIEW_FINDINGS.md](../REVIEW_FINDINGS.md) terms |

## Size budgets

`AGENTS.md`, `docs/ARCHITECTURE.md`, playbooks, and pattern routers stay under 100 lines. Move workflow into skills before adding prose.

## Use-case files — design sources & implementation status

Use-case READMEs own product behavior and ACs. Keep this shape: purpose/actor/trigger, flows, ACs, Acceptance Test Matrix when touched, optional Screen flow/Design Sources/Diagrams, implementation status.

Use `$axis-use-case-spec` for spec shape, `$axis-use-case-implementation` for status, and `$axis-visual-artifact` for visual artifacts.

## Design Sources

```markdown
## Design Sources

| Screen | Source | Preview |
|---|---|---|
| register | [source](https://design.example/source-frame) | [preview](./register.svg) |
```

Rules: Source links are editable artifacts; non-`N/A` previews need an editable source in the same row; Mermaid owns local use-case diagrams.

### Diagrams (content rules)

Use Mermaid for local use-case diagrams. Put workflow, sequence, and entity diagrams under `## Diagrams` in the owning use-case README.

## Implementation status (after each US AC block)

```markdown
> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Domain | ✅ |
> | Application | ✅ |
> | Infrastructure | ✅ |
> | API | ✅ |
> | Frontend | ⏳ |
>
> **Gaps vs spec:** N/A.
>
> **Deferred follow-ups:** N/A.
>
> **Decisions:** N/A.
```

Rules: one row per layer; `⚠️` names exact gaps; deferrals name exact ACs or `N/A`; avoid historical "Done" logs unless needed for current status.

## New doc files

Create a new doc only for a separate topic large enough to justify another file. Every `docs/**/*.md` file starts with an H1 and `> **Navigation**:`.
