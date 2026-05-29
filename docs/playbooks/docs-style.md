# Docs style

> **Navigation**: [← docs/README.md](../README.md) · [← CLAUDE.md](../../CLAUDE.md)

Short anti-pattern checklist for everything under `docs/`. Read once; come back when adding a new file. Most of these are enforced by [`scripts/check-doc-drift.sh`](../../scripts/check-doc-drift.sh) and the **Markdown link check** CI job — the doctrine here exists so the rules feel justified, not arbitrary.

---

## Single owner per topic

Every fact — a version, a file path, a command, a step list — has **one** owner. Every other doc that needs it **links**, never repeats. The current ownership table lives in [`docs/README.md` § Single source of truth](../README.md#single-source-of-truth-per-topic).

When you find yourself editing the same fact in two files, the architecture is wrong: collapse to one owner + N pointers.

---

## Anti-patterns (don't ship these)

| Anti-pattern | Why it rots | Do instead |
|---|---|---|
| **Speculation in reference docs** ("Not yet implemented", "planned design", "Will be wired") | Reads as reference, is actually guesswork; the real design will diverge | Put forward-looking content in `docs/PROGRESS.md` (current status) or a domain use-case file (spec). Enforced for `docs/ARCHITECTURE.md` by the drift script. |
| **Duplicating versions / paths / commands** across docs | Both copies drift; readers don't know which is canonical | Link to the owner doc (see ownership table) |
| **Duplicating compose ports / service URLs** | Playbooks drift from `docker-compose.yml` | Owner: [local-dev.md](./local-dev.md) + compose file; enforced by [`check-local-dev-docs.py`](../../scripts/check-local-dev-docs.py) |
| **Aspirational metrics** in engineering docs (e.g. "50 customers in 6 months") | Nobody measures or tests against them; they age into embarrassment | Keep in pitch deck / `PRODUCT_VISION.md` if anywhere; do not pollute technical reference |
| **Empty "TODO: fill later" sections** | Look authoritative, contain nothing, lie to readers | Delete the section. Add it when there's content to add. |
| **"Process about process"** docs > 100 lines | Nobody reads them; the rules don't get followed | Embed the rule into the **drift script** or a **template**. Doctrine without enforcement is decoration. |
| **New file for content that fits in an existing file** | Doc graph fragments; agents have to read more files to get less | Absorb into the closest existing file. New file only when topic is genuinely separate **and** ≥ ~50 lines worth. |

---

## Size budgets

| File class | Budget | Action when exceeded |
|---|---|---|
| Reference docs (`CLAUDE.md`, `ARCHITECTURE.md`, playbooks) | 300 lines | Split by topic, extract a sub-playbook, or move detail into an index + section pattern |
| `patterns.md` | exempt | Use [`patterns-index.md`](./patterns-index.md) as the entry point; never read end-to-end |
| Domain README | 200 lines | Move detail into per-use-case files |
| Use-case file | no hard cap | Already scoped by AC list |

`patterns.md` is the only intentional outlier — its size is mitigated by the lazy-load index pattern. Don't replicate that escape hatch elsewhere without the same mitigation.

---

## Use-case files — wireframes & implementation status

Every `docs/use-cases/<domain>/<short-slug>/README.md` file uses these layouts so agents can scan status without parsing inline pipes.

**Canonical example (multi-screen + diagrams):** [platform-foundation/register-org/README.md](../use-cases/platform-foundation/register-org/README.md). Copy that structure when refreshing older use cases.

### Section order (after AC, before implementation status)

```text
## Screen flow      ← when rules below apply
## Wireframes
## Diagrams
> **Implementation status**
```

### When to add `## Screen flow`

Add **`## Screen flow`** when **any** of these is true:

- More than **three** wireframe screens in the use-case folder (including `*-states` / error variants), or
- The happy path has **branches** (e.g. SSO vs email/password), or
- Error screens are easy to confuse with sequential steps.

Skip `## Screen flow` when there are **zero or one** local screen wireframes (a single `## Wireframes` table row is enough).

### `## Screen flow` (content rules)

1. One sentence: row order in `## Wireframes` matches this section.
2. **Happy path** table: `| Step | Screen | When |` — use `1`, `2a`, `2b`, `3` for branches; screen slug in backticks. If a branch reuses the same UI (e.g. 2b submit on step 1’s screen), say so in **Screen** — it does not get a second wireframe row; merge step ids in the wireframes `#` column (e.g. `1 · 2b`) and label the edge in mermaid.
3. **Error / reference** table (separate): screens that are **not** sequential steps — when to open each (validation, 5xx, provider errors).
4. Optional **mermaid** `flowchart` — solid edges = happy path, dotted = error/reference (keep labels = screen slugs).

Do **not** duplicate Excalidraw links here; links live only in `## Wireframes`.

### `## Wireframes` (content rules)

- Assets live **flat** in the use-case folder (`<screen>.excalidraw` + `.svg` next to `README.md`).
- Reference **shared kit** screens from `../../../wireframes/` (e.g. `app-shell`) — do not copy files into the use-case folder.
- One table per README — no blockquote wireframe stacks.
- Opening line (when `## Screen flow` exists): state how many screens, that order matches Screen flow, and that **sequence/architecture** drawings are under `## Diagrams`.
- **Inventory:** every `*.excalidraw` **UI screen** in this folder must have a row. Include error/reference variants (`*-states`). Use `N/A` only when the use case truly has no wireframes.
- **Columns:** minimum `| Screen | Excalidraw | Preview |`. When `## Screen flow` exists, use `| # | Screen | Role | Excalidraw | Preview |` — `#` matches flow steps (`1`, `2a`, `—` for non-step error screens); `Role` = short happy-path / error label.
- **Row order:** happy-path screens first (same order as Screen flow), then error/reference screens (same order as the error table in Screen flow).
- **Do not** list diagrams here (e.g. `*-flow`, `tenant-provisioning`, entity models) — those are not UI screens.

Minimal table (few screens, no Screen flow):

```markdown
## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| login | [source](./login.excalidraw) | [preview](./login.svg) |
```

Full table (see [register-org](../use-cases/platform-foundation/register-org/README.md#wireframes)):

```markdown
## Wireframes

All UI assets in this folder (N screens). Row order matches [Screen flow](#screen-flow) above.
Sequence/architecture drawings are under [Diagrams](#diagrams).

| # | Screen | Role | Excalidraw | Preview |
|---|--------|------|------------|---------|
| 1 | … | Happy path — … | [source](./….excalidraw) | [preview](./….svg) |
| — | …-states | Error — … | [source](./….excalidraw) | [preview](./….svg) |
```

### `## Diagrams` (content rules)

- **Mermaid only** in this README (`sequenceDiagram`, `flowchart`, `erDiagram`, …) — one `### <diagram-slug>` section per diagram. First line inside each fence: `MERMAID_INIT` from [`mermaid-theme.mjs`](../diagrams/mermaid-theme.mjs) ([playbook](./mermaid.md)).
- **Standard set (multi-screen user journeys):**
  - **One** `### <slug>-journey` `sequenceDiagram` — actor happy path from first screen through the use-case outcome (use `rect rgb(22, 35, 58)` phase bands; keep SSO/error branches minimal — details belong in `*-cases`).
  - **Zero or one** `### <slug>-cases` `sequenceDiagram` — dev/QA map of API responses to `*-states` wireframes. Skip when there are few error screens.
  - **Do not** add a second happy-path sequence (e.g. avoid both `*-flow` and `*-journey` for the same story).
- Platform-wide architecture diagrams live in [docs/README.md § Key Diagrams](../README.md#key-diagrams), not duplicated here.
- **Related use cases:** prefer **one use-case folder** for a single actor journey (e.g. register → verify → provisioning wait). Add extra `###` diagram sections (`tenant-provisioning`, …) instead of a second README. Link out only when the story is truly a different actor/domain. Do not paste another use case’s Mermaid in a separate folder if it is the same journey.
- After diagrams, optional **APIs** or **Related** line — anchors within the same README (`#tenant-provisioning`), not duplicate folders.
- Omit `## Diagrams` when this use case has no local diagram.

### Adopting this layout on existing use cases

When touching an older use case that only has a flat wireframes table:

1. Count `*.excalidraw` in the folder — **screens only** (UI wireframes). Sequence/entity content belongs in `## Diagrams` as Mermaid, not as `.excalidraw` files.
2. Add or refresh `## Screen flow` if the when-to-add rules apply.
3. Expand `## Wireframes` so **every screen file** has a row; add `#` / `Role` if helpful.
4. Move cross-use-case diagram references into `**Related:**` prose (link to the other README’s `###` anchor).
5. Regenerate `.svg` if **wireframe** `.excalidraw` changed; run [`visual-artifact-checklist.md`](./visual-artifact-checklist.md). Preview Mermaid after diagram edits.

No need to bulk-edit all use cases in one PR — update the use case you are already changing, and align siblings in the same domain when obvious.

### Implementation status (after each US AC block)

```markdown
> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ✅ |
> | Application | ✅ |
> | Infrastructure | ✅ |
> | API | ✅ |
> | Frontend | ⏳ |
>
> **Gaps vs spec:** none for backend on this US.
>
> **Done:**
> - Handler X; endpoint Y.
>
> **Deferred (PR #N follow-up):** Frontend-only gap (one line).
>
> **Decisions:** …
```

Rules:

- **One row per layer** — split `Domain + Application` into two rows.
- **Blank blockquote line** (`>`) between **Gaps vs spec**, **Done**, **Deferred**, and **Decisions** — never glue `**Done:**` onto the same line as **Gaps vs spec**.
- Use a **bullet list** under **Done** (or **Gaps vs spec**) when there are several semicolon-separated backend notes; keep a single sentence on one line when it is short.
- **`Gaps vs spec`** lists remaining AC bullets; never write `pending API layer` when endpoints already exist — say what is missing (`403 test`, `date filter query param`, etc.).
- **`API ✅`** on a US means in-scope REST/OpenAPI AC for that story are shipped; Frontend-only gaps do not downgrade API to ⚠️.

**Bulk validate:** `python3 scripts/check-use-case-docs.py --check` (also run via `check-doc-drift.sh`).

---

## Use case files (flow-first)

Use case files should be self-contained and user-facing:

1. Purpose / actor / trigger
2. Main flow
3. Alternate/error flows
4. Acceptance criteria
5. Wireframes table (mapped to this use case)
6. Diagrams table (mapped to this use case; explicit N/A if not needed yet)
7. Implementation status callout

Avoid writing engineering process constraints as end-user use cases. Keep those in shared playbooks and gates (for example `frontend.md`, `agent-checklist.md`, `CLAUDE.md`).

---

## When you add a new `.md` file

1. Add the back-link header (per [`docs/README.md`](../README.md)): `> **Navigation**: [← parent.md](...)` so future readers can climb back up.
2. If it belongs in the docs hub: add a playbook row, a [Key Diagrams](../README.md#key-diagrams) index link, or a [Wireframes](../README.md#wireframes) domain pointer — **not** a per-screen wireframe file row (those stay in the owning use-case `## Wireframes` only).
3. If it owns a topic, add it to the **Single source of truth** table.
4. If the topic could be enforced mechanically, add a rule to `scripts/check-doc-drift.sh` — that is what makes the convention survive.

---

## When you touch any `.md` file

Boy-scout pass: while you're in the file, scan for the anti-patterns above. Stale links and `⏳` / `TODO` lines that have since shipped are the most common rot. The cost of removing one is seconds; the cost of leaving it accumulates.
