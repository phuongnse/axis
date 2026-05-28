# Use case — Switch application language (English / Vietnamese)

> **Navigation**: [← Identity Access](./README.md)

## Purpose

_(One sentence about user value.)_

## Primary actor

- _(Actor)_

## Trigger

- _(What starts the use case.)_

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Define user-facing localization and visual theme behavior for the SPA so each use case has explicit UI artifacts, flow behavior, acceptance criteria, and implementation status.

## Acceptance Criteria

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


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

---

## Wireframes

| Use case | Screen | Excalidraw | Preview |
|--------|--------|------------|---------|
| switch-language | app-shell-language-selector | N/A (to be added) | N/A (to be added) |
| switch-language | settings-language | N/A (to be added) | N/A (to be added) |
| switch-visual-theme | app-shell-theme-selector | N/A (to be added) | N/A (to be added) |
| switch-visual-theme | settings-theme | N/A (to be added) | N/A (to be added) |

## Diagrams

| Use case | Diagram | Source | Preview |
|---------|---------|--------|---------|
| switch-language | locale-resolution-flow | N/A (to be added) | N/A (to be added) |
| switch-visual-theme | theme-resolution-flow | N/A (to be added) | N/A (to be added) |

---
