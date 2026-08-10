# Axis Repo Skills

Repo skills are executable workflows. [reference.md](./reference.md) owns universal execution semantics; [workflows.toml](./workflows.toml) owns cross-skill state and transition topology; this catalog owns intent routing; each linked skill owns only its domain workflow.

## Usage

1. Select one entry owner from the catalog by user intent.
2. Read [reference.md](./reference.md) and the full owner skill before editing.
3. Follow only handoffs explicitly typed as **Requires**, **Delegates**, or **Returns to**.
4. Reuse current evidence; do not recurse into an already-satisfied prerequisite.
5. Apply [reference.md § Agent routing](./reference.md#agent-routing) to current work units after choosing the workflow owner, and re-evaluate unexecuted units when their ambiguity, scope, ownership, or verification changes; model choice does not change ownership or gates.
6. Run repeatable repository workflows and verification through finite `python scripts/axis.py ...` routes; `$axis-script-scope` owns rare mutation exceptions.

`$axis-*` aliases map to `.agents/skills/<name>/SKILL.md`.

## Responsibility catalog

| Intent | Entry owner | Boundary |
|---|---|---|
| Risk dossier, sign-off, or retirement | [axis-design-gate/SKILL.md](./axis-design-gate/SKILL.md) | Decides risk before implementation; does not implement the surface |
| Draft or repair a product contract | [axis-use-case-spec/SKILL.md](./axis-use-case-spec/SKILL.md) | Owns use-case readiness and AC/AT authoring |
| Implement a documented product slice | [axis-use-case-implementation/SKILL.md](./axis-use-case-implementation/SKILL.md) | Orchestrates layer work and acceptance evidence |
| Decide module boundaries or foundational patterns | [axis-module-architecture/SKILL.md](./axis-module-architecture/SKILL.md) | Decides architecture; does not implement tactical patterns |
| Implement adopted module patterns | [axis-module-patterns/SKILL.md](./axis-module-patterns/SKILL.md) | Implements only decisions required by current ACs or architecture |
| Change REST/OpenAPI wire shape | [axis-api-contract/SKILL.md](./axis-api-contract/SKILL.md) | Owns contract shape, generation, and parity |
| Change MCP tools, API coverage, auth boundary, or MCP maintenance workflow | [axis-mcp-integration/SKILL.md](./axis-mcp-integration/SKILL.md) | Owns typed MCP operation parity, mutation safety, and agent-facing process |
| Design and implement a client journey | [axis-frontend-feature/SKILL.md](./axis-frontend-feature/SKILL.md) | Owns self-directed experience, route, state, form, recovery, and feature behavior |
| Define shared SPA foundation contracts or nonvisual mechanics | [axis-frontend-foundation/SKILL.md](./axis-frontend-foundation/SKILL.md) | Owns product-neutral routing, provider, shell-state, scroll, and context mechanics; not product journeys or visual conventions |
| Define, accept, adopt, or change the UI constitution and view composition | [axis-ui-system/SKILL.md](./axis-ui-system/SKILL.md) | Owns semantic roles and values, page archetypes, shared visual mappings, golden references, active UI phase, and safe replacement |
| Change or audit guidance, repo skills, diagrams, navigation, routing, or documentation ownership | [axis-doc-hygiene/SKILL.md](./axis-doc-hygiene/SKILL.md) | Owns clarity and single-source hygiene; domain owners retain spec, status, and evidence decisions |
| Select or change repository bootstrap, commands, and checks | [axis-script-scope/SKILL.md](./axis-script-scope/SKILL.md) | Chooses environment profiles, the smallest proof, and wrapper boundaries |
| Decide readiness for independent review | [axis-review-readiness/SKILL.md](./axis-review-readiness/SKILL.md) | Audits immutable evidence; does not review, commit, or publish |
| Resolve review findings | [axis-review-feedback/SKILL.md](./axis-review-feedback/SKILL.md) | Classifies and fixes findings, then returns evidence |
| Publish or update a PR branch | [axis-pull-request/SKILL.md](./axis-pull-request/SKILL.md) | Owns commits required for publication, review loop, metadata, and GitHub actions |

Validate the system with `python scripts/axis.py check repo-skills`.
