# Search Experience

> **Navigation**: [docs/foundations/search/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Provide one accessible search interaction for collection grids and reference documents while the owning server query retains matching, ranking, scope, and storage decisions.

## Primary actor

- Signed-in Axis Platform user locating records or server-owned reference content.

## Trigger

- A collection grid or reference document exposes free-text search.

## Main flow

1. The user enters search text.
2. The client keeps the text in controlled, shareable surface state and debounces the owning server query.
3. A newer query cancels or supersedes the prior request and resets collection paging to the first page.
4. The server returns authoritative ordered results and optional structured highlight segments.
5. The surface renders results, semantic highlights, and stable loading, empty, no-result, error, or success states without matching locally.

## Alternate / error flows

- Empty query: restore the server's deterministic default result order.
- Rapid input: stale responses cannot replace results for newer text.
- No result: retain the search control and current document or grid frame with localized feedback.
- Request failure: preserve the current surface and expose a localized retry action.
- Unsupported locale: render the server-provided fallback content without exposing protocol values.
- Deep-linked reference: open the canonical document and focus the exact server identity even when search is empty.

## Acceptance Criteria

- **AC-001** Every product grid and searchable reference document sends free-text search to its owning server query; the client does not scan records or implement fuzzy ranking.
- **AC-002** Shared search state contains product-neutral text and busy behavior without database syntax, provider scores, feature DTOs, or navigation destinations.
- **AC-003** Collection search is controlled, debounced, shareable, resets to page one when text changes, and never presents partial client filtering as whole-dataset results.
- **AC-004** Newer search text cancels or supersedes older work, and stale responses cannot replace current results.
- **AC-005** Structured highlights preserve original text, render with a clearly distinguishable semantic `mark` treatment in light and dark modes, and do not change the result's accessible name.
- **AC-006** The canonical expression reference document searches current server metadata, context fields, and parameters while preserving grouped sections; an exact deep-link target remains visibly current and programmatically identified after focus moves to it.
- **AC-007** Edit-only insertion remains an action on the canonical document item and is absent in read-only mode.
- **AC-008** Search controls and results provide localized labels, keyboard access, visible focus, busy state, and result-count or no-result feedback.
- **AC-009** The foundation does not contain searchable business copy, field weights, matching algorithms, authorization, API routes, or read-store behavior.
- **AC-010** Rules, Business Objects, and future consumers use the same search interaction contract without feature-local search implementations.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | Shared search state debounces controlled server intent, resets paging, cancels or supersedes stale requests, and restores default results for an empty query. | AC-001, AC-002, AC-003, AC-004, AC-009 | UI component test | Yes |
| AT-002 | UI component | Grid consumers render authoritative server pages with localized loading, retry, empty, and no-result behavior without local text matching. | AC-001, AC-003, AC-008, AC-010 | UI component test | Yes |
| AT-003 | UI component | The canonical reference document preserves groups and targets, renders structured highlights, and exposes insertion only in edit mode. | AC-005, AC-006, AC-007, AC-008 | UI component test | Yes |
| AT-004 | Browser journey | Rules, Business Objects, and expression-reference search remain keyboard-operable and current during rapid input without console errors. | AC-001, AC-003, AC-004, AC-005, AC-006, AC-008, AC-010 | Browser automation | Yes |
| AT-005 | Static frontend | Shared and consuming code typechecks and lints without client matcher, searchable business metadata, feature DTO, or storage imports in the foundation. | AC-001, AC-002, AC-009, AC-010 | Frontend CI | Yes |

## Out Of Scope

- Matching, ranking, tokenization, field weights, authorization, and workspace isolation.
- Product-specific filters, columns, result actions, and navigation.
- Database indexes, read models, storage adapters, and API wire shapes.
- Semantic, vector, hybrid, recommendation, or generative search.
- A cross-module global-search route.

## Screen Flow

| Surface | Required contract |
|---|---|
| Grid search | Debounced server query, route-owned text and paging, stable table frame, and authoritative result page. |
| Reference search | Server-ranked items remain in canonical groups and show semantic highlights in the original localized text. |
| Reference target | Clicking a semantic term opens the canonical document, focuses the exact server identity, and keeps that entry visibly labeled as the current reference. |
| Request states | Preserve the surface while exposing busy, no-result, failure, retry, and current-result behavior. |

Required UI quality: searches must remain responsive during rapid input, stale responses must not win, result count and matching text must be immediately visible, deep-link targets must remain visibly current after programmatic focus, highlights must not alter accessible names, and empty queries must restore deterministic default results.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Contract | Done |
> | Frontend | Done |
> | Tests | Done |
>
> **Implemented:** Rules, Business Objects, and the canonical expression guide use controlled, debounced server search with authoritative paging, cancellation, grouped structured highlights, exact deep-link targets, localized states, and no client matcher.
>
> **Gaps vs spec:** None.
>
> **Deferred follow-ups:** Global search, alternative result layouts, and semantic search remain out of scope.
>
> **Verification:** Acceptance proof is tracked in the sibling evidence sidecar.
>
> **Decisions:** Every current product grid and the expression document cut over to server-owned search without a client fallback. Search matching is owned by [docs/playbooks/search.md](../../playbooks/search.md). The user approved this high-risk direction on 2026-07-25.
