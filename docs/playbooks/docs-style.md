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

### Wireframes (in each use-case folder)

```markdown
## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| login | [source](./login.excalidraw) | [preview](./login.svg) |
| shared-app-shell | [source](../../../wireframes/app-shell.excalidraw) | [preview](../../../wireframes/app-shell.svg) |
```

- Put screens **only used by this use case** **flat** inside the use-case folder (next to `README.md`).
- Reference **shared kit screens** (e.g. `app-shell`, `_template`) from `.../wireframes/` — do not copy duplicates.
- Use `N/A` rows when no wireframe applies.
- One table per `README.md` — no blockquote wireframe stacks.

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

1. Add the back-link header (per [`docs/README.md`](../README.md)): `> **Navigation**: [← parent.md](.)` so future readers can climb back up.
2. Add a row to the relevant table in `docs/README.md` (playbooks / diagrams / wireframes).
3. If it owns a topic, add it to the **Single source of truth** table.
4. If the topic could be enforced mechanically, add a rule to `scripts/check-doc-drift.sh` — that is what makes the convention survive.

---

## When you touch any `.md` file

Boy-scout pass: while you're in the file, scan for the anti-patterns above. Stale links and `⏳` / `TODO` lines that have since shipped are the most common rot. The cost of removing one is seconds; the cost of leaving it accumulates.
