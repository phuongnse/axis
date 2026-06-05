# PR slicing — isolated, mergeable increments

> **Navigation**: [← docs/README.md](../README.md) · [← agent-checklist.md](./agent-checklist.md) · [← process.md](./process.md)

Use this when a use case is too large for one PR. **Each PR must merge into `main` on its own** — CI green, no dependency on another open PR, and the shipped path must still work for users.

> Splitting work into branches is **not** the same as isolating it. A "sliced" PR is still coupled if it does not compile alone, claims green CI that was never run, re-adds a file another slice owns, or hardcodes a shared value a sibling defines differently. The rules below exist to close exactly those gaps.

---

## What "isolated" actually means (two-sided test)

A slice is isolated only if **both** sides hold. Verify both before marking the PR ready — a branch cut from a stale `main` can pass neither and still look fine in the diff.

| Side | Definition | How to verify |
|------|------------|---------------|
| **A — Stands alone** | A fresh checkout of the branch compiles, the local Verification gate is green, CI is expected to run the full suite, and every route / endpoint / contract / symbol the slice *references* already exists on its merge target. | Switch to the branch on a clean tree → run `python scripts/axis.py verify`; rely on CI/branch protection for full `dotnet test Axis.sln`. |
| **B — Integrates** | After **rebasing onto current `main`**, it still compiles and is green, and merging it requires **no unmerged sibling**. | Rebase onto `origin/main`, re-run `python scripts/axis.py verify`; CI must rerun on the rebased branch before merge. |

If a slice references something an **unmerged sibling owns** (a route it navigates to, an endpoint it calls, a symbol it imports, a constant it reads), it fails side A — it is stacked in disguise, not isolated. Either pull that dependency into this slice, or ship a fallback that compiles and degrades gracefully on the current `main`.

---

## Rules (P0 for agents)

| Rule | Meaning |
|------|---------|
| **Branch from `main`** | Each slice starts from `origin/main`. Never stack slice B on slice A's branch. |
| **Prove both sides before "ready"** | Run the local Verification gate on the branch **and** after a trial rebase onto `origin/main`; CI/branch protection owns the full suite. |
| **Vertical when a contract changes** | If an endpoint adds a **required** field or changes a response/contract already used on `main`, ship backend + minimal caller in the **same** PR so `main` never breaks. |
| **Horizontal when additive** | New UI on existing APIs can be frontend-only; a new API with no caller on `main` yet can be backend-only. |
| **One owner per shared seam** | A file or symbol touched by more than one slice has exactly **one owning slice**; the others consume it, never re-add it. See below. |
| **No duplicate new files across siblings** | Adding the same new path in two open PRs is a slicing defect (add/add conflict on merge). Pick one owner. |
| **One source for shared values/contracts** | A value or contract used by more than one slice is defined once and imported; never hardcode a second copy. |
| **No "enable in a follow-up"** | Do not merge backend that breaks the current caller, or frontend that calls something absent on `main`. |
| **Honest status callouts** | Update `> **Implementation status**` per slice; name intentionally-skipped bullets under `**Deferred (PR #N follow-up):**`. |
| **Merge order documented + enforced** | State the order in the PR body and rebase remaining slices after **each** merge. |

**Anti-pattern:** `feature-full` branched from `feature-part1` — merging "full" without "part1" breaks the tree; "full" is not isolated.

---

## Shared seams

Most cross-slice damage happens at a few **shared seams** — files or symbols several slices naturally want to touch. Before opening the series, list the seams and assign **one owner** each; everyone else consumes the owner's version. If a later slice needs a not-yet-merged seam, it owns a compiling fallback (two-sided test, side A).

Common seam categories:

| Category | Why it bites if split |
|----------|------------------------|
| Barrels / index files (re-exports, shared `types`, API clients) | Add/add and import-order conflicts; values drift |
| Generated files (route trees, client SDKs, snapshots) | Conflicts; one slice references an entry another owns |
| Shared aggregates / entities | Two slices add the same member, and **two migrations edit one model snapshot** |
| Migrations + model snapshot | Divergent snapshots produce a broken model |
| Shared constants / contracts | Slices disagree on the value or shape |

