# Design Gate: Structural Agent Workflows and Policy Guards

> **Navigation**: [docs/playbooks/design-gate.md](./design-gate.md) · [scripts.md](./scripts.md) · [reference.md](../../.agents/skills/reference.md) · [docs/README.md](../README.md)

## Timing, risk, and scope

This is a corrective full Design Gate completed during independent review of checkpoint `aabd1155`. The task was routed and classified before implementation in the working session, but its durable dossier was omitted. This record closes that process gap without representing the dossier as pre-code evidence.

The change is standard-tier with a broad blast radius: it changes deterministic repository checks, agent routing, review workflow behavior, and documentation across 37 paths. It does not change product behavior, REST/OpenAPI contracts, database schema, authentication behavior, or the approved technology stack.

## Governing rules

- Workflow gates, truthful evidence, and no-substitution blockers follow [AGENTS.md § Critical Rules](../../AGENTS.md#critical-rules) and [AGENTS.md § Operating Rules](../../AGENTS.md#operating-rules).
- Skill ownership, routing, blocker handling, and model profiles follow [reference.md](../../.agents/skills/reference.md).
- Deterministic command ownership and guard design follow [axis-script-scope](../../.agents/skills/axis-script-scope/SKILL.md) and [scripts.md](./scripts.md).
- Durable guidance and retirement hygiene follow [axis-doc-hygiene](../../.agents/skills/axis-doc-hygiene/SKILL.md).
- Checkpoint readiness, independent review, feedback, and publication follow [workflows.toml](../../.agents/skills/workflows.toml).

## Blast radius

The inventory was derived with:

```text
git diff-tree --no-commit-id --name-only -r aabd1155
rg -n "Ready review|axis-ready-review|ready-review" .agents docs scripts .codex AGENTS.md .github
```

It covers `.agents/skills/**`, `.codex/**`, policy scripts and tests under `scripts/**`, PR/Renovate metadata, `AGENTS.md`, and their owner docs. Generated product contracts and runtime application code are outside the slice.

## Decisions

- Deterministic guards may parse schemas, configuration, graphs, state machines, command syntax, source symbols, or executable behavior. They must not infer semantic compliance from prose keywords, fragments, wording, or minimum text length.
- Semantic routing and blocker behavior are proved by fresh-agent forward tests and independent review, not by scanning agent instructions for expected phrases.
- Review readiness and independent review are separate states. Review findings and approval are distinct graph edges; publication requires approval.
- Project orchestration has named roles and no default delegate. Model, reasoning, minimality, and compression profiles live once in the manifest; `ultra` is not applied mechanically.
- `Partial` and `Not started` use-case states require typed, unique GAP-ID rows. The gap description remains owner-reviewed prose.
- PR requirements use explicit statuses with checkbox consistency. Automation may leave requirements pending; it may not pre-claim future review or verification.

## Alternatives rejected

- Aggregated keyword, phrase, or retired-name deny-lists: they encode current wording rather than the invariant and become brittle as the repository evolves.
- Duplicated profile prose in every named agent: it permits drift from the manifest.
- A generic default agent or automatic `ultra`: neither expresses task ownership or proportional cost/value.
- Compatibility shims for the renamed readiness skill: the project-local skill name is an internal pre-release workflow surface and is cleanly replaced.

## Retirement and compatibility

Clean cutover retires the `axis-ready-review` skill alias and the ambiguous `Ready review` label. The CLI command `python scripts/axis.py ready-review` remains because it names the checkpoint operation, not the review verdict. No fallback skill, dual routing path, or permanent retired-name checker is retained.

The post-edit inventory uses the `rg` command above and structural catalog/link validation. Historical names may remain in Git history; current owner docs and configuration must resolve through the new skill.

## Contract decision

`N/A because no product wire shape, schema, casing, or generated frontend/backend contract changes.` The new TOML files are repository workflow configuration validated by their owning checker.

## Verification and close-loop evidence

- Focused policy classes and full policy suite passed at checkpoint `aabd1155`; repository skills, documentation checks, Markdown links, dependency audits, and Renovate schema validation passed.
- Fresh `axis_investigator` and `axis_planner` scenarios proved blocker stopping, profile application, readiness/reviewer separation, outcome routing, and evidence reuse.
- `python scripts/axis.py ready-review` passed on the clean checkpoint, including build, format, 49 API tests, 166 architecture tests, 179 frontend tests, and 383 policy tests.
- Independent review found this missing dossier, dishonest generated checklist states, unstructured partial gaps, and two enforcement-ledger overclaims. The follow-up delta adds this honest record, typed statuses/GAP IDs, regression tests, and evidence-aligned ledger entries before a fresh delta readiness and review.

## Sign-off boundary

The user explicitly approved the repository-wide structural cleanup and role-profile decisions. No high-risk product, contract, schema, authentication, or stack decision was introduced by this slice, so no additional high-risk sign-off applies.
