# Review findings

> **Navigation**: [<- docs/README.md](./README.md) . [<- AGENTS.md](../AGENTS.md)

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

P0/P1/P2 in [`AGENTS.md`](../AGENTS.md) describe severity and escalation, not
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
| [`AGENTS.md`](../AGENTS.md) | Severity, architecture stops, escalation rules | Command matrices, proof that a rule is enforced |
| [`agent-checklist.md`](./playbooks/agent-checklist.md) | Workflow, AC/path coverage, Verification gate command matrix | Reclassifying review-only checks as gates |
| This file | Enforcement status, known gaps, repeat finding classes, rule registry rows | Feature specs or implementation patterns |
| [`WORKAROUNDS.md`](./WORKAROUNDS.md) | Intentional P0/P1 violations and cleanup triggers | General lessons or resolved history |
| `scripts/axis.py`, tests, CI | Mechanical enforcement | New policy prose without docs ownership |

If a doc needs to mention another surface's rule, link to the owner instead of
restating the mechanism. Restatement is allowed only for a one-line summary that
does not change commands, status, or scope.

---

## Rule registry contract

The ledger below is the rule registry. Add or change rows there instead of
creating a second policy table in another doc. Each row must answer:

- **Rule owner:** the doc, test project, analyzer config, review owner, or
  `None` when the row is deliberately not a rule.
- **Trigger / scope:** the files, PR shape, or review situation that activates
  the row.
- **Mechanism:** the command, CI/build/tooling check, or review path that owns
  the finding class.
- **Proof / gap:** negative test, CI scope, analyzer/compiler wiring, known
  partial gap, or why the row stays human-owned.
- **Status:** one of the taxonomy labels above.

Rows marked **Enforced** need proof that the mechanism runs and fails for a bad
example. Rows marked **Partial** must name the known gap. Rows marked
**Review-only** must not use gate language.

---

## Enforcement truth audit

The registry is only useful if its enforcement claims match committed repo
wiring. Doc drift therefore checks the CI workflow, local pre-push hook,
`scripts/axis.py pre-push`, `scripts/axis.py verify`, analyzer settings, OpenAPI snapshot test, frontend
API-type generation command, and solution membership for architecture tests.

This audit covers committed files only. GitHub branch-protection settings live
outside the repository; when those settings change, update the repo docs and
workflow in the same maintenance pass.

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

