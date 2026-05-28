# Use case — Switch visual theme (light / dark / system)

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


**Purpose:** let users choose a comfortable visual theme with consistent contrast and persistence.  
**Primary actor:** authenticated user.  
**Trigger:** user changes theme mode in header/settings selector.

#### Main flow
1. User opens theme selector.
2. User selects `light`, `dark`, or `system`.
3. App updates active theme immediately without full reload.
4. App persists selected theme mode and applies it on next app load.

#### Alternate / error flows
- Selected mode is `system` → app resolves active theme from OS `prefers-color-scheme`.
- Theme preference storage unavailable/corrupted → app defaults to `system`.
- Theme changes during usage → app keeps current route and in-progress user interaction.

#### Acceptance Criteria

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
> **Gaps vs spec:** use case not started.
>
> **Decisions:**
> - Theme mode uses class-based strategy (`light` / `dark`) with `system` resolution.
> - Theme toggle must be available in authenticated shell and settings.

---

## Implementation rules (reference)

Use case behavior above is user-facing scope. Engineering rules remain in shared docs:
- [Frontend playbook](../../playbooks/frontend.md)
- [Agent checklist (Gate 0–3)](../../playbooks/agent-checklist.md)

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
