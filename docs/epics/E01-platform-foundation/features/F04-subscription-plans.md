# F04 — Subscription Plan Management

[← Back to E01](../README.md)

> **Wireframe**: [docs/epics/E01-platform-foundation/wireframes/pricing.excalidraw](../wireframes/pricing.excalidraw) · [preview](../wireframes/pricing.svg)

---

## Description

Define subscription plan tiers with feature limits and enforce those limits at the API level. Billing integration is out of scope for MVP — this feature covers plan definitions and enforcement logic only.

---

## User Stories

### US-010 — View available plans

**As a** prospective customer, **I want to** compare available subscription plans **so that** I can choose the one that fits my needs.

**Acceptance Criteria:**

*Happy path*
- [ ] A public pricing page lists all active plans with a side-by-side feature comparison table.
- [ ] Each plan shows: name, monthly price (or "Free"), workflow limit, execution limit per month, user limit, storage limit, and key feature flags.
- [ ] Signed-in users see their current plan highlighted with a "Current plan" badge.

*Validation & errors*
- [ ] If the pricing page fails to load plan data, it shows a static fallback with a "Pricing may be outdated" notice rather than a blank page.

*Edge cases*
- [ ] A plan that has been retired (no longer available for new signups) is not shown on the public pricing page but remains visible to existing orgs still on that plan.

*Out of scope*
- Monthly vs annual billing toggle — Phase 2.
- Per-seat pricing — not in MVP.

---

### US-011 — Enforce plan limits at the API

**As a** platform operator, **I want** API endpoints to enforce plan limits **so that** organizations cannot exceed their subscription without upgrading.

**Acceptance Criteria:**

*Happy path*
- [ ] When an org is within limits, all operations proceed normally with no noticeable overhead.
- [ ] Plan limit checks complete in under 10 ms (Redis-cached counters).

*Validation & errors*
- [ ] Creating a workflow beyond the plan's workflow limit returns HTTP 402 with body: `{ "error": "plan_limit_exceeded", "limit_type": "workflows", "current": N, "max": M, "upgrade_url": "..." }`.
- [ ] Triggering an execution when the monthly execution limit is reached returns HTTP 402 with `limit_type: "executions_per_month"`.
- [ ] Inviting a user beyond the user limit returns HTTP 402 with `limit_type: "users"`.
- [ ] The HTTP 402 response always includes a human-readable `message` field in addition to the machine-readable `error` field.

*Edge cases*
- [ ] Monthly execution counters reset at midnight UTC on the 1st of each month; a TTL on the Redis key ensures automatic reset.
- [ ] If Redis is unavailable, the limit check falls back to a DB count query (slower but correct) and logs a warning.
- [ ] Bulk operations (e.g., importing 5 workflows) check the limit against the total before beginning any creation; partial success is not allowed.
- [ ] Deleting a workflow decrements the workflow counter immediately.

*Out of scope*
- Soft limits with grace period (allowing some overage before blocking) — not in MVP.

---

### US-012 — Change organization plan (admin override)

**As a** Platform Admin, **I want to** manually change an organization's plan **so that** I can support early customers and testing without a billing integration.

**Acceptance Criteria:**

*Happy path*
- [ ] Platform Admin dashboard has a "Change plan" action per organization.
- [ ] Selecting a new plan updates the org's plan immediately; new limits take effect on the next API request.
- [ ] The org's limit counters in Redis are refreshed after a plan change.

*Validation & errors*
- [ ] Downgrading to a lower plan when the org already exceeds the new limits: the change is allowed, but existing resources over the limit are not deleted. The org is flagged as "over limit" and blocked from creating additional resources until they are within the new limits.
- [ ] A non-Platform-Admin cannot reach this endpoint (HTTP 403).

*Edge cases*
- [ ] Plan change is logged in the platform audit log with: who changed, from what plan, to what plan, and at what time.

*Out of scope*
- Org-initiated plan upgrade — Phase 2 (requires billing integration).

> **Implementation status** — Domain: ✅ | Application: ✅ | Infrastructure: ✅ | API: ✅ (backend AC) | Frontend: ⏳
> Gaps vs spec: US-010 pricing page UI, static fallback on load failure, "Current plan" badge (Frontend only). No multi-workflow bulk-import API yet — single-workflow import/duplicate enforce +1 before save (US-011 bulk AC applies when bulk endpoint exists).
> **Done (backend):** `GET /api/plans` with limits + `featureFlags` + `isAvailableForNewSignups`; signed-in org on retired plan still sees that plan; 402 on create/duplicate/import workflow, invite user, start execution; Redis read-through cache + INCR/DECR on mutate + execution key TTL to month-end UTC; DB fallback + warning when Redis write/read fails; delete workflow decrements cache; platform plan change 403 + audit log + cache refresh; downgrade over limit blocks new creates via existing usage check (resources not deleted).
> **Deferred (follow-up PR):** atomic check-and-consume for monthly execution starts (race can briefly exceed cap under concurrency); fail-closed when usage counter/Redis unavailable (today logs warning and treats usage as 0).
> Decisions: `featureFlags` derived from plan slug for MVP (no JSON column); `PlatformAdmin:UserIds` config for US-012.
