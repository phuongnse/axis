# Use Cases

> **Navigation**: [← docs/README.md](../README.md) · [← AGENTS.md](../../AGENTS.md)

Use cases are the user-facing source of truth for behavior. **One markdown file = one use case** (flow, AC, design sources, diagrams, implementation status). Domain `README.md` indexes all use cases in that area.

Create a full use-case README only when the work is shipped, being implemented, or being specified for the next concrete slice. Distant product ideas belong in a lightweight roadmap/open-work note until they are ready for doc-first implementation. The checker rejects new/touched placeholder sections and stock Main flow text, so a committed use-case file must describe real behavior rather than act as a blank reservation.

---

## How agents find open work

**Do not use `- [ ]` checkboxes in use-case files as progress** — they stay unchecked by convention (spec only). Use this order:

| Step | Source | What you learn |
|------|--------|----------------|
| 1 | Domain **Open work** in `docs/use-cases/{domain}/README.md` | Prioritized gaps (backend vs frontend) |
| 2 | `docs/use-cases/{domain}/*.md` | Per-use-case ACs + `> **Implementation status**` + `Gaps vs spec` / `Deferred follow-ups` / `Decisions` |
| 3 | `docs/PROGRESS.md` | Module layer summary; cross-cutting foundation phases |
| 4 | `grep -rE "\\| Application \\| ⚠️\\|\\| Infrastructure \\| ⚠️\\|\\| API \\| ⚠️" docs/use-cases/` | Use cases with partial backend layers ([agent-checklist](../playbooks/agent-checklist.md)) |

**Symbols** (per layer on this use case, not the whole module):

| Symbol | Meaning |
|--------|---------|
| ✅ | All in-scope backend AC for this layer are implemented (or Frontend-only when layer is Frontend). |
| ⚠️ | Layer partially shipped — read `**Gaps vs spec**`. |
| ⏳ | Layer not started for this use case. |
| N/A | Layer does not apply. |

When you ship code, update **use-case callout → domain README → PROGRESS** in the same PR. Never mark ✅ while `**Gaps vs spec**` still lists backend work for that layer.

**Use-case file layout:** Purpose/Actor/Trigger, flow sections, AC, optional **Screen flow**, design sources table, diagrams table, implementation status — see [docs-style § Use-case visual artifacts](../playbooks/docs-style.md#use-case-files--design-sources--implementation-status). Multi-screen reference: [register-workspace](./platform-foundation/register-workspace/README.md).

---

## Domains

| Domain | Scope |
|--------|--------|
| [platform-foundation](./platform-foundation/README.md) | Workspace registration, workspace management, isolation, subscription plans |
| [identity-access](./identity-access/README.md) | Authentication, users, roles, permissions, security, i18n/theming |
| [data-modeling](./data-modeling/README.md) | Models, field types, data classes, record CRUD |
| [workflow-builder](./workflow-builder/README.md) | Canvas, steps, triggers, branching, parallel, import/export |
| [form-builder](./form-builder/README.md) | Forms, fields, workflow integration, submissions |
| [workflow-engine](./workflow-engine/README.md) | Execution, handlers, errors, history, retry |
| [page-builder](./page-builder/README.md) | Pages, widgets, drag & drop, data binding |

---

## Platform core loop

```
Platform setup → Identity & users → Model data → Build workflows
    → Add forms → Execute & monitor → Build pages & widgets → End users
```

---

## Structure

```text
docs/use-cases/
├── README.md
├── USE_CASE_TEMPLATE.md
└── <domain>/
    ├── README.md            # domain index + grouped use-case links
    └── <short-slug>/        # one folder per use case (short, readable name)
        ├── README.md        # spec (flow, AC, implementation status)
        └── *.svg            # optional committed previews for this use case
```

Editable design sources are linked from each use-case `## Design Sources` table; workflow rules live in [design-source.md](../playbooks/design-source.md). Legacy shared wireframe references live in [`../wireframes/`](../wireframes/) — agent contract: [wireframes/README](../wireframes/README.md#agent-contract). Platform diagrams: [`../diagrams/`](../diagrams/).

---

## Distributed-ready foundation (cross-domain)

Distributed foundation status is tracked in [PROGRESS.md](../PROGRESS.md) (phases + cross-domain rollout notes).
Use this page for feature/use-case navigation; use [PROGRESS.md](../PROGRESS.md) for platform phase details.
