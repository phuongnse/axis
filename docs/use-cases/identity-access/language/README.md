# Use case — Switch application language (English / Vietnamese)

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

_(One sentence about user value.)_.

## Primary actor

- _(Actor)_

## Trigger

- _(What starts the use case.)_

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

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
> | Frontend | ⏳ |
>
> **Gaps vs spec:** use case not started.
>
> **Decisions:**
> - Translation is key-based (`feature.section.key`) with English fallback.
> - New user-facing strings in migrated screens must come from locale files.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
