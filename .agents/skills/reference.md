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
10. **Route durable guidance before edit.** The entry domain owner keeps spec, status, and evidence decisions. Other durable guidance **Requires** selecting `$axis-doc-hygiene` or entering it through a typed handoff before edit.

## Handoff types

| Type | Meaning |
|---|---|
| **Requires** | Complete the target first; reuse current evidence when still valid |
| **Delegates** | Caller remains orchestrator; target returns the requested result |
| **Returns to** | Resume the named caller with evidence and unresolved decisions |
| Plain link | Reference or optional navigation only |

Delegated skills do not auto-route back, commit, publish, or invoke another workflow unless their caller explicitly requested that action.

## Engineering method

1. **Understand before simplifying.** Read the governing contract and trace the real flow, callers, and dependencies before choosing a smaller design. A small change in the wrong owner is not minimal.
2. **Minimal solution ladder.** Stop at the first valid rung: no change; reuse existing code; use the standard library; use a native platform capability; use an installed dependency; then write the minimum custom code. Do not add speculative abstractions, dependencies, flags, or files.
3. **Root-cause loop.** For a failure, reproduce it, read exact diagnostics and recent changes, trace the source, state one hypothesis, and test one variable. Do not stack unproven fixes; after three failed fix attempts, stop and reassess the architecture with the user.
4. **Fail-before/pass-after.** For a bug or logic change, first prove the smallest reliable check fails for the intended reason, then make it pass. If no automated boundary exists, record why and use the smallest reproducible check.
5. **Safety floor.** Minimality never removes required acceptance behavior, trust-boundary validation, security, data-loss protection, error behavior, or accessibility.
6. **Communication clarity.** Lead with outcome and decisive evidence; preserve exact paths, commands, identifiers, and errors; remove filler and repeated summaries. Expand when compression could obscure sequence, risk, ambiguity, or an irreversible action.
7. **Skill proof.** Pair deterministic skill checks with a realistic forward test when a fresh agent is available; pass task artifacts, not the intended answer. Compare with the current Axis baseline, not a deliberately weak workflow, and measure task fidelity plus workflow cost rather than brevity alone. Otherwise report semantic compliance as review-only.

## Improvement loop

Apply at review boundaries and to validated feedback:

1. **Trigger from evidence.** Use a reproducer, review finding, gate escape, false positive, or stale rule—not a hypothetical.
2. **Classify by scope.** Decide whether the evidence proves a local defect, reusable decision/invariant, or obsolete rule. A first occurrence may still expose a systemic class.
3. **Promote by value.** Fix local defects in their owner; put reusable decisions in one owner; add a checker plus regression test when the invariant is deterministic; delete obsolete rules and checks. Do not generalize merely to memorialize an incident.
4. **Verify the class.** Apply the Engineering method's behavior proof at the lowest reliable boundary and keep incident details in the regression fixture, not guidance.
5. **Prune.** Replace duplicate prose with owner links and sweep retired identifiers.

Report `Improvement: local fix / owner updated / enforcement updated / rule retired / N/A` with evidence.

## Output envelope

Use only fields that carry information: status, decisions, evidence, gaps, and next owner. Domain skills may add unique fields; omit empty boilerplate.
