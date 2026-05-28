# Use case — Switch visual theme (light / dark / system)

> **Navigation**: [← Identity Access](./README.md) · [Use cases index](./README.md#use-cases)

## Purpose

Switch the visual theme between light, dark, and system so that I can choose a comfortable theme and preserve readable contrast.

## Primary actor

- Authenticated user

## Trigger

- User opens the theme selector in the header (or settings) and picks a mode.

## Main flow

1. User opens the theme selector.
2. User selects `light`, `dark`, or `system`.
3. App updates the active theme immediately without a full reload.
4. App persists the selected mode and applies it on next load (resolving `system` from the OS preference).

## Alternate / error flows

- Selected mode is `system` → app resolves the active theme from OS `prefers-color-scheme`.
- Theme preference storage unavailable or corrupted → app defaults to `system`.
- Theme changes during usage → app keeps the current route and in-progress interaction.

## Context

Define user-facing localization and visual theme behavior for the SPA so each use case has explicit UI artifacts, flow behavior, acceptance criteria, and implementation status.

## Acceptance Criteria

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

*Deferred capabilities*
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

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
