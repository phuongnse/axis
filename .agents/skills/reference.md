# Axis skill workflow contract

Shared rules for every `.agents/skills/*/SKILL.md` workflow. Skill-specific gates sit in each skill's `## Hard gates` section.

## Hard gates (universal)

1. **Read before edit.** Read the full matching `SKILL.md` (and every chained skill) before changing source, tests, scripts, migrations, contracts, frontend behavior, or docs status.
2. **Sequential steps.** Numbered workflow steps are ordered gates. Step *N+1* is forbidden until step *N* is complete and its output evidence is recorded.
3. **STOP means stop.** When a step says stop, do not push, open or update a PR, mark ready, edit implementation files, or claim ready/done. Report the blocker instead.
4. **No silent deferral.** Do not substitute "follow-up", "later", "non-blocking", or PR-body notes for completing a gate. Defer only a **specific item** with **explicit user approval** for that item.
5. **Skip is explicit.** Skip a step only when the user explicitly requested skipping **that step**. Record the skip reason in the skill output.
6. **Chains are mandatory.** `$axis-*` aliases and `.agents/skills/<name>/SKILL.md` paths name required skills, not optional reading.
7. **Honest output.** Fill the skill output template. A missing section means the step did not run — do not invent pass status.
8. **Invalidate and rerun.** After a follow-up edit invalidates evidence, rerun only the checks the skill names for that boundary; do not skip because an earlier run passed.
9. **Current contracts only.** When adding guidance or deterministic checks, state the supported shape of the system today. Generalize lessons into reusable decision criteria; do not document replaced artifacts or incident-specific remedies unless an owner doc explicitly approves compatibility.

## Stop vs defer vs skip

| Outcome | When | Allowed next action |
|---|---|---|
| **Stop** | Required input missing, verification failed, sign-off missing, tool blocked | Fix blocker or ask user; do not proceed |
| **Defer** | One specific item cannot close now | User approves deferral; record exact owner and AC/item |
| **Skip** | User explicitly waived a step | Record reason; later steps that depend on it still stop unless user waived those too |

## Intent routing

- Route by the user's intended next workflow action, not exact words.
- Treat examples and trigger phrases as cues, not exhaustive command strings.
- Name concrete signal classes and explicit overrides when a skill has multiple directions, phases, or publication boundaries.
- Ask one question only when two workflow directions remain plausible or conflicting after local inspection.

## Publication gate (PR boundary)

Applies to `$axis-pull-request` and [docs/playbooks/scripts.md § Pre-PR review checkpoint](../../docs/playbooks/scripts.md#pre-pr-review-checkpoint). A push-only update to a branch that has an upstream, an open PR, or explicit PR intent is a PR branch/diff update.

The pre-PR review checkpoint is triggered by non-trivial implementation, behavior, contract, or high-risk changes. Docs-only, metadata-only, and small guidance/tooling-text changes record `not triggered` with the reason; when unsure, run the checkpoint.

```text
$axis-ready-review -> Ready
  -> IF pre-PR review checkpoint not triggered:
       -> record not triggered with reason
  -> IF pre-PR review checkpoint triggered:
       -> checkpoint commit on publish branch
       -> pre-PR review (CodeRabbit CLI)
       -> IF findings:
            -> $axis-review-feedback (fix valid items, commit)
            -> ready-review --since <checkpoint> when triggered
            -> rerun review scoped to checkpoint
            -> repeat until clean OR user-approved defer/false-positive per item
  -> ONLY THEN: push branch / gh pr create / mark ready
```

**Forbidden before the publication gate closes:**

- Push new commits to a branch that has an open PR or is being published for one
- `gh pr create`, mark ready, or PR body claiming review is "follow-up"
- PR body language such as "follow-up as needed", "non-blocking findings", or "address later" while valid findings remain open

Metadata-only PR title/body edits may skip the checkpoint per `$axis-pull-request`.

## Chaining map

| From | Must read before proceeding |
|---|---|
| Non-trivial implementation | `$axis-design-gate` |
| UI customization, shadcn sync, or component-provider change | `$axis-design-gate` then `$axis-ui-system` |
| New module, module boundary, DDD/CQRS foundation, or event-sourcing change | `$axis-design-gate` then `$axis-module-architecture` |
| Tactical DDD/CQRS pattern implementation | `$axis-module-architecture` when module/foundation scope changed, then `$axis-module-patterns` |
| Spec gap | `$axis-use-case-spec` |
| Use-case slice | `$axis-use-case-implementation` (+ design gate when non-trivial) |
| Before review | `$axis-ready-review` |
| PR create / update branch / mark ready | `$axis-pull-request` (requires ready-review evidence) |
| Push/update a published or PR branch | `$axis-pull-request` (branch/diff update) |
| Pre-PR review findings | `$axis-review-feedback` before push/PR |
| Review feedback close-out | `$axis-ready-review` before another review pass |
