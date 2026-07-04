# Select Site Theme

> **Navigation**: [docs/use-cases/site-experience/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Let a visitor or authenticated user choose how supported web surfaces resolve light and dark presentation, with the choice kept as a browser preference.

## Primary actor

- Visitor or authenticated user

## Trigger

- User opens any web route.
- User changes the site theme from a preferences control.

## Main flow

1. User opens a web route.
2. System applies an initial supported theme mode before React renders, using a saved browser preference when present and supported, then the product fallback theme mode.
3. When the active mode is system, system resolves light or dark presentation from the browser color-scheme preference.
4. User opens the preferences control and chooses a supported theme mode.
5. System applies the selected mode immediately, updates browser theme metadata, preserves the current route and in-progress form state, and saves the browser preference when storage is available.
6. User continues using the web app with the selected theme behavior.

## Alternate / error flows

- Saved browser preference is missing, unsupported, or unreadable: ignore it and continue with the product fallback theme mode.
- Browser color-scheme preference is unavailable: system mode resolves to light presentation.
- Browser storage cannot be written: keep the current in-page theme usable and do not crash.
- User is authenticated: keep the theme preference browser-owned and do not call profile or account preference APIs.

## Acceptance Criteria

*Happy path*
- **AC-001** User can choose supported theme modes from public auth screens and authenticated app screens.
- **AC-002** A selected theme mode applies immediately without changing route, clearing entered form data, or requiring a full page reload.
- **AC-003** Initial page load applies a supported saved browser theme mode before React renders.
- **AC-004** When no supported saved value exists, initial page load uses the product fallback theme mode.
- **AC-005** System mode resolves light or dark presentation from the browser color-scheme preference.
- **AC-006** Supported theme mode values are exactly `light`, `dark`, and `system`, with `system` as the product fallback.

*Validation & errors*
- **AC-007** Unsupported saved browser values are ignored instead of applied.
- **AC-008** Storage access failure does not crash the app and keeps theme selection usable in memory.
- **AC-009** Theme selection does not require an account, does not call an API, and is not persisted as a server profile preference.

*Edge cases*
- **AC-010** Changing theme updates document theme metadata and the browser color scheme for the resolved presentation.
- **AC-011** System mode reflects browser color-scheme changes while the page is open.
- **AC-012** Theme selection remains available and non-blocking on supported public and authenticated surfaces.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | Visitor opens a public auth screen with no saved theme preference, chooses dark mode, reloads, and sees the dark presentation restored from the browser preference | AC-001, AC-002, AC-003, AC-004, AC-006, AC-010, AC-012 | Browser automation | Yes |
| AT-002 | UI component | Theme control exposes only supported theme modes, applies the selected mode immediately, preserves form state, and satisfies the required UI quality bar | AC-001, AC-002, AC-006, AC-010, AC-012 | UI component test | Yes |
| AT-003 | UI component | Unsupported or unreadable browser theme preference falls back without crashing | AC-004, AC-007, AC-008 | UI component test | Yes |
| AT-004 | UI component | System mode resolves from browser color-scheme preference and updates when the browser preference changes while the page is open | AC-005, AC-011 | UI component test | Yes |
| AT-005 | UI component | Theme selection remains browser-owned for authenticated users and does not call profile or account preference APIs | AC-009 | UI component test | Yes |

## Out Of Scope

- Server persistence for authenticated theme preferences.
- Workspace-specific theme preferences.
- Per-surface custom palettes beyond the supported site theme modes.
- User-authored custom colors.

## Screen flow

| Screen | Required contract |
|---|---|
| App boot | Resolve initial theme before React renders from supported browser preference or the product fallback theme mode. |
| Public auth screens | Render the preferences control and allow unauthenticated theme selection without an API call. |
| Authenticated app shell | Render the preferences control and keep theme selection browser-owned. |
| System theme mode | Follow browser color-scheme preference and update the resolved presentation when that preference changes. |

Required UI quality: theme controls must have programmatic labels, keyboard access, focus visibility, visible selected state, stable overlay behavior, and copy that fits in every supported language on supported mobile and desktop viewports.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | N/A |
> | Application | N/A |
> | Infrastructure | N/A |
> | API | N/A |
> | Frontend | Done |
>
> **Implemented:** Browser-owned theme mode selection for `light`, `dark`, and `system`; boot-time theme resolution from browser storage before React renders; immediate document theme metadata and color-scheme updates; system-mode browser color-scheme tracking while the page is open; localized public and authenticated preferences menu controls; and light/dark theme tokens.
>
> **Gaps vs spec:** N/A.
>
> **Deferred follow-ups:** N/A.
>
> **Verification:** Required AT rows are covered by browser automation, UI component tests, and frontend static checks.
>
> **Decisions:** Site Experience owns the web theme selection use case because it is a cross-site presentation preference. Theme preference is browser-owned for this slice; server persistence and workspace-scoped theme behavior are explicit future boundaries.
