# Select Site Theme

> **Navigation**: [docs/use-cases/site-experience/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Let a visitor or authenticated user choose how supported web surfaces resolve light and dark presentation, with authenticated choices persisted as a user-level profile preference.

## Primary actor

- Visitor or authenticated user

## Trigger

- User opens any web route.
- User changes the site theme from a preferences control.

## Main flow

1. User opens a web route.
2. System applies an initial supported theme mode before React renders, using a saved browser preference when present and supported, then the product fallback theme mode.
3. When the active mode is system, system resolves light or dark presentation from the browser color-scheme preference.
4. If the user is authenticated, system loads the Identity-owned user profile theme preference.
5. If the authenticated profile has a supported theme preference, system applies that server value and mirrors it to browser storage for the next page load.
6. User opens the preferences control and chooses a supported theme mode.
7. System applies the selected mode immediately, updates browser theme metadata, preserves the current route and in-progress form state, and saves the browser preference when storage is available.
8. If the user is authenticated, system persists the selected theme mode as the Identity-owned user profile theme preference.
9. User continues using the web app with the selected theme behavior.

## Alternate / error flows

- Saved browser preference is missing, unsupported, or unreadable: ignore it and continue with the product fallback theme mode.
- Browser color-scheme preference is unavailable: system mode resolves to light presentation.
- Browser storage cannot be written: keep the current in-page theme usable and do not crash.
- Authenticated profile theme is missing: keep the current resolved theme until the user explicitly chooses a theme while authenticated.
- Authenticated profile theme is unsupported: ignore it, use the product fallback theme mode, and do not mirror the unsupported value to browser storage.
- Authenticated theme persistence fails: keep the current in-browser theme usable, show a clear retry state, and do not claim the choice was saved across devices.
- Theme profile load fails: keep the current resolved theme usable and let the user retry through the normal authenticated surface refresh or theme control.

## Acceptance Criteria

*Happy path*
- **AC-001** User can choose supported theme modes from public auth screens and authenticated app screens.
- **AC-002** A selected theme mode applies immediately without changing route, clearing entered form data, or requiring a full page reload.
- **AC-003** Initial page load applies a supported saved browser theme mode before React renders.
- **AC-004** When no supported saved value exists, initial page load uses the product fallback theme mode.
- **AC-005** System mode resolves light or dark presentation from the browser color-scheme preference.
- **AC-006** Supported theme mode values are exactly `light`, `dark`, and `system`, with `system` as the product fallback.
- **AC-007** Authenticated users can persist a supported theme mode as a user-level profile preference owned by the Identity module.
- **AC-008** Authenticated profile reads expose the server-persisted theme preference when one exists.
- **AC-009** When an authenticated server preference exists, the frontend treats it as the source of truth and mirrors it to browser storage for the next load.
- **AC-010** Unauthenticated theme choice remains a browser preference and does not require an account or API call.

*Validation & errors*
- **AC-011** Unsupported theme values are rejected by authenticated persistence and cannot be selected through the theme control.
- **AC-012** Unsupported saved browser values and unsupported server values are ignored instead of applied.
- **AC-013** Storage access failure does not crash the app and keeps theme selection usable in memory.
- **AC-014** Authenticated persistence failure shows a clear retry state, keeps the current in-browser theme usable, and does not present the preference as saved across devices.
- **AC-015** Authenticated profile load failure keeps the current resolved theme usable and does not block public auth flows.

*Edge cases*
- **AC-016** Changing theme updates document theme metadata and the browser color scheme for the resolved presentation.
- **AC-017** System mode reflects browser color-scheme changes while the page is open.
- **AC-018** Theme preference is user-owned, not workspace-owned; switching or selecting a workspace must not change the persisted theme mode.
- **AC-019** Registration, sign-in, verification, and callback flows do not silently create or update a server theme preference.
- **AC-020** Theme selection remains available and non-blocking on supported public and authenticated surfaces.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | Visitor opens a public auth screen with no saved theme preference, chooses dark mode, reloads, and sees the dark presentation restored from the browser preference | AC-001, AC-002, AC-003, AC-004, AC-006, AC-010, AC-016, AC-020 | Browser automation | Yes |
| AT-002 | Browser journey | Authenticated user selects a theme mode, reloads, and the server-persisted profile preference is applied and mirrored to browser storage | AC-007, AC-008, AC-009, AC-016 | Browser automation + API integration test | Yes |
| AT-003 | API boundary | Identity-owned theme persistence accepts only supported values, rejects unsupported values, and returns the saved value on profile reads | AC-006, AC-007, AC-008, AC-011 | API integration test | Yes |
| AT-004 | Application boundary | User-level theme preference remains independent of workspace selection | AC-007, AC-018 | Application test | Yes |
| AT-005 | UI component | Theme control exposes only supported theme modes, applies the selected mode immediately, preserves form state, and satisfies the required UI quality bar | AC-001, AC-002, AC-006, AC-011, AC-020 | UI component test | Yes |
| AT-006 | UI component | Unsupported or unreadable browser theme preference falls back without crashing | AC-004, AC-012, AC-013 | UI component test | Yes |
| AT-007 | UI/API boundaries | Authenticated persistence or profile load failure keeps the current theme usable and shows retry/recovery state without claiming cross-device save | AC-014, AC-015 | UI component test + API integration test | Yes |
| AT-008 | UI component | System mode resolves from browser color-scheme preference and updates when the browser preference changes while the page is open | AC-005, AC-017 | UI component test | Yes |
| AT-009 | Browser journey | Registration, sign-in, verification, and callback flows do not silently create or update server theme preference | AC-019 | Browser automation + Application test | Yes |

## Out Of Scope

- Workspace-specific theme preferences.
- Per-surface custom palettes beyond the supported site theme modes.
- User-authored custom colors.

## Screen flow

| Screen | Required contract |
|---|---|
| App boot | Resolve initial theme before React renders from supported browser preference or the product fallback theme mode. |
| Public auth screens | Render the preferences control and allow unauthenticated theme selection without an API call. |
| Authenticated app shell | Load server-persisted user theme when available, apply it as source of truth, and expose theme selection in an authenticated surface. |
| Theme persistence state | Show pending, saved, retry, and non-blocking failure states without clearing route or form state. |
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
> **Implemented:** Browser-owned theme mode selection for `light`, `dark`, and `system`; boot-time theme resolution from browser storage before React renders; immediate document theme metadata and color-scheme updates; system-mode browser color-scheme tracking while the page is open; localized public and authenticated preferences menu controls; authenticated user theme preference API and persistence; server-profile theme sync; and light/dark theme tokens.
>
> **Gaps vs spec:** N/A.
>
> **Deferred follow-ups:** N/A.
>
> **Verification:** Required AT rows are covered by browser automation, UI component tests, API integration tests, application tests, infrastructure tests, and frontend static checks.
>
> **Decisions:** Site Experience owns the web theme selection use case because it is a cross-site presentation preference. Identity owns authenticated user theme persistence because the preference belongs to the user profile, not workspace-scoped or shared platform state. Server preference is the authenticated source of truth when present; browser preference supports bootstrapping and unauthenticated use. Workspace-scoped theme behavior is an explicit future boundary.
