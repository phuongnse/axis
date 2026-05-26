# F06 — Parallel Step Execution

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| workflow-editor | [source](../wireframes/workflow-editor.excalidraw) | [preview](../wireframes/workflow-editor.svg) |


[← Back to E04](../README.md)

---

## Description

Multiple steps can run concurrently inside a Parallel Group. The workflow fans out, runs them in parallel, and waits for all (or any) to complete before continuing.

---

## US-069 — Create a parallel step group

**As an** Organization Member, **I want to** configure multiple steps to run in parallel **so that** independent tasks don't block each other.

**Acceptance Criteria:**

*Happy path*
- [ ] "Add Parallel Group" option in the step type sidebar adds a special container node to the canvas.
- [ ] Steps are added inside the Parallel Group by dragging from the sidebar into the group container.
- [ ] The canvas shows the group as a bordered region with a clear visual distinction from sequential steps.
- [ ] Connections enter the group at the group's input handle and exit at the group's output handle.

*Validation & errors*
- [ ] A Parallel Group with fewer than 2 steps blocks publishing: "A Parallel Group must contain at least 2 steps."
- [ ] A Condition step inside a Parallel Group is allowed; branching within a parallel branch is valid.
- [ ] Steps outside the Parallel Group cannot be connected to steps inside it (bypassing the group boundary is not allowed).

*Edge cases*
- [ ] Nested Parallel Groups (a Parallel Group inside another Parallel Group) are not supported in MVP; the canvas blocks this configuration.
- [ ] A Form step inside a Parallel Group is valid; the group waits for all form submissions before completing (with AND join).

*Out of scope*
- Dynamic parallelism (creating N parallel branches based on a list of records at runtime) — not in MVP.

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
> **Gaps vs spec:** canvas container node rendering and step nesting UI pending Frontend; parallel group represented via step config in existing JSONB storage. `ParallelGroup` and `JoinType` are Phase 2 — shown as planned (dashed) in diagram.

---

## US-070 — Configure fan-in (join) behavior

**As an** Organization Member, **I want to** configure how the workflow continues after parallel steps complete **so that** I can handle different completion scenarios.

**Acceptance Criteria:**

*Happy path*
- [ ] The Parallel Group config panel has a "Join type" selector:
  - **Wait for all (AND)** — default; continues when all branches complete.
  - **Wait for first (OR)** — continues when any one branch completes; remaining branches are cancelled.
- [ ] The selected join type is shown as a label on the group's output handle on the canvas.

*Validation & errors*
- [ ] If join type is AND and any branch fails, the entire Parallel Group is marked as Failed immediately; remaining running branches are cancelled.
- [ ] If join type is OR and the first branch fails (before any other branch succeeds), the group continues waiting for another branch. If all branches fail, the group fails.

*Edge cases*
- [ ] With OR join, a branch that is still running when the group completes receives a cancellation signal. Long-running operations (e.g., HTTP requests) are given a 5-second grace period before being forcibly terminated.
- [ ] Changing the join type after the workflow is published requires creating a new version.

*Out of scope*
- "Wait for N of M" join type (e.g., wait for 2 out of 3 branches) — not in MVP.

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
> **Gaps vs spec:** AND/OR join execution, branch cancellation, and grace period pending E06.

---

## US-071 — Access results from parallel branches

**As an** Organization Member, **I want to** use the output of all parallel steps in subsequent steps **so that** I can combine results.

**Acceptance Criteria:**

*Happy path*
- [ ] After the Parallel Group completes, execution context contains outputs from all completed branches, namespaced by step ID: `{{context.step_a.field}}`, `{{context.step_b.field}}`.
- [ ] Subsequent steps (after the group) can reference these values in expressions and config fields.

*Validation & errors*
- [ ] Referencing a parallel branch's output in a step that is itself inside the same Parallel Group (sibling branch) is blocked at design time: the context variable picker does not offer sibling branch outputs.

*Edge cases*
- [ ] With OR join, branches that did not complete (were cancelled) have `null` values in the context under their step IDs.
- [ ] If two parallel branches write to the same output variable name (via Script steps), the value from whichever branch completes last wins. A design-time warning is shown when this is detected.

*Out of scope*
- Merging/reducing outputs from parallel branches with built-in aggregation functions — not in MVP; use a Script step after the group for custom aggregation.

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
> **Gaps vs spec:** context namespacing by step ID and sibling-output blocking pending E06; design-time duplicate output warning backend polish — see gaps below.
