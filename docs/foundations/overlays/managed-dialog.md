# Managed Dialog

> **Navigation**: [docs/foundations/overlays/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Provide one app-scoped managed-window workspace that keeps multiple overlapping windows expanded or minimized while preserving mounted consumer state and consumer-owned lifecycle decisions.

## Consumers

- Authenticated product workflows that need an independent, app-scoped work surface.

## Activation

- A consumer requests a managed window for a stable workflow and resource identity, or an authenticated deep link supplies a launch intent.

## Guarantees

- Resolves stable workflow and resource identities so an existing window focuses or restores and an identity replacement preserves its mounted consumer state.
- Keeps multiple windows independently mounted, overlapping, activatable, and available through a dock and switcher.
- Leaves the authenticated route visible and pointer-interactive outside expanded window rectangles.
- Provides responsive geometry, focus, and accessible controls within the authenticated work area.
- Preserves consumer state through minimize and authenticated navigation, then clears the workspace on Workspace or session-authority transition, teardown, or reload.

## Alternate / error flows

- Existing identity: opening the same workflow and resource does not create a duplicate; it restores a minimized window or focuses an expanded one.
- Identity replacement: a consumer may replace a stable identity after its resource identity becomes available; the replacement preserves the mounted renderer and subsequent launches deduplicate against the replacement identity.
- Compact viewport: expanded windows use fullscreen, dragging and resizing are unavailable, the dock tray exposes one compact item plus `+N`, and no surface overlaps the footer or creates document overflow.
- Explicit fullscreen workflow: a workflow that requires the maximum work area declares fullscreen as its initial-size policy and retains the centered windowed rectangle as its restore size.
- Viewport, navigation, or footer change: each stored rectangle is clamped to the current authenticated work area while preserving a valid restore snapshot.
- Busy operation: close controls are disabled and Escape cannot bypass the consumer decision.
- Unsaved changes: minimize, restore, activation, and authenticated navigation preserve the draft; close uses the consumer-owned compact confirmation alert.
- Missing renderer or unavailable resource: the affected window shows a localized recoverable fallback with a safe close action without failing `AppShell` or sibling windows.
- Long-running session: the manager may normalize z-order values without changing the relative window order.

## Acceptance Criteria

- **AC-001** One app-owned managed-window host owns window descriptors, activation, geometry, dock tray, switcher, and renderer lifecycle without changing compact overlay behavior owned elsewhere.
- **AC-002** Stable workflow and resource identity deduplicate open requests; an existing minimized window restores and an existing expanded window focuses without changing another window's title, content, draft, or geometry. A stable identity replacement preserves the mounted consumer state and makes the replacement identity authoritative for later launches.
- **AC-003** Multiple expanded windows remain visible, overlapping, and pointer-activatable; one active window owns the highest z-order, keyboard focus trap, and Escape handling, while authenticated route content remains visible and pointer-interactive outside window rectangles.
- **AC-004** Window descriptors keep presentation metadata while mounted consumer renderers retain their own state, dirty state, and busy state across minimize and authenticated navigation.
- **AC-005** Desktop windows open at the centered windowed 50%-by-75% preset by default, while compact viewports and workflows with an explicit fullscreen policy open fullscreen with the windowed rectangle retained as their restore size. Runtime content overflow scrolls within the body and never changes the window mode. Desktop windows may resize below the windowed preset to an independent 35%-by-50% minimum, remain clamped while dragged or resized, maximize within the authenticated work area, restore the exact valid pre-maximize rectangle through either the size control or non-control header double-click, and reset to the configured initial-size policy. Explicit windowed and fullscreen policies remain available.
- **AC-006** The dock tray appears only for minimized windows, stays immediately above the app footer, orders recently minimized items right-to-left, keeps minimized renderers mounted, exposes older items through `+N`, and uses one visible dock plus `+N` on compact viewports. Transparent tray space does not intercept expanded-window interaction, and fullscreen body/footer content keeps an internal safe area for visible tray controls without reducing the fullscreen work-area rectangle.
- **AC-007** Reset, minimize, restore, maximize, restore-size, close, dock, overflow, and switcher controls have localized accessible names, observable keyboard focus, and correct state-dependent availability; dirty state has a non-color-only accessible indicator. Every expanded window has a stable footer with one explicit localized exit action: `Close` for non-editable states or `Cancel` for editable forms.
- **AC-008** Consumer-owned dirty and busy guards control destructive closure from the header, footer, dock, switcher, and Escape; closing one window does not unmount, reset, or dismiss sibling windows.
- **AC-009** `Windows (N)` sits at the leading edge of the active expanded window footer, lists every expanded and minimized window, identifies the active and dirty entries, and focuses or restores the selected stable identity. When every window is minimized and no expanded footer exists, the dock tray exposes the same switcher as the recovery path.
- **AC-010** Managed-window state survives navigation among authenticated modules for the current app session and is cleared on Workspace or session-authority transition, app teardown, or reload without profile or local-storage persistence.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | A managed window opens at the centered windowed 50%-by-75% preset by default regardless of runtime body overflow, opens fullscreen only for a compact viewport or explicit workflow policy, retains windowed restore geometry, and resets to that configured policy. Independent resize minimum, clamping, maximize/restore, header double-click, minimize, focus restoration, and header/footer close availability remain intact. | AC-005, AC-007, AC-008 | UI component test | Yes |
| AT-002 | UI component | The manager opens two overlapping windows, deduplicates stable identities, keeps one switcher in the active footer, activates by pointer and switcher, exposes a tray switcher when all windows are minimized, applies Escape only to the active window, and activates the correct sibling after close. | AC-001, AC-002, AC-003, AC-009 | UI component test | Yes |
| AT-003 | UI component | Independent consumer renderers retain independent query, draft, dirty, busy, title, and error state across minimize, restore, authenticated navigation, unavailable data, and sibling lifecycle changes; a Workspace or session-authority transition clears the managed workspace before any held session restore completes. Read-only and unavailable states expose footer `Close`; editable forms expose `Cancel` with product actions, and every exit path uses the same lifecycle guard. | AC-004, AC-007, AC-008, AC-010 | UI component test | Yes |
| AT-004 | Browser journey | A consumer launches another window from an exposed route action while one window remains expanded, overlaps and activates windows, maximizes and restores within the work area, docks items right-to-left above the footer, exposes `+N`, and remains keyboard-operable on desktop and compact viewports. | AC-002, AC-003, AC-005, AC-006, AC-007, AC-009 | Browser automation | Yes |
| AT-005 | Browser journey | A consumer draft survives minimize and authenticated route navigation, dirty closure remains guarded, the switcher restores the draft, and sign-out clears the workspace without layout overflow. | AC-004, AC-006, AC-008, AC-009, AC-010 | Browser automation | Yes |
| AT-006 | Static frontend | The app-owned host, renderer boundary, window descriptors, required consumer-supplied footer content, localized controls, and migrated consumers typecheck and lint without provider leakage or feature imports in foundation primitives. | AC-001, AC-007 | Frontend CI | Yes |

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
| Window header | Provide title, optional status, reset, minimize, maximize-or-restore-size, close, desktop drag, and non-control double-click maximize-or-restore-size. Align controls top-right with the title row on desktop. On compact viewports, preserve full-width identity and description content and center the controls in their own row between them. |
| Window body and footer | Keep consumer content in the only managed scroll region with a mandatory stable footer below it. Put the active window's `Windows (N)` switcher at the leading edge and the consumer action group at the trailing edge. Use `Close` for read-only, loading, error, and unavailable states; use `Cancel` plus product actions for editable forms; never show equivalent `Close` and `Cancel` actions together. Overflow remains internal and does not change the window mode; preserve an internal fullscreen safe area around visible tray controls. |
| Dock tray | Render only when minimized windows exist, align compact title bars right-to-left immediately above the app footer, expose overflow through `+N`, and expose `Windows (N)` only when no expanded footer exists. |
| Window switcher | Render exactly once: in the active expanded footer, or in the tray when all windows are minimized. List all stable identities with localized title plus active and dirty state, then focus or restore the selected item. |

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Contract | Done |
> | Frontend | Done |
> | Tests | Done |
>
> **Implemented:** The app shell hosts the descriptor manager, renderer boundary, overlapping window layer, responsive dock tray, and window switcher. Current consumers use stable launch identities while their mounted renderers retain query, form, dirty, busy, and error state across minimize and authenticated navigation. The foundation provides focus, drag, resize, maximize, exact restore, and header double-click behavior inside the authenticated work area.
>
> **Gaps vs spec:** None.
>
> **Deferred follow-ups:** Reload/session persistence, native pop-outs, tabbed docking, split panes, cross-device sync, and keyboard shortcuts remain out of scope.
>
> **Verification:** Acceptance proof is tracked in [docs/foundations/overlays/managed-dialog.evidence.md](./managed-dialog.evidence.md).
>
> **Decisions:** The window manager is an app-owned shared pattern hosted by the app shell. It keeps presentation lifecycle while consumer renderers remain mounted and own their query, form, mutation, dirty, busy, section-composition, and footer-action semantics. Stable workflow and resource identity deduplicate windows; an identity replacement keeps mounted consumer state intact. Managed windows use restrained elevation without a route scrim; only the active window traps focus, handles Escape, owns the highest z-order, and renders the shared switcher at the leading edge of its footer. Header controls center-align with the title row at the desktop right, while title and description use the same compact vertical rhythm as the shared page header; compact controls remain a centered row so variable content keeps the full width. Non-editable states use `Close`, editable forms use `Cancel` plus product actions, and the footer exit shares the header-close lifecycle. Desktop workflows default to the centered `windowed` preset; compact viewports use fullscreen. Docks align above the app footer with bounded visible items and overflow; the tray supplies the switcher only when all windows are minimized. Maximize fills the authenticated work area, and restore returns to the exact valid prior rectangle. Windows survive authenticated navigation but not Workspace or session-authority transitions, sign-out, or reload. [Detail Sections](../data-display/detail-sections.md) separately owns shared section composition.
