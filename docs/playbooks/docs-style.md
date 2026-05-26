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
| **Speculation in reference docs** ("Not yet implemented", "planned design", "Will be wired") | Reads as reference, is actually guesswork; the real design will diverge | Put forward-looking content in `docs/PROGRESS.md` (current status) or an epic feature file (spec). Enforced for `docs/ARCHITECTURE.md` by the drift script. |
| **Duplicating versions / paths / commands** across docs | Both copies drift; readers don't know which is canonical | Link to the owner doc (see ownership table) |
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
| Epic README | 200 lines | Move detail into per-feature files |
| Feature file | no hard cap | Already scoped by AC list |

`patterns.md` is the only intentional outlier — its size is mitigated by the lazy-load index pattern. Don't replicate that escape hatch elsewhere without the same mitigation.

---

## Feature files — wireframes & implementation status

Every `docs/epics/*/features/F0N-*.md` file uses these layouts so agents can scan status without parsing inline pipes.

### Wireframes (top of file, after title / back-link)

```markdown
## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| login | [source](../wireframes/login.excalidraw) | [preview](../wireframes/login.svg) |
```

One row per screen. **Do not** stack multiple `> **Wireframe**:` blockquote lines — they are hard to read and drift from the table format.

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
> **Gaps vs spec:** …
> **Done:** …
> **Deferred (PR #N follow-up):** …
> **Decisions:** …
```

Rules:

- **One row per layer** — split `Domain + Application` into two rows.
- **`Gaps vs spec`** lists remaining AC bullets; never write `pending API layer` when endpoints already exist — say what is missing (`403 test`, `date filter query param`, etc.).
- **`API ✅`** on a US means in-scope REST/OpenAPI AC for that story are shipped; Frontend-only gaps do not downgrade API to ⚠️.

**Bulk normalize:** `python3 scripts/normalize-feature-docs.py` (also enforced by `check-doc-drift.sh` on changed feature files).

---

## When you add a new `.md` file

1. Add the back-link header (per [`docs/README.md`](../README.md)): `> **Navigation**: [← parent.md](...)` so future readers can climb back up.
2. Add a row to the relevant table in `docs/README.md` (playbooks / diagrams / wireframes).
3. If it owns a topic, add it to the **Single source of truth** table.
4. If the topic could be enforced mechanically, add a rule to `scripts/check-doc-drift.sh` — that is what makes the convention survive.

---

## When you touch any `.md` file

Boy-scout pass: while you're in the file, scan for the anti-patterns above. Stale links and `⏳` / `TODO` lines that have since shipped are the most common rot. The cost of removing one is seconds; the cost of leaving it accumulates.
