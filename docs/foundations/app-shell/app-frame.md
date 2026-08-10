# App Frame

> **Navigation**: [docs/foundations/app-shell/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Provide the shared frame for authenticated Axis Platform routes without owning dashboard content, account profile behavior, or session lifecycle behavior.

## Consumers

- Authenticated routes that need shared page chrome.

## Activation

- An authenticated route renders inside the application frame.

## Guarantees

- Renders authenticated route content within shared top-bar, main-content, and footer regions.
- Presents product identity, page context, signed-in identity, eligible Workspace context, preferences, and sign-out entry points without taking ownership of their workflows.
- Keeps route content full-width and leaves route-specific layout decisions to the consuming route.
- Presents version and copyright metadata in the footer.

## Alternate / error flows

- Narrow viewport: frame content reflows without horizontal page overflow.
- Constrained height: the app frame prevents document-level scrolling; route content owns any needed internal scroll container.
- Missing user label or initials: account actions menu uses the user fallback copy and initial placeholder.
- Sign-out selected: session lifecycle behavior is handed off to the sign-out use case.

## Acceptance Criteria

- **AC-001** Authenticated routes render page content inside the shared app frame.
- **AC-002** The frame exposes a top bar with product identity, page context, and one account-actions entry point ordered as signed-in identity, one flat eligible-Workspace choice set, preferences, then spatially separated sign-out.
- **AC-003** The frame exposes footer app metadata with version information and Axis Platform copyright.

*Quality*
- **AC-005** The frame fits supported desktop and mobile widths without horizontal page overflow, document-level scrolling, or a maximum content width on authenticated routes.
- **AC-006** Visible frame copy uses the frontend translation layer.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | App frame renders top bar, main content, footer metadata, and the ordered account surface with stable semantic interaction rows without a placeholder route navigation bar. | AC-001, AC-002, AC-003, AC-006 | UI component test | Yes |
| AT-002 | Browser journey | Desktop and mobile frame render an empty route surface, footer metadata, and the ordered account surface without a placeholder route navigation bar, console errors, document-level overflow, or shell-level content width caps. | AC-001, AC-002, AC-003, AC-005 | Browser automation | Yes |
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
| Account actions menu | Orient with the signed-in human identity, present one flat eligible-Workspace choice set with the current state, then language/theme preferences and a spatially separated sign-out action. Do not add profile editing or duplicate Personal/Organization grouping labels already conveyed by each option icon. |
| Main content | Preserve the owning route content in a full-width, non-document-scrolling region, including an empty route surface when no product screen exists yet. |
| Footer | Show version information on the left and Axis Platform copyright metadata on the right across the available viewport width. |

Required UI quality: frame landmarks and controls must be keyboard-reachable, visible copy must be localized, and the shell must not create document-level overflow at supported mobile or desktop widths.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Contract | Done |
> | Frontend | Done |
> | Tests | Done |
>
> **Implemented:** Authenticated routes render inside the shared App Frame with top bar, full-width main content, and footer. The frame exposes the signed-in identity, one flat eligible-Workspace choice set, preferences, and separated sign-out in semantic order; keeps visible copy localized; preserves an empty route surface; contains document-level overflow; does not impose route-content width caps; and shows footer version and copyright metadata. Placeholder route navigation remains absent until visible contributions exist.
>
> **Gaps vs spec:** N/A.
>
> **Deferred follow-ups:** N/A.
>
> **Verification:** Required AT rows are covered by UI component test, Playwright browser automation, and frontend CI.
>
> **Decisions:** App Frame is a foundation contract, not a use case. Authenticated use cases may rely on its page chrome, frame structure, and document-scroll containment; route-specific layout, profile behavior, sign-out lifecycle, and global module navigation remain owned elsewhere. Global sidebar behavior is owned by [docs/foundations/app-shell/module-navigation.md](./module-navigation.md) and does not render before visible contributions exist.
