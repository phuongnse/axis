---
name: axis-frontend-foundation
description: Build Axis app shell and shared SPA foundation slices that are not owning use cases. Use for app frame, shared authenticated layout, navigation chrome, route frames, design-system primitives, cross-route UI infrastructure, or reusable frontend behavior that enables future use cases without adding a product workflow.
---

# Axis Frontend Foundation

## Goal

Create or tighten the owning app shell or shared SPA foundation spec so implementation can follow spec -> tests -> code -> verification without pretending the foundation is a product use case.

## Hard gates

Follow [reference.md](../reference.md).
- If the request has an actor goal, business side effect, validation flow, or acceptance criteria, route to `$axis-use-case-spec` or `$axis-use-case-implementation`.
- Use `$axis-design-gate` before non-trivial layout, route, component, or behavior changes; high-risk surfaces still stop for sign-off.
- Use `$axis-frontend-feature` for SPA implementation after the foundation contract is clear.
- Use `$axis-doc-hygiene` when changing guidance, routing tables, or durable frontend rules.
- Do not claim review-ready without `$axis-ready-review`.

## Inputs

- Foundation name and scope, such as app frame, shared navigation, or design-system primitive.
- Existing routes, components, tokens, translations, tests, and dependent use-case mentions.
- The smallest durable contract needed by future use cases.

## Workflow

1. Locate or create the owning foundation spec.
   - Continue here only when the change enables screens or workflows but does not itself complete a user goal.
   - If a user action is in scope, name the owning use case first and let that use case own product outcomes.
   - Read [docs/foundations/README.md](../../../docs/foundations/README.md) and the surface `README.md`.
   - Search existing docs and code with `rg -n "<foundation words>" docs/foundations docs/use-cases frontend/src tests`.
   - If no foundation file exists, create `docs/foundations/{surface}/{slug}.md` and add the surface README link.
   - If the surface folder does not exist, stop and ask for scope unless the user explicitly approved a new surface.
   - Do not add placeholder files under `docs/foundations` or `docs/use-cases`.

2. Read governing rules.
   - Read [AGENTS.md](../../../AGENTS.md), [docs/playbooks/frontend.md](../../../docs/playbooks/frontend.md), [docs/playbooks/testing.md](../../../docs/playbooks/testing.md), and [docs/playbooks/agent-checklist.md](../../../docs/playbooks/agent-checklist.md).
   - Read existing same-surface components, routes, translations, and tests with `rg`.
   - Read related use-case docs only to preserve dependencies; do not expand their acceptance criteria from foundation work.

3. Establish foundation boundaries before writing behavior.
   - Capture Purpose, Primary actor, Trigger, Main flow, Alternate/error flows, Acceptance Criteria, Acceptance Test Matrix, Out Of Scope, optional Screen flow, optional Diagrams, implementation status, and Decisions.
   - Use local `AC-001` and `AT-001` IDs, matching use-case spec style.
   - Name what the foundation guarantees, which routes or components consume it, and what remains owned by future use cases.
   - Keep the contract product-neutral: slots, navigation affordances, responsiveness, accessibility, localization, and interaction boundaries.
   - Record other durable rules in [docs/playbooks/frontend.md](../../../docs/playbooks/frontend.md) only when future agents need that rule outside the current diff.

4. Implement the foundation.
   - Build on existing Axis components, tokens, translations, and routing patterns.
   - Keep visible copy in the frontend translation layer.
   - Do not add API contracts, auth behavior, storage behavior, or product actions unless the owning use case or contract skill requires them.
   - Avoid design-system component API or visual deviations unless the user has explicitly signed off.

5. Test observable behavior.
   - Use Vitest and Testing Library for component or route behavior.
   - Assert navigation, responsive affordances, accessible labels, localized copy, and enabled/disabled behavior when in scope.
   - Use Playwright or an available browser-capable tool for layout-sensitive desktop/mobile evidence when the app is runnable.
   - Before marking every implementation status row `Done` or `N/A`, create or update the sibling `{slug}.evidence.md` sidecar with `## Acceptance Evidence` rows for every required AT. Each row must name committed evidence files and exact `python scripts/axis.py ...` commands; group comma-separated AT IDs in one row only when those cells are identical.
   - Do not put evidence paths or runner commands in the foundation spec file.
   - Do not count temporary browser smoke, screenshots, console output, or manual inspection as required AT evidence unless the proof is committed and referenced by the evidence table.

6. Verify and report.
   - Run `python scripts/axis.py check foundation-docs` when foundation docs changed.
   - Run `python scripts/axis.py check repo-skills` when skill text or routing changes.
   - Run the narrow frontend check selected by `$axis-script-scope`.
   - Report docs/status updates, checks, visual evidence, and the next owning use case or foundation follow-up.

## Output

Report:

```text
Spec created/updated:
- ...

Decisions resolved:
- ...

Open decisions:
- ...

Next skill:
- ...

Checks:
- ...
```