**Rule of thumb:** if removing one slice from the series would leave a *dangling reference* in another, the seam was split wrong.

---

## Verification Gate Honesty

"Verification gate green" in a PR body is a factual claim that you ran the local Verification gate on this branch: `python scripts/axis.py verify` with the command matrix in [agent-checklist § Verification Gate](./agent-checklist.md#verification-gate--verify-before-push). Do not present unit-only output, a one-file test, or a partial command as the Verification gate.

The full suite is a separate claim: full local verification means full `dotnet test Axis.sln --nologo` plus the applicable frontend and drift checks. CI/branch protection is the authoritative full gate before merge.

If you cannot run a piece (e.g. an integration suite needs infra you do not have), **say so explicitly** in your own walk-through. Never tick a green box you did not verify — the cost of a false green lands on review, not on you.

---

## Merge order & rebase cadence

1. **Document the order** in each PR body (product/contract sequence).
2. **Enable branch protection:** *require branches to be up to date before merging*, so each slice's CI re-runs against the post-merge tree — a stale-`main` green is not accepted.
3. **After every merge**, rebase each remaining slice onto updated `main`, re-run `python scripts/axis.py verify`, and consolidate seams (drop anything now owned by the just-merged slice).
4. Do not mark the parent use case ✅ until all in-scope slices are merged or explicitly deferred.

---

## Decision tree

```
Does the change alter a contract already used on main?
├─ Yes → Same PR: API + caller (vertical slice)
└─ No
   ├─ New UI only, existing API → Frontend-only PR OK
   ├─ New API only, no main caller yet → Backend-only PR OK
   └─ Both needed for one user-visible outcome → Vertical slice

Does the slice reference a route / endpoint / symbol / constant
an unmerged sibling owns?
├─ Yes → Not isolated. Pull it in, or ship a compiling fallback.
└─ No  → Safe to open standalone.
```

---

## Failure modes → the rule that prevents them

| Symptom at merge time | Rule |
|-----------------------|------|
| Branch does not compile because it references something only a sibling adds | Two-sided test, side A · shared-seam ownership |
| "Verification gate green" in the body but CI is red | Verification gate honesty |
| Two open PRs add the same new file → add/add conflict | No duplicate new files across siblings |
| Two slices add the same member/migration → broken model snapshot | One owner per shared seam |
| Two slices disagree on a shared constant/contract value | One source for shared values/contracts |
| Merging slice B alone breaks the caller on `main` | Vertical when a contract changes · no "enable in a follow-up" |

---

## Agent workflow

1. Read the use-case ACs; split into slices. **List the shared seams and assign one owner each.**
2. For each slice: branch from `origin/main`, implement, run `python scripts/axis.py verify`, push, open a **draft** PR, and let CI prove the full gate.
3. PR description: Summary, linked spec, Requirements — plus one line: **Merge independence:** what still works on `main` if only this PR lands.
4. Do not mark the parent use case ✅ until all in-scope slices are merged or explicitly deferred.

---

## Checklist before push (each slice)

- [ ] Branch created from current `origin/main`, not from another feature branch
- [ ] **Stands alone:** fresh checkout builds + `python scripts/axis.py verify` green
- [ ] **Integrates:** trial rebase onto `origin/main` still builds + green
- [ ] No route / endpoint / symbol / constant referenced that an unmerged sibling owns (or a compiling fallback is in place)
- [ ] No new file path also added by another open PR
- [ ] Shared values/contracts imported from their single owner, not re-hardcoded
- [ ] Any partially-done spec bullet listed under `**Deferred (PR #N follow-up):**`
- [ ] `python scripts/axis.py check doc-drift` when `src/`, `tests/`, or `docs/use-cases/` change
- [ ] "Verification gate green" in the PR body reflects commands you actually ran (anything skipped is stated)
