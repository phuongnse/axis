# F05 — Branching & Conditional Logic

> **Wireframe**: [docs/epics/E04-workflow-builder/wireframes/workflow-editor.excalidraw](../wireframes/workflow-editor.excalidraw) · [preview](../wireframes/workflow-editor.svg)

[← Back to E04](../README.md)

---

## Description

Workflows can take different execution paths based on data values using Condition steps. This enables if/else and multi-branch switch logic.

---

## US-066 — Add an if/else branch

**As an** Organization Member, **I want to** route my workflow down different paths based on a condition **so that** different scenarios are handled appropriately.

**Acceptance Criteria:**

*Happy path*
- [ ] Adding a Condition step creates it with two default outgoing handles: "If true" and "If false."
- [ ] Each handle can be connected to different subsequent steps.
- [ ] The expression builder (see US-059) is used to define the condition.
- [ ] Canvas edges show the branch label ("If true" / "If false") next to the arrow.

*Validation & errors*
- [ ] A Condition step with only one outgoing connection (missing the other branch) blocks publishing with: "The Condition step '{name}' must have at least 2 outgoing branches."

*Edge cases*
- [ ] Both branches of an if/else can converge back to the same downstream step (diamond pattern). The downstream step executes once, whichever branch reaches it first.
- [ ] A Condition step's expression can reference output from any preceding step in the workflow, not only the immediately previous step.

*Out of scope*
- Loop-back branching (sending execution back to an earlier step) — cycles are blocked in MVP.

> **Implementation status** — Domain: ✅ | Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: canvas branch label rendering pending Frontend; branch evaluation at execution time pending E06.
> Decisions: cycle detection implemented in domain (DFS reachability check in AddTransition).

---

## US-067 — Add a multi-branch condition

**As an** Organization Member, **I want to** add more than two branches from a Condition step **so that** I can handle multiple distinct cases.

**Acceptance Criteria:**

*Happy path*
- [ ] "+ Add branch" button in the Condition step config panel adds an additional named branch with its own expression.
- [ ] Each branch has a user-defined label (editable) and an expression.
- [ ] A "Default" branch (no expression) can be added; at most one default branch per step.
- [ ] Branches are evaluated in the order shown; the first matching branch wins.
- [ ] Branch order can be changed via drag-and-drop within the config panel.

*Validation & errors*
- [ ] Attempting to add a second Default branch is blocked: "Only one default branch is allowed."
- [ ] A branch without a label is blocked: "Branch label is required."

*Edge cases*
- [ ] If no branch matches and there is no Default branch, the step fails at execution time with: "No condition branch matched and no default branch is configured."
- [ ] A Condition step with 10 or more branches still renders correctly on the canvas, with the config panel scrollable.

*Out of scope*
- Regex-based branch matching — not in MVP.

> **Implementation status** — Domain: ✅ | Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: branch drag-to-reorder UI and default-branch validation at publish pending Frontend + API.

---

## US-068 — Merge branches back to a single path

**As an** Organization Member, **I want** diverged branches to merge back to a single step **so that** the workflow continues on a unified path after branching.

**Acceptance Criteria:**

*Happy path*
- [ ] Multiple incoming edges on a single step node are allowed and visually shown as converging arrows.
- [ ] The merge step executes as soon as any one incoming branch reaches it (OR-merge semantics by default for simple branching).
- [ ] Context from the branch that reached the merge step is carried forward; context from branches that were not taken is not present.

*Validation & errors*
- [ ] A step that is a merge point (multiple incoming edges) and also has its own complex config (e.g., HTTP Request) works normally — there is no restriction on which step types can act as merge points.

*Edge cases*
- [ ] If both branches of an if/else reach the merge point (e.g., both run a Notification step then merge), the merge step executes exactly once (the second arrival is ignored). This is the expected behavior and is documented in the execution history.
- [ ] This OR-merge behavior is distinct from the Parallel Group fan-in (AND-join) behavior described in F06.

*Out of scope*
- Explicit merge/join nodes on the canvas — merging is implicit (any step with multiple incoming edges acts as a merge point). An explicit Join node is used only in Parallel Groups (F06).

> **Implementation status** — Domain: ✅ | Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: OR-merge deduplication (execute-once on first arrival) is an execution engine concern — pending E06.
