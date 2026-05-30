# PR slicing — isolated, mergeable increments

> **Navigation**: [← docs/README.md](../README.md) · [← agent-checklist.md](./agent-checklist.md) · [← process.md](./process.md)

Use this when a use case is too large for one PR. **Each PR must merge into `main` on its own** — CI green, no dependency on another open PR, and the shipped path must still work for users.

---

## Rules (P0 for agents)

| Rule | Meaning |
|------|---------|
| **Branch from `main`** | Every slice starts as `git checkout -b cursor/{slug}-9e73 origin/main`. Never stack slice B on slice A's branch. |
| **Vertical when API shape changes** | If an endpoint adds **required** fields or changes response contracts, ship **backend + minimal frontend** in the **same** PR so `main` never breaks. |
| **Horizontal when additive** | New routes/screens that use existing APIs can be frontend-only. New APIs with no callers yet can be backend-only only if nothing on `main` calls them. |
| **No “enable in follow-up”** | Do not merge backend that returns 400 for the current register form, or frontend that calls endpoints that do not exist on `main`. |
| **Honest callouts** | Update `> **Implementation status**` per slice; use `**Deferred (PR #N follow-up):**` for bullets intentionally left to a later slice. |
| **Merge order documented** | List recommended merge order in the PR body when slices are sequenced for product flow (not for technical dependency). |

**Anti-pattern:** `cursor/feature-full` branched from `cursor/feature-part1` — merging “full” without “part1” breaks the tree; merging “part1” alone is fine but “full” is not isolated.

---

## Decision tree

```
Does the change alter a contract already used on main?
├─ Yes → Same PR: API + caller (vertical slice)
└─ No
   ├─ New UI only, existing API → Frontend-only PR OK
   ├─ New API only, no main caller yet → Backend-only PR OK
   └─ Both needed for one user-visible outcome → Vertical slice
```

---

## Example: `register-org` (platform-foundation)

Spec: [register-org/README.md](../use-cases/platform-foundation/register-org/README.md)

| Slice | Branch suffix | Scope | Self-contained because |
|-------|---------------|--------|-------------------------|
| 1 — Journey | `register-org-journey` | Frontend: confirmation, verify, provisioning poll | Uses APIs already on `main` (`POST /organizations`, verify 204, `GET /provisioning-status`) |
| 2 — Legal + slug | `register-org-legal-slug` | Backend: terms columns, `GET /legal/versions`, slug preview, validator **+** frontend: Terms checkbox, slug preview on register | Register form on `main` still works after merge |
| 3 — Verify session | `register-org-verify-session` | Backend: verify 200 + session cookie **+** frontend: PKCE after verify, callback → provisioning when token stored | Without PKCE, user still reaches provisioning via `?token=` (slice 1) |
| 4 — Retry | `register-org-retry-provisioning` | Backend: `POST /retry-provisioning` **+** frontend: Try again button | Provisioning poll (slice 1) unchanged if retry not used |
| 5 — OAuth | `register-org-oauth` | Providers, external registration session, `register-org-complete`, tests | Email/password path unaffected; providers gated by config |

**Recommended merge order:** 1 → 2 → 3 → 4 → 5 (product flow; slice 5 can parallelize after 1 if staffed).

After each merge, rebase remaining slice branches onto updated `main` before opening/updating the next PR.

---

## Agent workflow

1. Read the use case ACs; split into slices using the table above (or write a new one in the PR series).
2. For each slice: branch from `origin/main`, implement, run Gate 1 (`dotnet test`, `npm run ci`, drift if applicable), push, open **draft** PR.
3. PR description: Summary, linked spec, Requirements — plus one line: **Merge independence:** what still works on `main` if only this PR lands.
4. Do not mark parent use case ✅ until all in-scope slices are merged or explicitly deferred.

---

## Checklist before push (each slice)

- [ ] Branch created from current `origin/main`, not from another feature branch
- [ ] No imports/routes/tests that require an unmerged sibling PR
- [ ] If spec bullet is partially done, callout lists deferral with slice/PR name
- [ ] `./scripts/check-doc-drift.sh` when `src/`, `tests/`, or `docs/use-cases/` change
