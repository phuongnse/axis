# F06 — Localization & Theming

[← Back to E02](../README.md)

---

## Description

Provide a consistent user experience across locales (English and Vietnamese) and visual themes (light, dark, system). This feature defines the frontend architecture, persistence behavior, and acceptance criteria for translation keys and theme switching.

---

## User Stories

### US-031 — Switch app language (English / Vietnamese)

**As a** user, **I want to** switch the app language between English and Vietnamese **so that** I can use Axis in my preferred language.

**Acceptance Criteria:**

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
> **Gaps vs spec:** feature not started.
>
> **Decisions:**
> - Translation must be key-based (`feature.section.key`) with English fallback.
> - New user-facing strings in migrated screens must come from locale files, not inline literals.

---

### US-032 — Support light / dark / system theme

**As a** user, **I want to** choose light, dark, or system theme **so that** the UI matches my preference and environment.

**Acceptance Criteria:**

*Happy path*
- [ ] Theme selector supports `light`, `dark`, and `system`.
- [ ] UI applies selected theme immediately without full-page reload.
- [ ] Theme preference persists across page refreshes and browser restarts.

*Validation & errors*
- [ ] When `system` is selected, app follows OS preference (`prefers-color-scheme`).
- [ ] If theme preference storage cannot be read, app defaults to `system`.

*Edge cases*
- [ ] Theme switch does not break contrast for critical feedback states (error/warning/success).
- [ ] Theme preference applies before first meaningful paint to avoid visible flash on load.

*Out of scope*
- Custom theme palette editor.

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
> **Gaps vs spec:** feature not started.
>
> **Decisions:**
> - Theme mode uses class-based strategy (`light` / `dark`) plus `system` resolution.
> - Theme toggle must be available in authenticated shell and settings.

---

### US-033 — Enforce i18n/theming implementation rules on new frontend work

**As a** product team, **I want** a clear implementation contract for i18n and theming **so that** future frontend features stay consistent.

**Acceptance Criteria:**

*Happy path*
- [ ] Frontend playbook documents i18n structure (`features`, locale files, key naming, fallback behavior).
- [ ] Frontend playbook documents theming structure (theme provider, toggle placement, persistence behavior).
- [ ] New/updated screens in scope of a PR do not introduce new hardcoded user-facing strings.

*Validation & errors*
- [ ] CI/frontend checks fail when formatting or lint rules are violated in i18n/theming files.

*Edge cases*
- [ ] Partial migrations are allowed per feature as long as implementation status callouts list remaining gaps.

*Out of scope*
- Automatic lint rule that blocks all hardcoded strings across legacy screens.

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
> **Gaps vs spec:** feature not started.
>
> **Decisions:**
> - Docs-first applies: establish this feature spec before implementation PRs.
