# PR slicing — isolated, mergeable increments

> **Navigation**: [← docs/README.md](../README.md) · [← agent-checklist.md](./agent-checklist.md) · [← process.md](./process.md)

Use this when a use case is too large for one PR. **Each PR must merge into `main` on its own** — CI green, no dependency on another open PR, and the shipped path must still work for users.

> **Why this doc is strict.** The `register-org` series (#152–#157) was sliced "by the book" yet shipped branches that did not compile, claimed "Gate 1 green" while CI was red, duplicated files across siblings, and disagreed on a shared constant. Splitting into branches is **not** the same as isolating. See [§ Worked retrospective](#worked-retrospective-register-org) for exactly what broke and which rule below would have caught it.

---

## What "isolated" actually means (two-sided test)

A slice is isolated only if **both** sides hold. Test both before marking the PR ready — branches cut from a stale `main` pass neither by accident.

| Side | Definition | How to verify |
|------|------------|---------------|
| **A — Stands alone** | A fresh checkout of the branch **compiles and the full Gate 1 is green**, and every route/endpoint/contract the slice *references* exists on the branch's merge target. | `git switch <branch>` on a clean tree → run the **full** Gate 1 (below). No filters. |
| **B — Integrates** | After **rebasing onto current `main`**, it still compiles and is green, and merging it requires **no unmerged sibling**. | Rebase onto `origin/main`, re-run Gate 1. |

If a slice navigates to a route, calls an endpoint, imports a symbol, or reads a constant that **another unmerged slice owns**, it fails side A — it is not isolated, it is *stacked in disguise*. Either pull that dependency into this slice, or stub it in a way that compiles and degrades gracefully on the current `main`.

> **Concrete trap (happened in #155):** the callback navigated to a typed `/provisioning` route defined only in the journey slice. The branch did not type-check. Fix was a `window.location.assign('/provisioning?...')` hard redirect that compiles and degrades gracefully whether or not the journey slice is present.

---

## Rules (P0 for agents)

| Rule | Meaning |
|------|---------|
| **Branch from `main`** | Every slice starts as `git checkout -b cursor/{slug}-9e73 origin/main`. Never stack slice B on slice A's branch. |
| **Prove both sides before "ready"** | Run the full Gate 1 on the branch **and** after a trial rebase onto `origin/main`. A green claim means you ran it — see [§ Gate 1 honesty](#gate-1-honesty). |
| **Vertical when API shape changes** | If an endpoint adds **required** fields or changes response contracts, ship **backend + minimal frontend** in the **same** PR so `main` never breaks. |
| **Horizontal when additive** | New routes/screens that use existing APIs can be frontend-only. New APIs with no callers yet can be backend-only only if nothing on `main` calls them. |
| **One owner per shared seam** | A file or symbol touched by >1 slice has exactly **one owning slice**; the others **consume**, never re-add. See [§ Shared-seam ownership](#shared-seam-ownership). |
| **No duplicate new files across siblings** | Adding the same new path in two open PRs is a slicing defect (add/add conflict on merge). Pick one owner. |
| **One source for shared constants/contracts** | A value or contract used by >1 slice (legal version, route name, DTO shape) is defined once and imported; never hardcode a second copy with a different value. |
| **No “enable in follow-up”** | Do not merge backend that returns 400 for the current register form, or frontend that calls endpoints that do not exist on `main`. |
| **Honest callouts** | Update `> **Implementation status**` per slice; use `**Deferred (PR #N follow-up):**` for bullets intentionally left to a later slice. |
| **Merge order documented + enforced** | List the recommended merge order in the PR body, and rebase remaining slices after **each** merge — see [§ Merge order & rebase cadence](#merge-order--rebase-cadence). |

**Anti-pattern:** `cursor/feature-full` branched from `cursor/feature-part1` — merging “full” without “part1” breaks the tree; merging “part1” alone is fine but “full” is not isolated.

---

## Shared-seam ownership

Most cross-slice damage happens at a handful of **shared seams** — files or symbols that several slices naturally want to touch. Before opening the series, list the seams and assign **one owner** each. Every other slice imports/consumes the owner's version; if a later slice needs a not-yet-merged seam, it owns a compiling fallback (see two-sided test, side A).

Typical seams in this repo:

| Seam | Examples | Failure if duplicated |
|------|----------|------------------------|
| Frontend barrels | `features/{x}/api.ts`, `types.ts`, `index.ts` | Import-order + add/add conflicts; constants drift |
| Generated route tree | `frontend/src/routeTree.gen.ts` | Conflicts; a slice references a route another owns |
| Aggregates / entities | `User.cs` and friends | Two slices add the same method/property + **two migrations editing one snapshot** |
| EF migrations + model snapshot | `Migrations/*` | Divergent `IdentityDbContextModelSnapshot.cs` → broken model |
| Shared constants/contracts | legal version, scopes, claim names, route names | Slices disagree on the value (see retrospective) |

**Rule of thumb:** if removing a slice from the series would leave a *dangling reference* in another slice, the seam was split wrong.

---

## Gate 1 honesty

“Gate 1 green” in a PR body is a factual claim that you ran, on this branch:

- **.NET:** full `dotnet build Axis.sln` (zero warnings) **and** `dotnet test` for the affected module(s) — no solution filter, mirroring CI scope ([CLAUDE.md Gate 1 policy](../../CLAUDE.md#gates)).
- **Frontend:** `npm run ci` (tsc + Biome) **and** `npm test` — the **whole** suite, not the file you touched.
- **Drift:** `./scripts/check-doc-drift.sh` when `src/`, `tests/`, or `docs/use-cases/` changed.

If you cannot run a piece (e.g. Testcontainers needs Docker you don't have), **say so explicitly** in your own walk-through — never tick a green box you did not verify. In the `register-org` series three branches claimed green while `tsc`/Biome/Vitest/drift were red; the cost landed on review, not on the author.

---

## Merge order & rebase cadence

1. **Document the order** in each PR body (product/contract sequence, e.g. `1 → 2 → 3 → 4 → 5`).
2. **Enable branch protection:** *Require branches to be up to date before merging* on `main`, so each slice's CI re-runs against the post-merge tree — a stale-`main` green is not accepted.
3. **After every merge**, rebase each remaining slice branch onto updated `main`, re-run Gate 1, and resolve any seam consolidation (drop now-duplicated files/symbols owned by the just-merged slice).
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
another unmerged slice owns?
├─ Yes → Not isolated. Pull it in, or ship a compiling fallback.
└─ No  → Safe to open standalone.
```

---

## Example: `register-org` (platform-foundation)

Spec: [register-org/README.md](../use-cases/platform-foundation/register-org/README.md)

| Slice | Branch suffix | Scope | Owns these seams |
|-------|---------------|--------|-------------------|
| 1 — Journey | `register-org-journey` | Frontend: confirmation, verify, provisioning poll | `/provisioning` + `/register/confirmation` + `/auth/verify` routes; `provisioning-steps`, `registration-context` |
| 2 — Legal + slug | `register-org-legal-slug` | Backend: terms columns, `GET /legal/versions`, slug preview, validator **+** frontend Terms checkbox / slug preview | `User` legal-acceptance fields + migration; **the legal version constant** (`WellKnownLegalDocuments`) + `GET /api/legal/versions` |
| 3 — Verify session | `register-org-verify-session` | Backend: verify 200 + session cookie **+** frontend PKCE after verify | `verify-email` response contract |
| 4 — Retry | `register-org-retry-provisioning` | Backend: `POST /retry-provisioning` **+** frontend Try again | retry endpoint |
| 5 — OAuth | `register-org-oauth` | Providers, external session, `register-org-complete` | external-registration endpoints/aggregates |

**Recommended merge order:** 1 → 2 → 3 → 4 → 5 (product flow; slice 5 can parallelize after 1 if staffed). Slices 3/5 **consume** the seams owned by 1/2 — e.g. slice 5 must read the legal version from slice 2's source, not hardcode its own.

---

## Worked retrospective: register-org

What actually shipped, and which rule above prevents it next time:

| Defect (as merged) | Rule violated |
|--------------------|---------------|
| #155 callback navigated to `/provisioning` (route owned by #153) → branch did not type-check | Two-sided test, side A; shared-seam ownership |
| #153, #155, #157 marked "Gate 1 green" while `tsc`/Biome/Vitest/drift were red | Gate 1 honesty |
| `post-verify-session.ts`, `verifyEmail()`, `callback` changes added in **both** #153 and #155 | No duplicate new files; one owner per seam |
| `User.RecordLegalAcceptance` + users migration added in **both** #154 and #157 | One owner per seam (aggregates + migration snapshot) |
| `pr-slicing.md` itself added as a **new file in both** #153 and #154 | No duplicate new files across siblings |
| `LEGAL_VERSION='1.0'` (#157) vs `'2026-05-01'` (#154) → OAuth completion 400s after #154 merges | One source for shared constants |
| Query handlers shipped with no tests → drift red on #157 | Gate 1 honesty (drift is part of Gate 1) |

---

## Agent workflow

1. Read the use case ACs; split into slices. **List the shared seams and assign one owner each** (table above).
2. For each slice: branch from `origin/main`, implement, run the **full** Gate 1 (`dotnet build`/`dotnet test`, `npm run ci`, `npm test`, drift if applicable), push, open **draft** PR.
3. PR description: Summary, linked spec, Requirements — plus one line: **Merge independence:** what still works on `main` if only this PR lands.
4. Do not mark the parent use case ✅ until all in-scope slices are merged or explicitly deferred.

---

## Checklist before push (each slice)

- [ ] Branch created from current `origin/main`, not from another feature branch
- [ ] **Stands alone:** fresh checkout builds + full Gate 1 green (no filters)
- [ ] **Integrates:** trial rebase onto `origin/main` still builds + green
- [ ] No route/endpoint/symbol/constant referenced that an unmerged sibling owns (or a compiling fallback is in place)
- [ ] No new file path also added by another open PR
- [ ] Shared constants/contracts imported from their single owner, not re-hardcoded
- [ ] Any partially-done spec bullet listed under `**Deferred (PR #N follow-up):**`
- [ ] `./scripts/check-doc-drift.sh` when `src/`, `tests/`, or `docs/use-cases/` change
- [ ] "Gate 1 green" in the PR body reflects commands you actually ran (anything skipped is stated)
