# App Frame

> **Navigation**: [docs/foundations/app-shell/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Provide the shared frame for authenticated Axis Platform routes without owning dashboard content, account profile behavior, or session lifecycle behavior.

## Primary actor

- Signed-in Axis Platform user

## Trigger

- User opens an authenticated Axis Platform route.

## Main flow

1. System renders the authenticated route inside the shared app frame.
2. System shows the Axis Platform brand mark and the current page context in the top bar.
3. System exposes profile context in the top-bar account trigger.
4. System groups preferences and sign-out inside the top-bar account actions menu panel.
5. System renders the route content in a full-width main content region.
6. System shows app version and Axis Platform copyright metadata in the footer.

## Alternate / error flows

- Narrow viewport: frame content reflows without horizontal page overflow.
- Missing user label or initials: account actions menu uses the user fallback copy and initial placeholder.
- Sign-out selected: session lifecycle behavior is handed off to the sign-out use case.

## Acceptance Criteria

*Frame structure*
- **AC-001** Authenticated routes render page content inside the shared app frame.
- **AC-002** The frame exposes a top bar with the Axis Platform brand mark, page context, and one account actions menu whose trigger shows profile context and whose panel groups preferences and sign-out.
- **AC-004** The frame exposes footer app metadata with version information and Axis Platform copyright.

*Quality*
- **AC-005** The frame fits supported desktop and mobile widths without horizontal page overflow and without imposing a maximum content width on authenticated routes.
- **AC-006** Visible frame copy uses the frontend translation layer.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | App frame renders banner, main content, footer metadata, and grouped account actions without a placeholder route navigation bar. | AC-001, AC-002, AC-004, AC-006 | UI component test | Yes |
| AT-002 | Browser journey | Desktop and mobile frame render an empty route surface, footer metadata, and account actions without a placeholder route navigation bar, console errors, horizontal overflow, or shell-level content width caps. | AC-001, AC-002, AC-004, AC-005 | Browser automation | Yes |
| AT-003 | Static frontend | Frame code typechecks, lints, and keeps localized copy keys valid. | AC-006 | Frontend CI | Yes |

## Out Of Scope

- Dashboard content and information architecture.
- Route-specific contained, fluid, or canvas workspace layout decisions.
- Account profile behavior.
- Sign-out backend/session behavior.
- Future navigation destinations beyond currently implemented routes.
- Global module navigation until [docs/foundations/app-shell/module-navigation.md](./module-navigation.md) is implemented with at least one visible contribution.
- Canvas-specific tool, layer, property, or asset panels; those belong to the owning canvas workspace feature rather than global app shell chrome.

## Screen flow

| Screen | Required contract |
|---|---|
| Authenticated app frame | Render top bar, main content, and footer around authenticated route content. |
| Top bar | Show the Axis Platform brand mark, page context, and a compact account trigger with profile context across the available viewport width. |
| Account actions menu | Show language/theme preferences and sign-out without repeating profile summary or adding profile editing behavior. |
| Main content | Preserve the owning route content exactly in a full-width region, including an empty route surface when no product screen exists yet. |
| Footer | Show version information on the left and Axis Platform copyright metadata on the right across the available viewport width. |

Required UI quality: frame landmarks and controls must be keyboard-reachable, visible copy must be localized, and the layout must not create horizontal page overflow at supported mobile or desktop widths.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Contract | Done |
> | Frontend | Done |
> | Tests | Done |
>
> **Implemented:** Authenticated routes render inside the shared App Frame with top bar, full-width main content, and footer. The frame exposes profile context in a compact account trigger, groups preferences and sign-out in its menu panel, keeps visible copy localized, preserves an empty dashboard route surface, does not impose route content width caps, and shows footer version/copyright metadata. Placeholder route navigation is intentionally absent until the app has meaningful authenticated destinations.
>
> **Gaps vs spec:** N/A.
>
> **Deferred follow-ups:** N/A.
>
> **Verification:** Required AT rows are covered by UI component test, Playwright browser automation, and frontend CI.
>
> **Decisions:** App Frame is a foundation contract, not a use case. Future authenticated use cases may rely on its page chrome and frame structure; dashboard content, route-specific contained/fluid/canvas layout, profile behavior, sign-out session lifecycle, and global module navigation remain owned elsewhere. Account display context comes from the current-user profile when available; the frame must not assume access tokens are frontend-decodable. Global sidebar behavior is owned by [docs/foundations/app-shell/module-navigation.md](./module-navigation.md) and must not render before visible contributions exist; canvas-specific side panels are owned by the canvas workspace feature.
