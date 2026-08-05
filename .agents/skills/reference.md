# Axis Skill Workflow Contract

Universal semantics for every repo skill. Intent routing lives in [README.md](./README.md), cross-skill topology lives in [workflows.toml](./workflows.toml), and domain decisions stay in the selected owner skill or owner document. Deterministic checks validate structure and graph integrity; realistic forward tests validate prose behavior.

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
11. **Compatibility is explicit.** Derive compatibility from the owning contract and real consumers/data. A clean cutover approved because no supported production consumer/data exists removes the old path completely; it does not preserve shims, dual contracts, feature flags, fallback parsing, negative assertions, deny-lists, duplicate tests, or routine guidance that keeps retired identifiers or concepts alive. This lifecycle fact never lowers the production quality of the replacement.
12. **Classify workarounds by contract.** Before taking an alternate path after a failure, name the owning contract, root cause, required owner, execution/trust boundary, invariants, and evidence boundary. If the proposal changes any of them merely to continue instead of repairing the root cause, it is a workaround: stop and re-enter `$axis-design-gate`. Only an explicit owning-contract change plus its own complete evidence can establish a new valid path.

## Change-driven scope

Before building, starting, recreating, testing, verifying, or reviewing, map touched paths, known affected consumers, and evidence-required dependencies to exact work units. Run each selected work unit once per valid checkpoint, reusing its evidence until a later edit or concrete finding invalidates it; leave untouched and unaffected units unrun. Use broad or full scope only when cross-cutting invalidation, inseparable dependency evidence, or an explicit immutable-review or CI contract requires it, and record that reason. This is semantic agent judgment, not brittle identifier lists or automatic diff inference.

## Agent routing

Select the Axis workflow before selecting an agent. Skill ownership, Design Gate evidence, typed handoffs, acceptance criteria, and verification remain unchanged after delegation. Project-local agent configuration owns runtime-specific role and model selection; this contract owns only portable work-shape semantics, and user-level tooling preferences are never a project dependency.

| Work shape | Semantic role | Boundary |
|---|---|---|
| Primary orchestration, ambiguous or high-risk decisions, integration, routine checks, final verification | Primary orchestrator | Keep primary-owned, user-controlled, shared-state, or handoff-dominated work on the primary |
| Narrow lookup, inventory, extraction, or deterministic summarization | Read-only scout | No edits or owner decisions; return exact path and symbol evidence |
| Broad exploration or failure triage that materially benefits from isolated context | Read-only investigator | No edits; separate evidence from hypotheses and stop at owner decisions |
| Substantial bounded planning or decision preparation that benefits from isolated context | Planning delegate | No edits or owner decisions; return options, risks, and the smallest executable sequence |
| Bounded implementation with exact ACs, disjoint ownership, and known verification | Implementation worker | One writer; return on ambiguity, scope growth, or owner decision |
| Scoped working-tree or immutable checkpoint review, security, or high-risk architecture | Independent read-only reviewer | Working-tree review cannot issue Ready; follow the delegated review lifecycle |

Before non-trivial execution, derive the smallest independently ownable work units from the current scope, then record a routing checkpoint with `Owner`, `Work unit`, `Work shape`, `Execution owner` (`primary` or one named semantic role), and `Rationale`. Group units only when they share the same owner and boundary. This records a decision and does not require delegation. Size and product-domain labels do not decide routing: source, tests, tooling, and guidance are equally eligible when their work shape fits. Route a bounded implementation unit with exact acceptance, disjoint ownership, and known verification to the implementation worker when total delegation, handoff, verification, and integration cost is lower than primary execution. Keep it on the primary only for a named primary-owned, user-controlled, shared-state, integration, or net-cost boundary; the rationale must identify that concrete boundary rather than restating that the task is small or sequential. Never split work artificially when the handoff would cost more than execution.

Routing is a lifecycle decision, not a one-time task classification. Re-evaluate an unexecuted unit when an owner decision resolves its ambiguity, its scope or ownership changes, or its verification becomes known. A unit that has become bounded and disjoint is newly eligible for delegation even when the parent task began ambiguous; either route it then or record the concrete boundary that still keeps it primary-owned. If the work shape cannot yet be named, keep it on the primary only until the next re-evaluation trigger. Every delegated prompt names objective, scope, base or checkpoint, permissions, stop conditions, and final output contract. The primary integrates results and retains completion ownership.

Delegation also assigns verification ownership. The primary owns routine verification and final evidence unless the prompt explicitly transfers one focused check. Run each check once per still-valid checkpoint, reuse its result across agents, and rerun only when later edits or a concrete finding invalidate it. Reviewers inspect the diff and existing evidence; they run only the smallest reproducer for a finding or evidence gap, never an already-passing routine suite.

## Blocker and completion protocol

