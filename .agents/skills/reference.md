# Axis Skill Workflow Contract

Universal semantics for every repo skill. Intent routing lives in [README.md](./README.md); domain decisions stay in the selected owner skill or owner document.

## Universal gates

1. **Read before edit.** Read the full entry skill and every resource marked **Requires** before changing its surface.
2. **Follow order.** Numbered steps are sequential unless the skill explicitly marks them independent.
3. **Stop means stop.** Do not edit, publish, or claim completion until the named condition is resolved.
4. **No silent deferral.** Defer one specific item only with explicit user approval and an owner.
5. **Skip is explicit.** Record the exact user-approved skip and do not assume dependent steps are waived.
6. **Use typed handoffs.** Plain skill links are navigation, not automatic chains.
7. **Reuse evidence.** A current satisfied prerequisite is idempotent; do not recurse or rerun it.
8. **Report honestly.** Missing or stale evidence is `not run` or `blocked`, never pass.
9. **Keep current contracts only.** Remove superseded guidance instead of documenting incident history or retired names.

## Handoff types

| Type | Meaning |
|---|---|
| **Requires** | Complete the target first; reuse current evidence when still valid |
| **Delegates** | Caller remains orchestrator; target returns the requested result |
| **Returns to** | Resume the named caller with evidence and unresolved decisions |
| Plain link | Reference or optional navigation only |

Delegated skills do not auto-route back, commit, publish, or invoke another workflow unless their caller explicitly requested that action.

## Improvement loop

Apply at review boundaries and to validated feedback:

1. **Trigger from evidence.** Use a reproducer, review finding, gate escape, false positive, or stale rule—not a hypothetical.
2. **Classify by scope.** Decide whether the evidence proves a local defect, reusable decision/invariant, or obsolete rule. A first occurrence may still expose a systemic class.
3. **Promote by value.** Fix local defects in their owner; put reusable decisions in one owner; add a checker plus regression test when the invariant is deterministic; delete obsolete rules and checks. Do not generalize merely to memorialize an incident.
4. **Verify the class.** Prove fail-before/pass-after at the lowest reliable boundary and keep incident details in the regression fixture, not guidance.
5. **Prune.** Replace duplicate prose with owner links and sweep retired identifiers.

Report `Improvement: local fix / owner updated / enforcement updated / rule retired / N/A` with evidence.

## Output envelope

Use only fields that carry information: status, decisions, evidence, gaps, and next owner. Domain skills may add unique fields; omit empty boilerplate.
