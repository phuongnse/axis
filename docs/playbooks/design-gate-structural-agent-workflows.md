# Design Gate: Structural Agent Workflows and Policy Guards

> **Navigation**: [docs/playbooks/design-gate.md](./design-gate.md) · [scripts.md](./scripts.md) · [reference.md](../../.agents/skills/reference.md) · [docs/README.md](../README.md)

## Risk and scope

This full Design Gate is standard-tier with a broad blast radius: it changes deterministic repository checks, agent routing, review workflow behavior, and documentation. It does not change product behavior, REST/OpenAPI contracts, database schema, authentication behavior, or the approved technology stack.

## Governing rules

- Workflow gates, truthful evidence, and no-substitution blockers follow [AGENTS.md § Critical Rules](../../AGENTS.md#critical-rules) and [AGENTS.md § Operating Rules](../../AGENTS.md#operating-rules).
- Skill ownership, routing, blocker handling, and model profiles follow [reference.md](../../.agents/skills/reference.md).
- Deterministic command ownership and guard design follow [axis-script-scope](../../.agents/skills/axis-script-scope/SKILL.md) and [scripts.md](./scripts.md).
- Durable guidance and retirement hygiene follow [axis-doc-hygiene](../../.agents/skills/axis-doc-hygiene/SKILL.md).
- Checkpoint readiness, independent review, feedback, and publication follow [workflows.toml](../../.agents/skills/workflows.toml).

## Blast radius

The inventory covers:

```text
.agents/skills/**
.codex/**
scripts/**
.github/PULL_REQUEST_TEMPLATE.md
.github/renovate.json5
AGENTS.md
docs/ENFORCEMENT.md
docs/foundations/**
docs/playbooks/**
docs/use-cases/**
```

Generated product contracts and runtime application code are outside the slice. Retirement sweeps use exact identifiers only as one-time migration evidence; no retired-name deny-list remains in steady-state policy.

## Decisions

- Deterministic guards may parse schemas, configuration, graphs, state machines, command syntax, source symbols, or executable behavior. They must not infer semantic compliance from prose keywords, fragments, wording, or minimum text length.
- Semantic routing and blocker behavior are proved by fresh-agent forward tests and independent review, not by scanning agent instructions for expected phrases.
- Review readiness and independent review are separate states. Review findings and approval are distinct graph edges; publication requires approval.
- Each repository workflow purpose has one public Axis command, one profile/state term, and one evidence vocabulary. Internal checker modules do not retain no-op mode flags.
- Executable Axis command examples are validated by the CLI parser that owns those commands; the repository does not maintain a second command-name catalog.
- Project orchestration has named roles and no default delegate. Model, reasoning, minimality, and compression profiles live once in the manifest; `ultra` is not applied mechanically.
- Routing is evaluated per independently ownable work unit and re-evaluated when decisions change ambiguity, scope, ownership, or verification. Bounded implementation may belong to source, tests, tooling, or guidance; retaining an eligible unit on the primary requires a concrete boundary or lower total execution cost.
- `Partial` and `Not started` use-case states require typed, unique GAP-ID rows. The gap description remains owner-reviewed prose.
- PR requirements use explicit statuses with checkbox consistency. Supported automation branches may leave requirements pending; human publication branches must resolve every requirement and may not pre-claim future review or verification.

## Alternatives rejected

- Aggregated keyword, phrase, or retired-name deny-lists: they encode current wording rather than the invariant and become brittle as the repository evolves.
- Duplicated profile prose in every named agent: it permits drift from the manifest.
- A generic default agent or automatic `ultra`: neither expresses task ownership or proportional cost/value.
- Compatibility shims for renamed commands, profiles, readiness states, and evidence routes: these are internal pre-release workflow surfaces and are cleanly replaced.

## Retirement and compatibility

Clean cutover uses one `review-readiness` vocabulary across the skill, CLI command, workflow evidence, CI, hooks, tests, and guidance. The exact duplicate `all` setup/doctor profile is replaced by `review`; frontend component and browser evidence use `frontend test` and `local-dev e2e`; `local-dev smoke` owns only its fixed journey; UI baseline checking and generation use `check ui-baseline` and `generate ui-baseline`; internal documentation checkers expose one validation mode without a no-op `--check` flag. No fallback alias, dual routing path, or permanent retired-name checker is retained.

The post-edit inventory uses an exact-identifier `rg` migration sweep plus structural catalog and link validation. Historical names may remain in Git history; current owner docs and configuration must resolve through the new skill.

## Contract decision

`N/A because no product wire shape, schema, casing, or generated frontend/backend contract changes.` The new TOML files are repository workflow configuration validated by their owning checker.

## Verification plan

- Run focused policy tests while changing each parser or workflow graph, then the complete policy suite.
- Run `python scripts/axis.py check repo-skills`, use-case docs, foundation docs, documentation drift, and Markdown links.
- Validate the real Renovate configuration and the exact PR metadata through their Axis commands.
- Forward-test named-agent routing, blocker stopping, profile application, outcome routing, and evidence reuse with fresh agents.
- Create an immutable checkpoint, run `$axis-review-readiness`, then obtain the configured independent review before publication.

## Sign-off boundary

`N/A because this standard-tier workflow change introduces no high-risk product, contract, schema, authentication, or stack decision.`