Use this protocol whenever progress depends on state outside the repository or on a decision only the user can make. It is a process boundary, not a prompt to invent a workaround.

1. **Classify the boundary.** Decide whether the evidence shows a repository defect, an external-state blocker, or a missing product decision. External-state blockers include sign-in/consent, an app-managed client reload or restart, a host dependency, sudo/permissions, certificate or trust-store state, approval, and destructive-action confirmation.
2. **Reproduce safely.** Run the smallest permitted check, capture the exact command, exit status, error/output, and the boundary where it occurred. Read-only diagnosis may continue; state-changing work stops when it reaches user-controlled state.
3. **No workaround: stop and ask.** A path is a workaround when it changes the required tool, runtime, client, authority, trust boundary, or evidence boundary instead of repairing the failed path. A missing or unusable required command, executable, library, runtime, SDK, package, host capability, or supported client is a blocker; do not substitute another command, library API, runtime, version, environment, container, user-local copy, PATH change, symlink, proxy/harness, raw REST call, or disabled security control as evidence for that requirement. An approved workaround remains a workaround: approval authorizes only the exact named diagnostic action and purpose; it never satisfies the failed prerequisite, converts substitute output into evidence for the required boundary, or unblocks completion. Only current evidence from the required boundary, or an explicit change to its owning contract followed by its required verification, can close the blocker. Report `Blocker`, `Evidence`, `Boundary`, `User action or decision needed`, and `Safe next step after confirmation`; never silently install host packages, kill or replace app-managed processes, bypass authentication or TLS/sandbox controls, inject credentials or tokens, or mutate the database directly.
4. **Separate evidence boundaries.** Unit/contract tests prove code and protocol behavior. A purpose-built protocol harness may diagnose or prove that protocol boundary, but it does not prove that the currently registered agent client has the current tool registry or usable authenticated session. Do not use stale registry state, indirect API calls, or a separate harness as a substitute for a required live-agent boundary.
5. **Audit completion.** Build an acceptance-to-evidence matrix. Current source, focused tests, and any required runtime/read-back evidence must all be present; stale, missing, indirect, or blocked evidence remains `not run` or `blocked`. Only then may the owner mark the work complete.

## Delegated review lifecycle

Delegated review is asynchronous. A reviewer reported as `running` or `pending`, and a bounded wait that returns no result, means **review pending**, not review failure or a readiness verdict. Keep the reviewer alive, continue bounded waits, and close the review only after a final completed result or an explicit runtime failure.

Review read-only means no intentional edits to tracked source, tests, contracts, migrations, documentation, Git state, or PR state. A reviewer reuses current parent evidence and may run only the smallest focused check needed to reproduce a finding or fill a missing or invalidated evidence gap; normal ignored outputs and temporary files are allowed. If a command would modify a tracked artifact, the reviewer must stop and report that boundary instead of silently changing it.

Use this compact handoff when asking the user:

```text
Blocker: <what cannot proceed>
Evidence: <exact command/error or observed state>
Boundary: <host, client, account, permission, approval, or destructive action>
User action or decision needed: <one concrete request>
Safe next step after confirmation: <what will be run or changed>
```

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
3. **Root-cause loop.** For a failure, reproduce it, read exact diagnostics and recent changes, trace the source, state one hypothesis, and test one variable. Before changing approach, apply the workaround classification in the universal gates; a proposal that changes the required contract boundary returns to the Design Gate. Do not stack unproven fixes; after three failed fix attempts, stop and reassess the architecture with the user.
4. **Fail-before/pass-after.** For a bug or logic change, first prove the smallest reliable check fails for the intended reason, then make it pass. If no automated boundary exists, record why and use the smallest reproducible check.
5. **Safety floor.** Minimality never removes required acceptance behavior, trust-boundary validation, security, data-loss protection, error behavior, or accessibility.
6. **Clean replacement.** When compatibility is not required, update spec, implementation, callers, generated artifacts, tests, and guidance as one cutover, then prove the retired identifiers are gone. Keeping both paths is additional product behavior, not a safety default.
7. **Communication clarity.** Lead with outcome and decisive evidence; preserve exact paths, commands, identifiers, and errors; remove filler and repeated summaries. Expand when compression could obscure sequence, risk, ambiguity, or an irreversible action.
8. **Skill proof.** Pair deterministic skill checks with a realistic forward test when a fresh agent is available; pass task artifacts, not the intended answer. Compare with the current Axis baseline, not a deliberately weak workflow, and measure task fidelity plus workflow cost rather than brevity alone. Otherwise report semantic compliance as review-only.
9. **Local convention first.** Before introducing a naming, syntax, import, or ambiguity workaround, inspect the same module and preserve its domain vocabulary. Resolve C# type-name ambiguity through `using` or a clear alias; do not rename the domain concept or inline a project namespace inside implementation code.

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
