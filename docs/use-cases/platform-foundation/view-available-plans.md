# Use case — View available plans

> **Navigation**: [← Platform Foundation](./README.md)

## Purpose

Compare available subscription plans so that I can choose the one that fits my needs.

## Primary actor

- Prospective customer (or signed-in org admin viewing pricing)

## Trigger

- User opens the public pricing page or plan comparison during signup.

## Main flow

1. Load active plans from `GET /api/plans`.
2. Render comparison table with limits and feature flags.
3. Highlight current plan when the user is signed in.

## Alternate / error flows

- Plan API failure → static fallback with “Pricing may be outdated” notice.

## Acceptance Criteria

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

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| pricing | [source](./wireframes/pricing.excalidraw) | [preview](./wireframes/pricing.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ✅ |
> | Application | ✅ |
> | Infrastructure | ✅ |
> | API | ✅ |
> | Frontend | ⏳ |
>
> **Gaps vs spec:** public pricing page UI, static fallback on load failure, "Current plan" badge (Frontend only).
>
> **Decisions:** retired plans hidden on public page; orgs on retired plans still resolve plan via API when signed in.
