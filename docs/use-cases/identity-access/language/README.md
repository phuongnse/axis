# Use case — Switch application language (English / Vietnamese)

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Switch the application language between English and Vietnamese so that I can read and operate the app in my preferred language.

## Primary actor

- Authenticated user

## Trigger

- User opens the language selector in the header (or settings) and picks a language.

## Main flow

1. User opens the language selector.
2. User selects `English` or `Vietnamese`.
3. App updates visible UI text immediately without a full reload.
4. App persists the selected locale and reuses it on next load.

## Alternate / error flows

- Locale resource key missing in the chosen language → app falls back to English for that key.
- Locale preference storage unavailable or corrupted → app defaults to English and remains usable.
- User is filling a form while switching locale → form input values remain unchanged.

## Context

Define user-facing localization and visual theme behavior for the SPA so each use case has explicit UI artifacts, flow behavior, acceptance criteria, and implementation status.

## Acceptance Criteria

**Purpose:** let users read and operate the app in their preferred language.  
**Primary actor:** authenticated user.  
**Trigger:** user changes language in header/settings selector.

#### Main flow
1. User opens language selector.
2. User selects `English` or `Vietnamese`.
3. App updates visible UI text immediately without full reload.
4. App persists selected locale and reuses it on next app load.

#### Alternate / error flows
- Locale resource key missing in chosen language → app falls back to English for that key.
- Locale preference storage unavailable/corrupted → app defaults to English and remains usable.
- User is filling a form while switching locale → form input values remain unchanged.

#### Acceptance Criteria

*Happy path*
- [ ] Language selector is available in authenticated app UI (header or settings) and supports `en` and `vi`.
- [ ] Selecting a language updates all visible UI text immediately without full-page reload.
- [ ] Language preference persists across page refreshes and browser restarts.

*Validation & errors*
- [ ] If a translation key is missing in selected locale, UI falls back to English string.
- [ ] If locale storage cannot be read, app defaults to English and remains usable.

*Edge cases*
- [ ] Switching language does not clear form input values or reset in-progress user actions.
- [ ] Right after sign-in, the app uses the previously selected locale.

*Out of scope*
- Runtime machine translation.
- Additional locales beyond `en` and `vi`.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | N/A |
> | Application | N/A |
> | Infrastructure | N/A |
> | API | N/A |
> | Frontend | ✅ |
>
> **Gaps vs spec:** none for the current SPA foundation. Backend-localized API error payloads are future work outside this frontend preference use case.
>
> **Implemented:**
> - `i18next` + `react-i18next` initialize key-based `en` / `vi` resources with English fallback.
> - `PreferenceControls` is available on public/auth screens and in the authenticated header.
> - Locale preference persists in `localStorage` under `axis.language`; `<html lang>` updates immediately.
> - Current visible SPA strings in landing, auth, provisioning, app shell, and dashboard scaffold use locale keys. Decorative public/auth backgrounds do not carry user-facing text.
> - Vitest covers immediate language switching and preference persistence.
>
> **Decisions:**
> - Translation is key-based (`feature.section.key`) with English fallback.
> - New user-facing strings in migrated screens must come from locale files.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |
