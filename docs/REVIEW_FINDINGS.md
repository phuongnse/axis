# Review findings

> **Navigation**: [<- docs/README.md](./README.md) . [<- CLAUDE.md](../CLAUDE.md)

Recurring review finding classes and how each is prevented. A finding class
should be explained once; after that it is either mechanized, kept explicitly
review-only, downgraded to guidance, or deleted as noise.

Sibling register: [WORKAROUNDS.md](./WORKAROUNDS.md) tracks intentional rule
violations. This file tracks the rules we are trying to make hard to violate.

---

## Enforcement taxonomy

Use these labels consistently in docs, PR templates, and review comments:

| Status | Meaning | Allowed language |
|---|---|---|
| **Enforced** | CI/build/tooling fails the PR for this class. Custom repo gates have a counterexample test proving the rule fires. | "gate", "enforced", "must" |
| **Partial** | Tooling blocks a deterministic subset and the known gap is documented. | "partially enforced"; name the gap |
| **Review-only** | Human/CodeRabbit judgment is required; CI cannot prove it without false positives. | "review checkpoint", not "gate" |
| **Guidance** | Useful convention or example, but not a defect class. | "prefer", "pattern", "example" |
| **Not a rule** | Deliberately not enforced or reviewed. | Avoid rule language |

P0/P1/P2 in [`CLAUDE.md`](../CLAUDE.md) describe severity and escalation, not
automation. A P0 can still be **Review-only** if CI cannot decide it without
intent. Conversely, a low-severity convention can be **Enforced** if a cheap,
deterministic check exists. Use this file to decide wording before adding
"must", "gate", or "enforced" language elsewhere.

Custom gates are not trusted just because they pass on a clean repo. Before a
custom rule can be marked **Enforced**, add a negative test under
[`scripts/tests`](../scripts/tests/test_policy_gates.py) or the relevant test
project showing the bad example fails. Analyzer/compiler/tool rules can rely on
the tool, but the repo-specific wiring still needs to run in CI.

## Ownership boundaries

Avoid duplicating rule text across docs. Each surface has one job:

| Surface | Owns | Does not own |
|---|---|---|
| [`CLAUDE.md`](../CLAUDE.md) | Severity, architecture stops, escalation rules | Command matrices, proof that a rule is enforced |
| [`agent-checklist.md`](./playbooks/agent-checklist.md) | Workflow, AC/path coverage, Verification gate command matrix | Reclassifying review-only checks as gates |
| This file | Enforcement status, known gaps, repeat finding classes | Feature specs or implementation patterns |
| [`WORKAROUNDS.md`](./WORKAROUNDS.md) | Intentional P0/P1 violations and cleanup triggers | General lessons or resolved history |
| `scripts/axis.py`, tests, CI | Mechanical enforcement | New policy prose without docs ownership |

If a doc needs to mention another surface's rule, link to the owner instead of
restating the mechanism. Restatement is allowed only for a one-line summary that
does not change commands, status, or scope.

---

## When to add a row

Wired into **Retrospective review** ([agent-checklist.md](./playbooks/agent-checklist.md)):
when a review finding repeats a class already seen, record it here. Pick a
status honestly; do not upgrade review-only guidance into a fake gate.

Before building a gate, the class must pass all three tests. If any answer is
"no", leave it review-only or guidance.

1. **Deterministic?** Can a rule decide it without understanding intent?
2. **Recurred?** Seen at least 2-3 times across PRs?
3. **Cheaper than re-review?** Gate build and maintenance cost less than
   catching it by review?

Mechanism tiers, cheapest first: analyzer severity in [`.editorconfig`](../.editorconfig)
-> architecture fitness test ([tests/Architecture](../tests/Architecture/Axis.Architecture.Tests/README.md))
-> codegen/source generator -> CI guard -> review-only.

---

## Ledger