| Finding class | Rule owner | Trigger / scope | Mechanism | Proof / gap | Status |
|---|---|---|---|---|---|
| FE/BE casing and wire-shape drift | [agent-checklist.md](./playbooks/agent-checklist.md) | API contract or `openapi.json` changes | `OpenApiDocumentTests` + frontend `gen:api-types` diff on `openapi.json` | CI .NET/frontend jobs run when API contract paths change | **Enforced** |
| .NET test name convention | [testing.md](./playbooks/testing.md) | New or changed .NET test methods | `python scripts/axis.py check test-naming` | `scripts/tests/test_policy_gates.py` negative test + CI policy tests | **Enforced** |
| Python policy gates still fire | This file | Every PR that runs Doc drift | `python scripts/axis.py check policy-tests` | `scripts/tests/test_policy_gates.py` negative tests + CI `Doc drift` job | **Enforced** |
| Governance owner-boundary drift | [Ownership boundaries](#ownership-boundaries) | `AGENTS.md`, `CONTRIBUTING.md`, and PR template wording | `python scripts/axis.py check doc-drift` rejects policy command restatement and Design Gate machine-gate wording | `scripts/tests/test_policy_gates.py` negative tests scoped to entry governance docs | **Enforced** |
| Review findings registry drift | [Rule registry contract](#rule-registry-contract) | `docs/REVIEW_FINDINGS.md` ledger rows | `python scripts/axis.py check doc-drift` validates owner, trigger, mechanism, proof/gap, and status fields | `scripts/tests/test_policy_gates.py` negative tests for missing owner/status and Partial rows without a known gap | **Enforced** |
| Enforcement claim wiring drift | [Enforcement truth audit](#enforcement-truth-audit) | Committed CI workflow, pre-push hook, analyzer config, OpenAPI/frontend type wiring, and solution membership | `python scripts/axis.py check doc-drift` validates the wiring that registry Enforced rows rely on | `scripts/tests/test_policy_gates.py` negative tests for missing CI doc-drift, pre-push quick-gate wiring, analyzer warnings-as-errors, and OpenAPI CI trigger coverage | **Enforced** |
| Repo toolchain version drift | [scripts.md](./playbooks/scripts.md#tool-versions) | Local and CI repo-maintenance commands using .NET SDK, Node.js, Buf, or Lychee | `scripts/axis.py` version checks plus CI setup pins | `scripts/tests/test_policy_gates.py` negative tests for wrong .NET/Node/Buf/Lychee versions and enforcement truth audit snippets for CI/local wiring | **Enforced** |
| Tracked text file encoding drift | [docs-style.md](./playbooks/docs-style.md) | Tracked text files | `python scripts/axis.py check text-encoding` | `scripts/tests/test_policy_gates.py` rejects BOM, CRLF, invalid UTF-8, and common mojibake markers | **Enforced** |
| Reference/playbook size budget drift | [docs-style.md](./playbooks/docs-style.md) | `AGENTS.md`, `docs/ARCHITECTURE.md`, and `docs/playbooks/*.md` | `python scripts/axis.py check doc-size-budgets` | `scripts/tests/test_policy_gates.py` rejects over-budget pattern routers and playbooks, and verifies current repo | **Enforced** |
| Use-case document contract drift | [agent-checklist.md](./playbooks/agent-checklist.md) | `docs/use-cases/**/README.md` use-case specs and domain indexes | `python scripts/axis.py check use-case-docs` parses use-case sections/tables and validates required sections, table schemas, Acceptance Test Matrix AC coverage/runner values, high-level matrix cells, wireframe/status table shape, Mermaid-only diagrams, self-links, and truncated domain rows | `scripts/tests/test_policy_gates.py` negative tests for matrix schema/coverage/enums, implementation-detail cells, wireframe/status table drift, and loader support for typed checker modules | **Enforced** |
| Ad hoc utility script drift | [scripts.md](./playbooks/scripts.md) | Repo-level and docs-level maintenance scripts, plus package-local native tooling placement | `python scripts/axis.py check scripts-standard` | `scripts/tests/test_policy_gates.py` rejects `.mjs` docs utility scripts and non-Python pre-push hook, accepts native `frontend/scripts` tooling, and verifies current repo | **Enforced** |
| Codex repo skill metadata drift | [scripts.md](./playbooks/scripts.md) | Repo-scoped skills under `.agents/skills/` | `python scripts/axis.py check codex-skills` | `scripts/tests/test_policy_gates.py` rejects template TODOs, frontmatter/name mismatches, overlong or vague skill bodies, broken doc references, missing required skill chaining, and default prompts that do not invoke the skill | **Enforced** |
| Frontend standard UI controls bypass shadcn/ui primitives | [frontend.md](./playbooks/frontend.md) | Frontend TSX outside `frontend/src/components/ui/` | `python scripts/axis.py check frontend-component-composition` rejects native `<button>`, `<input>`, `<label>`, `<select>`, and `<textarea>` outside the primitive layer | `scripts/tests/test_policy_gates.py` negative test for feature-local native controls | **Enforced** |
| Frontend component code bypasses design tokens for colors or shadows | [design-system.md](./playbooks/design-system.md) | Frontend TS/TSX component code | `python scripts/axis.py check frontend-style` rejects raw neutral color utilities, raw shadow utilities, and arbitrary color/gradient values | `scripts/tests/test_policy_gates.py` negative tests for raw neutral colors, raw shadows, and arbitrary gradients | **Enforced** |
| UI primitive added without catalog, visual, readiness, and test contract | [design-system.md](./playbooks/design-system.md) | `frontend/src/components/ui/*.tsx` | `python scripts/axis.py check frontend-component-composition` compares UI primitive files to `frontend/src/design-system/primitive-contracts.ts` and verifies catalog targets, readiness matrix fields, desktop Playwright screenshots, and test files | `scripts/tests/test_policy_gates.py` negative tests for missing primitive contracts, missing readiness coverage, missing catalog readiness target, and missing screenshot targets | **Enforced** |
| Route-bound frontend surface added without consumer contract | [design-system.md](./playbooks/design-system.md) | Frontend route files importing product UI components | `python scripts/axis.py check frontend-component-composition` compares route-bound product UI files to `frontend/src/design-system/consumer-contracts.ts` and verifies owner, readiness, primitive/state/evidence metadata, catalog matrix, desktop screenshot target, and test files | `scripts/tests/test_policy_gates.py` negative tests for missing registry, missing contract row, missing readiness matrix, missing screenshot target, missing metadata, unknown evidence, and missing test files | **Enforced** |
| `CancellationToken` not forwarded to a callee that accepts one | [AGENTS.md](../AGENTS.md) | Build of `src/` or `tests/` touching async calls | `CA2016` escalated to `warning`, build fails via `TreatWarningsAsErrors` | Analyzer/compiler wiring in `.editorconfig` + `Directory.Build.props` | **Enforced** |
| New skipped tests | [AGENTS.md](../AGENTS.md) | Added lines under `tests/` | Added-line ratchet rejects `Skip =` under `tests/` | `scripts/tests/test_policy_gates.py` negative test | **Enforced** |
| `EnsureCreated` reintroduced | [AGENTS.md](../AGENTS.md) | Added lines under `src/` or `tests/` | Added-line ratchet rejects `EnsureCreated` / `EnsureCreatedAsync` | `scripts/tests/test_policy_gates.py` negative test | **Enforced** |
| New or modified Application handler without matching test file | [agent-checklist.md](./playbooks/agent-checklist.md) | Changed `Commands/*Handler.cs` or `Queries/*Handler.cs` files | Diff ratchet checks matching handler test files | `scripts/tests/test_policy_gates.py` negative test; known gap: untouched legacy handlers are not swept | **Partial** |
| Endpoint returns `object`/anonymous JSON instead of an Application-layer DTO | [api-patterns.md](./playbooks/api-patterns.md) | Added endpoint return lines under `src/Axis.Api/Endpoints` | Added-line ratchet bans new `.Produces<object>` / `Results.Ok(new { ... })` | `scripts/tests/test_policy_gates.py` covers the ratchet class | **Enforced** |
| Minimal-API endpoint orchestrates more than one mediator call | [api-patterns.md](./playbooks/api-patterns.md) | Named endpoint handler methods | Full-state guard counts `.Send(`/`.Publish(` in named endpoint handlers | CI `Doc drift`; known gap: inline lambdas are not parsed | **Partial** |
| Cross-module in-process call / illegal project ref | [AGENTS.md](../AGENTS.md) | Project/type graph across modules | Architecture fitness tests + CodeRabbit path instruction | Tests catch project/type graph violations; known gap: runtime DI via `Contracts` still needs review | **Partial** |
| Cross-module raw SQL | [AGENTS.md](../AGENTS.md) | Data access that appears to cross module ownership | CodeRabbit path instruction + review | Requires table ownership and intent judgment | **Review-only** |
| Auth/permission check before input validation | [agent-checklist.md](./playbooks/agent-checklist.md) | Endpoint changes with auth and validation ordering | CodeRabbit path instruction + review | Requires endpoint intent and failure-order judgment | **Review-only** |
| Wrong status code on bad input | [api-patterns.md](./playbooks/api-patterns.md) | Endpoint error responses | CodeRabbit path instruction + review | Requires semantic assertion and error taxonomy judgment | **Review-only** |
| Side effects committed before the DB transaction they depend on | [domain-application-patterns.md](./playbooks/domain-application-patterns.md) | Handlers, repositories, jobs, or consumers with side effects | CodeRabbit path instruction + review | Requires data-flow and transaction-boundary judgment | **Review-only** |
| Test asserts something other than its name claims | [testing.md](./playbooks/testing.md) | Test changes with assertion/name mismatch risk | CodeRabbit path instruction; analyzer pilot only if it recurs | Green-but-wrong tests require assertion semantics | **Review-only** |
| `Result`/`Result<T>` vs bespoke bool/tuple/throw for business failures | [AGENTS.md](../AGENTS.md) | Business failure modeling in Application/Domain | CodeRabbit path instruction + review | Design judgment; exceptions are valid for infrastructure faults | **Review-only** |
| One public type per `.cs` file | None | C# file Workspace | None | Intentional groupings are common and valid | **Not a rule** |
| Inline fully-qualified type names instead of `using` directives | [.editorconfig](../.editorconfig) | Style cleanup | IDE suggestions / `dotnet format` cleanup | Low defect value; keep as style guidance unless it becomes recurring review noise | **Guidance** |

---

## Metric

Track repeat findings for already **Enforced** classes. A repeat means either
the gate has a hole, the CI trigger is wrong, or the docs overstated the rule.
Record that in Retrospective review so the fix is a stronger mechanism, not more
prose.
