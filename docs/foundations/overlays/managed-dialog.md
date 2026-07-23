# Managed Dialog

> **Navigation**: [docs/foundations/overlays/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Provide one app-scoped managed-window workspace for product details and forms that can keep multiple overlapping windows expanded or minimized while preserving mounted consumer state and product-owned lifecycle decisions.

## Primary actor

- Signed-in Axis Platform user viewing or editing product details in managed windows.

## Trigger

- A product surface requests a managed window for a stable workflow kind and resource identity.
- An authenticated deep link supplies a managed-window launch intent.

## Main flow

1. The app-level manager resolves a stable window identity from the workflow kind and resource identity; an existing window is restored or focused, while a new desktop window opens at the centered windowed preset occupying 50% of the authenticated work-area width and 75% of its height.
2. `AppShell` hosts the manager, renderer registry, expanded-window layer, dock tray, and window switcher; feature renderers remain mounted and own their TanStack Query, form, mutation, and draft state.
3. Multiple expanded windows may overlap. Pointer or focus activation brings one window to the front, moves keyboard focus into it, and makes only that window responsible for Escape and focus trapping while sibling windows remain pointer-activatable.
4. The authenticated route remains visible and pointer-interactive outside expanded window rectangles, so the user can continue the route workflow or launch another window without minimizing existing windows.
5. A desktop workflow opens windowed by default or fullscreen when its descriptor explicitly requires that mode. Compact viewports always use fullscreen. Content overflow scrolls within the body and never changes the window mode after render.
6. The user drags or resizes a desktop window within the work area, including below the initial windowed preset, maximizes or restores it through the size control or a non-control header double-click, and can reset it to its configured initial-size policy.
7. Minimize preserves the current geometry and mounted renderer, then places a compact title bar in the dock tray immediately above the footer. Docks order from newest at the right toward older items on the left; overflow moves older items into `+N`.
8. The always-available `Windows (N)` switcher lists expanded and minimized windows with active and dirty state. Selecting an item focuses it or restores it before focus.
9. Every expanded window keeps a stable action footer below its scrollable body. Read-only, loading, error, and unavailable states expose `Close`; editable forms expose `Cancel` plus their product-owned submit or lifecycle actions without duplicating `Close` and `Cancel` in the same footer.
10. Footer exit actions and the header close icon pass through the same owning-feature lifecycle. Dirty state requires explicit confirmation, busy state may reject closure, and accepted closure unmounts only that renderer and activates the most recent remaining expanded window.
11. Managed windows survive navigation between authenticated routes for the current app session. Sign-out, app teardown, or browser reload clears the workspace.

## Alternate / error flows

- Existing identity: opening the same workflow and resource does not create a duplicate; it restores a minimized window or focuses an expanded one.
- Create-to-record transition: Business Object create requests use the deterministic app-scoped `business-objects:create` identity and `create` resource key before persistence, so repeated launches restore or focus the same draft. The first successful create replaces that descriptor in place with `business-objects:{recordId}` in edit mode, preserving the mounted renderer while subsequent launches deduplicate against the persisted record identity.
- Compact viewport: expanded windows use fullscreen, dragging and resizing are unavailable, the dock tray exposes one compact item plus `+N`, and no surface overlaps the footer or creates document overflow.
- Explicit fullscreen workflow: a workflow that requires the maximum work area declares fullscreen as its initial-size policy and retains the centered windowed rectangle as its restore size.
- Viewport, navigation, or footer change: each stored rectangle is clamped to the current authenticated work area while preserving a valid restore snapshot.
- Busy operation: close controls are disabled and Escape cannot bypass the consumer decision.
- Unsaved changes: minimize, restore, activation, and authenticated navigation preserve the draft; close uses the consumer-owned compact confirmation alert.
- Missing renderer or unavailable record: the affected window shows a localized recoverable fallback with a safe close action without failing `AppShell` or sibling windows.
- Long-running session: the manager may normalize z-order values without changing the relative window order.

## Acceptance Criteria

- **AC-001** One app-owned managed-window provider and host under `AppShell` owns descriptors, activation, geometry, dock tray, switcher, and renderer lifecycle without changing the registry-owned compact dialog primitive.
- **AC-002** Stable workflow-kind and resource identity deduplicates open requests; an existing minimized window restores and an existing expanded window focuses without changing another window's title, content, draft, or geometry. Business Object creation uses one deterministic pre-persistence identity, then replaces that descriptor in place with the persisted record identity after the first successful create.
- **AC-003** Multiple expanded windows remain visible, overlapping, and pointer-activatable; one active window owns the highest z-order, keyboard focus trap, and Escape handling, while authenticated route content remains visible and pointer-interactive outside window rectangles.
- **AC-004** Window descriptors keep client-only presentation metadata in Zustand while mounted feature renderers retain server state, form state, mutations, dirty state, and busy state in their owning components across minimize and authenticated navigation.
- **AC-005** Desktop windows open at the centered windowed 50%-by-75% preset by default, while compact viewports and workflows with an explicit fullscreen policy open fullscreen with the windowed rectangle retained as their restore size. Runtime content overflow scrolls within the body and never changes the window mode. Desktop windows may resize below the windowed preset to an independent 35%-by-50% minimum, remain clamped while dragged or resized, maximize within the authenticated work area, restore the exact valid pre-maximize rectangle through either the size control or non-control header double-click, and reset to the configured initial-size policy. Explicit windowed and fullscreen policies remain available.
- **AC-006** The dock tray stays immediately above the app footer, orders recently minimized items right-to-left, keeps minimized renderers mounted, exposes older items through `+N`, and uses one visible dock plus `+N` on compact viewports. Transparent tray space does not intercept expanded-window interaction, and fullscreen body/footer content keeps an internal safe area for visible tray controls without reducing the fullscreen work-area rectangle.
- **AC-007** Reset, minimize, restore, maximize, restore-size, close, dock, overflow, and switcher controls have localized accessible names, observable keyboard focus, and correct state-dependent availability; dirty state has a non-color-only accessible indicator. Every expanded window has a stable footer with one explicit localized exit action: `Close` for non-editable states or `Cancel` for editable forms.
- **AC-008** Consumer-owned dirty and busy guards control destructive closure from the header, footer, dock, switcher, and Escape; closing one window does not unmount, reset, or dismiss sibling windows.
- **AC-009** `Windows (N)` lists every expanded and minimized window, identifies the active and dirty entries, and focuses or restores the selected stable identity.
- **AC-010** Managed-window state survives navigation among authenticated modules for the current app session and is cleared on sign-out, app teardown, or reload without profile or local-storage persistence.
- **AC-011** Rules create/edit/details and Business Object create/view/edit workflows consume the shared contract; command palettes, confirmation alerts, popovers, sheets, and drawers remain on their compact owning primitives.
- **AC-012** Base UI, React, Zustand, and `react-rnd` provide the required focus, state, drag, and resize primitives; the foundation does not add another window-manager dependency or expose provider-specific props to features.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | A managed window opens at the centered windowed 50%-by-75% preset by default regardless of runtime body overflow, opens fullscreen only for a compact viewport or explicit workflow policy, retains windowed restore geometry, and resets to that configured policy. Independent resize minimum, clamping, maximize/restore, header double-click, minimize, focus restoration, and header/footer close availability remain intact. | AC-005, AC-007, AC-008 | UI component test | Yes |
| AT-002 | UI component | The manager opens two overlapping windows, deduplicates stable identities, activates by pointer and switcher, applies Escape only to the active window, and activates the correct sibling after close. | AC-001, AC-002, AC-003, AC-009 | UI component test | Yes |
| AT-003 | UI component | Rules and Business Object renderers retain independent query, draft, dirty, busy, title, and error state across minimize, restore, authenticated navigation, unavailable data, and sibling lifecycle changes. Read-only and unavailable states expose footer `Close`; editable forms expose `Cancel` with their product actions, and every exit path uses the same lifecycle guard. | AC-004, AC-007, AC-008, AC-010, AC-011 | UI component test | Yes |
| AT-004 | Browser journey | Rules launches another window from an exposed route action while one window remains expanded, overlaps and activates windows, maximizes and restores within the work area, docks items right-to-left above the footer, exposes `+N`, and remains keyboard-operable on desktop and compact viewports. | AC-002, AC-003, AC-005, AC-006, AC-007, AC-009 | Browser automation | Yes |
| AT-005 | Browser journey | Business Object drafts survive minimize and authenticated route navigation, dirty closure remains guarded, the switcher restores the draft, and sign-out clears the workspace without layout overflow. | AC-004, AC-006, AC-008, AC-009, AC-010, AC-011 | Browser automation | Yes |
| AT-006 | Static frontend | The app-owned provider, renderer registry, Zustand descriptors, required consumer-supplied footer content, localized controls, and migrated consumers typecheck and lint without feature imports in registry primitives, provider leakage, new semantic tokens, or another window-manager dependency. | AC-001, AC-007, AC-011, AC-012 | Frontend CI | Yes |

## Out Of Scope

- Persisting windows, geometry, or drafts across browser reloads or signed-in sessions.
- Native operating-system windows, browser pop-outs, tabbed docking, split panes, or cross-device workspace sync.
- Keyboard shortcuts for the window switcher or window activation in the initial implementation.
- Product-specific forms, API mutations, authorization, validation, confirmation copy, or dirty-state policy.
- Replacing compact command-palette dialogs, alert dialogs, popovers, sheets, or drawers.

## Screen flow

| Surface | Required contract |
|---|---|
| Route beneath windows | Remain visible and pointer-interactive outside expanded window rectangles so route actions can launch or focus additional windows. |
| Expanded-window layer | Render every expanded window inside the authenticated work area with one active z-order and one active focus owner. |
| Window header | Provide title, optional status, reset, minimize, maximize-or-restore-size, close, desktop drag, and non-control double-click maximize-or-restore-size. Vertically center every title/accessory/description block with the window controls. |
| Window body and footer | Keep consumer content in the only managed scroll region with a mandatory stable action footer below it. Use `Close` for read-only, loading, error, and unavailable states; use `Cancel` plus product actions for editable forms; never show equivalent `Close` and `Cancel` actions together. Overflow remains internal and does not change the window mode; preserve an internal fullscreen safe area around visible tray controls. |
| Dock tray | Align compact title bars right-to-left immediately above the footer and expose overflow through `+N`. |
| Window switcher | List all stable identities with localized title plus active and dirty state, then focus or restore the selected item. |

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Contract | Done |
> | Frontend | Done |
> | Tests | Done |
>
> **Implemented:** `AppShell` owns the Zustand descriptor manager, static renderer registry, overlapping window layer, responsive dock tray, and window switcher. Rules and Business Objects use stable launch identities while their mounted renderers retain query, form, dirty, busy, and error state across minimize and authenticated navigation. Base UI and controlled `react-rnd` geometry provide focus, drag, resize, maximize, exact restore, and header double-click behavior inside the authenticated work area.
>
> **Gaps vs spec:** None.
>
> **Deferred follow-ups:** Reload/session persistence, native pop-outs, tabbed docking, split panes, cross-device sync, and keyboard shortcuts remain out of scope.
>
> **Verification:** Acceptance proof is tracked in [docs/foundations/overlays/managed-dialog.evidence.md](./managed-dialog.evidence.md).
>
> **Decisions:** The window manager is an app-owned shared pattern hosted by `AppShell`; Base UI primitives remain registry-owned and compact. Zustand stores plain client-only descriptors and presentation lifecycle, while feature renderers stay mounted and own query, form, mutation, dirty, busy, and footer action semantics. Stable workflow-kind and resource identity deduplicates windows. Managed windows use restrained elevation without a route scrim; authenticated route content remains pointer-interactive outside window rectangles, while only the active window traps focus, handles Escape, and owns the highest z-order. `ManagedDialog` requires consumer-supplied footer content and owns the stable footer wrapper; non-editable states use `Close`, editable forms use `Cancel` plus product actions, and the footer exit shares the header close lifecycle. Desktop workflows open at the centered `windowed` 50%-by-75% preset unless their descriptor explicitly requires fullscreen; compact viewports always use fullscreen, and runtime body overflow never changes the mode. Explicit `windowed` and `fullscreen` policies remain available, and every desktop window may resize below the initial preset to an independent 35%-by-50% minimum. Docks align right-to-left above `AppFooter` with `+N` overflow and no hard window cap. Maximize fills the authenticated work area and restores the exact valid prior rectangle. Windows survive authenticated navigation but not sign-out or reload. Base UI, React, Zustand, and `react-rnd` remain sufficient; no additional dependency is approved.