| Finding class | Mechanism | Proof / scope | Status |
|---|---|---|---|
| FE/BE casing and wire-shape drift | `OpenApiDocumentTests` + frontend `gen:api-types` diff on `openapi.json` | CI .NET/frontend jobs run when `openapi.json` or relevant source changes | **Enforced** |
| .NET test name convention | `python scripts/axis.py check test-naming` | `scripts/tests/test_policy_gates.py` negative test + CI policy tests | **Enforced** |
| Python policy gates still fire | `python scripts/axis.py check policy-tests` | CI `Doc drift` job runs on every PR | **Enforced** |
| Governance owner-boundary drift | `python scripts/axis.py check doc-drift` rejects policy command restatement in entry docs and Design Gate machine-gate wording | `scripts/tests/test_policy_gates.py` negative tests; scoped to `CLAUDE.md`, `CONTRIBUTING.md`, and the PR template | **Enforced** |
| Tracked text file encoding drift | `python scripts/axis.py check text-encoding` | `scripts/tests/test_policy_gates.py` rejects BOM, CRLF, invalid UTF-8, and common mojibake markers | **Enforced** |
| `CancellationToken` not forwarded to a callee that accepts one | `CA2016` escalated to `warning`, build fails via `TreatWarningsAsErrors` | Analyzer/compiler wiring in `.editorconfig` + `Directory.Build.props` | **Enforced** |
| New skipped tests | Added-line ratchet rejects `Skip =` under `tests/` | `scripts/tests/test_policy_gates.py` negative test | **Enforced** |
| `EnsureCreated` reintroduced | Added-line ratchet rejects `EnsureCreated` / `EnsureCreatedAsync` under `src/` and `tests/` | `scripts/tests/test_policy_gates.py` negative test | **Enforced** |
| New or modified Application handler without matching test file | Diff ratchet checks changed `Commands/*Handler.cs` and `Queries/*Handler.cs` | `scripts/tests/test_policy_gates.py` negative test; untouched legacy files are not swept | **Partial** |
| Endpoint returns `object`/anonymous JSON instead of an Application-layer DTO | Added-line ratchet bans new `.Produces<object>` / `Results.Ok(new { ... })` in `Axis.Api/Endpoints` | `scripts/tests/test_policy_gates.py` covers the ratchet class | **Enforced** |
| Minimal-API endpoint orchestrates more than one mediator call | Full-state guard counts `.Send(`/`.Publish(` in named endpoint handlers | CI `Doc drift`; known gap: inline lambdas are not parsed | **Partial** |
| Cross-module in-process call / illegal project ref | Architecture fitness tests + CodeRabbit path instruction | Tests catch project/type graph violations; runtime DI via `Contracts` still needs review | **Partial** |
| Cross-module raw SQL | CodeRabbit path instruction + review | Path-only grep cannot know table ownership safely | **Review-only** |
| Auth/permission check before input validation | CodeRabbit path instruction | Requires endpoint intent and failure-order judgment | **Review-only** |
| Wrong status code on bad input | CodeRabbit path instruction | Requires semantic assertion and error taxonomy judgment | **Review-only** |
| Side effects committed before the DB transaction they depend on | CodeRabbit path instruction | Requires data-flow and transaction-boundary judgment | **Review-only** |
| Test asserts something other than its name claims | CodeRabbit path instruction; analyzer pilot only if it recurs | Green-but-wrong tests require assertion semantics | **Review-only** |
| `Result`/`Result<T>` vs bespoke bool/tuple/throw for business failures | CodeRabbit path instruction | Design judgment; exceptions are valid for infrastructure faults | **Review-only** |
| One public type per `.cs` file | None | Intentional groupings are common and valid | **Not a rule** |
| Inline fully-qualified type names instead of `using` directives | IDE suggestions / `dotnet format` cleanup | Low defect value; keep as style guidance unless it becomes recurring review noise | **Guidance** |

---

## Metric

Track repeat findings for already **Enforced** classes. A repeat means either
the gate has a hole, the CI trigger is wrong, or the docs overstated the rule.
Record that in Retrospective review so the fix is a stronger mechanism, not more
prose.
