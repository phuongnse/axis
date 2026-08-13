# Managed Task Window Standards Assessment

> **Navigation**: [docs/foundations/overlays/managed-dialog.md](./managed-dialog.md) · [docs/foundations/overlays/managed-dialog.evidence.md](./managed-dialog.evidence.md) · [docs/foundations/visual-system/axis-visual-system.md](../visual-system/axis-visual-system.md) · [docs/playbooks/design-gate.md](../../playbooks/design-gate.md)

## Decision and scope

This is the criterion-level technical assessment for the complete `managed-task-window` review unit. The evaluated page/process includes the app-scoped manager, expanded window, header controls, body scroll owner, mandatory footer, overlapping-window activation, minimized dock, overflow switcher, stable identity, dirty/busy closure policy, authenticated-navigation continuity, and Workspace/session-authority teardown. It covers all six registered consumer families: Business Objects, Rules, Memberships, Product Roles, Service Identities, and Solution Delivery.

The assessment does not absorb Resource Workspace anatomy, feature form or API semantics, feature authorization, product validation, alert-dialog content, compact overlay primitives, persistence across reloads, or product-wide certification. Business Object Definition is the maximal visual representative because it exercises title status, description, tabs, editable form state, three footer actions, multiple overlapping identities, minimized overflow, and dirty lifecycle behavior. Solution Delivery supplies focused product-state evidence as a registered consumer. The five owner perceptual artifacts are accepted review evidence.

Normative and informative references:

- [WCAG 2.2 Recommendation](https://www.w3.org/TR/WCAG22/), evaluated at Levels A and AA for the declared managed-window process and responsive variations.
- [WCAG-EM](https://www.w3.org/WAI/test-evaluate/conformance/wcag-em/) for evaluation scope and representative-state discipline.
- [Understanding Reflow](https://www.w3.org/WAI/WCAG22/Understanding/reflow.html), [Text Spacing](https://www.w3.org/WAI/WCAG22/Understanding/text-spacing.html), [Target Size](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html), [Dragging Movements](https://www.w3.org/WAI/WCAG22/Understanding/dragging-movements.html), and [Focus Not Obscured](https://www.w3.org/WAI/WCAG22/Understanding/focus-not-obscured-minimum.html) for the exercised boundary conditions.
- [WAI-ARIA Authoring Practices: Dialog (Modal) Pattern](https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/) as informative focus and naming guidance. Managed windows intentionally use non-modal dialog semantics so pointer interaction outside their rectangles remains available.
- [ISO 9241-110:2020](https://www.iso.org/standard/75258.html) for interaction principles and [ISO 9241-210:2019](https://www.iso.org/standard/77520.html) for human-centred design throughout the lifecycle. This project mapping is not an ISO certification claim.

## Method and representative matrix

| Dimension | Evaluated evidence |
|---|---|
| Page/process | Stable identity open/dedup/replace; centered windowed and explicit fullscreen policies; active overlap; pointer activation; maximize/exact restore/header double-click/reset; minimize/restore; dock and `+N`; switcher; internal overflow; dirty/busy close; navigation continuity; authority teardown; unavailable renderer |
| Viewports | 1280×720 desktop, 390×844 compact, and 320×900 reflow boundary |
| Appearance | Light and dark; deterministic reduced-motion media; active, inactive, docked, dirty, busy, disabled, focus, and unavailable states |
| Content | Canonical EN plus VI; Business Object Definition as the maximal visual representative; long localized header/form copy and WCAG text-spacing overrides |
| Input/semantics | Pointer and keyboard journeys; non-modal named dialogs; active-window focus trap and Escape; localized named chrome; tabs; labelled form fields; one switcher in the active footer with an all-minimized tray fallback; current/dirty state; mandatory footer exit |
| Visual | Five accepted captures: light/dark × desktop/compact plus VI at the 320 CSS-pixel text-spacing/reflow boundary |
| Deterministic checks | Typed owner/id contract; five-family real-symbol registry; 50%×75% default and 35%×50% minimum geometry; maximize/restore; desktop top-right and compact centered header-control geometry; 32/44 CSS-pixel chrome/footer/tray targets; semantic layer materialization; tray topmost hit testing and footer collision; internal/document overflow; accessibility-tree snapshot; locale; reduced motion; exact screenshots; clean-cutover sweep |

Automation supports but does not make the perceptual or standards-review decision. Desktop captures show two active-capable overlapping windows without a route scrim, align chrome with the title row at the top-right, and place `Windows (N)` at the leading edge of the active footer. Compact captures preserve the footer placement, center the four chrome controls between full-width identity and description rows, and show one fullscreen window, one visible dock, `+N`, and a topmost tray in the reserved footer safe area. When every window is minimized, deterministic component evidence moves the single switcher to the tray so recovery remains available. Reflow evidence uses a 320 CSS-pixel viewport, the W3C equivalent boundary for 1280 CSS pixels at 400%, and separately applies the specified letter, word, and line spacing.

## WCAG 2.2 A/AA applicability

`Technical pass` means no failure was found within the declared page/process and current representative matrix. `N/A` means the scoped process contains no content or operation to which the criterion applies. These accepted findings apply only to the scoped review unit and are not a product-wide certification claim.

| Criterion | Result | Scoped evidence or rationale |
|---|---|---|
| 1.1.1 Non-text Content | Technical pass | Window, dock, switcher, async, and dirty icons are decorative or paired with localized accessible text; dirty state includes screen-reader text. |
| 1.2.1 Audio-only and Video-only (Prerecorded) | N/A | No prerecorded audio or video. |
| 1.2.2 Captions (Prerecorded) | N/A | No prerecorded synchronized media. |
| 1.2.3 Audio Description or Media Alternative (Prerecorded) | N/A | No prerecorded synchronized media. |
| 1.2.4 Captions (Live) | N/A | No live audio. |
| 1.2.5 Audio Description (Prerecorded) | N/A | No prerecorded video. |
| 1.3.1 Info and Relationships | Technical pass | Named dialog, title, description, tabs/panels, form relationships, footer actions, switcher menu, active/current state, and dirty/busy values are programmatic. |
| 1.3.2 Meaningful Sequence | Technical pass | Compact header identity, controls, and description; body; footer switcher; footer actions; and tray retain logical DOM order, while action-group reversal preserves task order. |
| 1.3.3 Sensory Characteristics | Technical pass | Active, dirty, docked, busy, and current states use text, state, icon, or accessible name rather than position or color alone. |
| 1.3.4 Orientation | Technical pass | No orientation lock; compact and desktop layouts are exercised. |
| 1.3.5 Identify Input Purpose | N/A | The representative fields do not collect personal information covered by this criterion. |
| 1.4.1 Use of Color | Technical pass | Active, dirty, disabled, status, tab, focus, and error states retain non-color indicators. |
| 1.4.2 Audio Control | N/A | No audio. |
| 1.4.3 Contrast (Minimum) | Technical pass | The browser matrix enumerates visible title, description, form, tab, footer, dock, menu, and switcher text and requires at least 4.5:1 in both themes. |
| 1.4.4 Resize Text | Technical pass | The 320 CSS-pixel boundary keeps the complete process readable and internally scrollable without clipping. |
| 1.4.5 Images of Text | Technical pass | No image of text. |
| 1.4.10 Reflow | Technical pass | Compact windows fill only the authenticated work area; body scroll remains internal; document width and height ownership do not overflow. |
| 1.4.11 Non-text Contrast | Technical pass | Runtime measurement requires at least 3:1 for editable-field boundaries, the settled keyboard-focus boundary, header controls, dock controls, switcher, and tray affordances in both themes. The theme gate independently rejects canonical input boundaries below 3:1 on base, card, or popover canvases; resting text-entry and outline-action fills inherit the managed canvas so boundary contrast does not become false active-state emphasis. |
| 1.4.12 Text Spacing | Technical pass | The VI matrix applies WCAG spacing values, then rechecks internal/document overflow, target geometry, tray layering, and the accepted capture. |
| 1.4.13 Content on Hover or Focus | Technical pass | No required content is hover-only; switcher/dropdown content is dismissible and focus-managed. |
| 2.1.1 Keyboard | Technical pass | Header/footer controls, tabs, fields, switcher, dock restore/close, confirmations, and product actions are keyboard operable; active-window focus is contained. |
| 2.1.2 No Keyboard Trap | Technical pass | Native Tab order advances through rendered controls and wraps only at the active expanded window boundaries; CSS-hidden descendants are excluded, and minimize or guarded close returns access to the route and prior focus. |
| 2.1.4 Character Key Shortcuts | N/A | No single-character shortcut. |
| 2.2.1 Timing Adjustable | N/A | No managed-window interaction has a user-facing time limit. |
| 2.2.2 Pause, Stop, Hide | N/A | No moving, blinking, scrolling, or auto-updating content requires a pause control. |
| 2.3.1 Three Flashes or Below Threshold | Technical pass | No flashing content; reduced-motion behavior is exercised. |
| 2.4.1 Bypass Blocks | Technical pass | The containing Authenticated Frame owns the `main` landmark; managed windows are named process overlays rather than a second document hierarchy. |
| 2.4.2 Page Titled | Technical pass | The containing application supplies the non-empty `Axis Platform` document title. |
| 2.4.3 Focus Order | Technical pass | Active-window header, section controls, body content, footer switcher, footer actions, and guarded confirmations follow DOM order across layout families. |
| 2.4.4 Link Purpose (In Context) | Technical pass | Route links stay owned by the visible frame; scoped window operations use explicit localized button names. |
| 2.4.5 Multiple Ways | N/A | A managed window is a task process, not a destination corpus requiring a second discovery method. |
| 2.4.6 Headings and Labels | Technical pass | Each window has a descriptive title; form, tab, chrome, dock, switcher, and footer controls have descriptive names. |
| 2.4.7 Focus Visible | Technical pass | Canonical focus-visible treatment applies to window, footer, dock, switcher, tab, and consumer controls. |
| 2.4.11 Focus Not Obscured (Minimum) | Technical pass | The active window owns focus; fullscreen footer actions remain above the verified topmost tray; internal body scrolling keeps focused content revealable. |
| 2.5.1 Pointer Gestures | N/A | No multipoint or path-based gesture is required. |
| 2.5.2 Pointer Cancellation | Technical pass | Buttons, tabs, fields, menus, and drag/resize completion use standard activation or release behavior. |
| 2.5.3 Label in Name | Technical pass | Visible chrome, dock, switcher, tab, footer, and product action text is included in each accessible name. |
| 2.5.4 Motion Actuation | N/A | No device/user motion input. |
| 2.5.7 Dragging Movements | Technical pass | Arbitrary drag/resize is optional; reset, maximize, restore-size, minimize, dock restore, switcher activation, and guarded close provide non-dragging ways to reveal, size, or leave the task. |
| 2.5.8 Target Size (Minimum) | Technical pass | Browser geometry asserts at least 44 CSS pixels on compact and 32 CSS pixels on desktop for managed chrome, footer, dock, and tray controls. |
| 3.1.1 Language of Page | Technical pass | Browser evidence asserts `lang=en` and `lang=vi` in their respective modes. |
| 3.1.2 Language of Parts | N/A | Each evaluated page uses one selected language; resource identifiers do not require part-language overrides. |
| 3.2.1 On Focus | Technical pass | Focusing window, tab, dock, switcher, or form controls does not mutate data or change context. |
| 3.2.2 On Input | Technical pass | Form input remains draft state; window lifecycle, navigation, and mutation require explicit actions. |
| 3.2.3 Consistent Navigation | Technical pass | Window chrome, footer exit, switcher, and dock retain stable order and meaning across all consumer families. |
| 3.2.4 Consistent Identification | Technical pass | Equivalent reset, minimize, maximize/restore, close/cancel, switcher, dock, dirty, and busy meanings use one shared mapping. |
| 3.2.6 Consistent Help | N/A | No help mechanism is present in the scoped process. |
| 3.3.1 Error Identification | Technical pass | Consumer errors remain contextual and the unavailable-renderer fallback is localized, announced, and safely closable. |
| 3.3.2 Labels or Instructions | Technical pass | Owner chrome and representative consumer fields/actions have visible or explicit labels and descriptions. |
| 3.3.3 Error Suggestion | Technical pass | Retryable consumer failures expose recovery; dirty close offers keep-editing/discard choices; unavailable renderers retain safe close. |
| 3.3.4 Error Prevention (Legal, Financial, Data) | Technical pass | Dirty editable state cannot be discarded through header, footer, dock, switcher, or Escape without consumer confirmation. |
| 3.3.7 Redundant Entry | Technical pass | Mounted consumer state survives minimize, activation, and authenticated navigation; no re-entry is required inside the current authority session. |
| 3.3.8 Accessible Authentication (Minimum) | N/A | Authentication is outside the authenticated managed-window scope. |
| 4.1.2 Name, Role, Value | Technical pass | Named non-modal dialogs and native controls expose active/current, selected, disabled, busy, invalid, expanded, and dirty states; accessibility-tree evidence asserts the representative contract. |
| 4.1.3 Status Messages | Technical pass | Consumer async/error feedback retains semantic status/alert behavior; dirty and busy state remain visible and programmatic without forced focus transfer. |

## Interaction and human-centred evaluation

These are current technical findings for the declared Managed Task Window review unit.

| Principle | Current technical assessment |
|---|---|
| Suitability for the task | Independent resource workflows remain mounted and recoverable while the route stays visible; one active window owns interaction without a global scrim. |
| Self-descriptiveness | Localized title, description, status, sections, chrome, mandatory exit, switcher count/state, dock title, dirty marker, and busy availability expose the current task and options. |
| Conformity with expectations | Windowed desktop geometry, fullscreen compact behavior, familiar minimize/maximize/reset/close actions, stable footer, and explicit discard confirmation match enterprise application conventions. |
| Learnability | Six consumer families reuse one header/body/footer, density, layer, dock, switcher, focus, and lifecycle grammar. |
| Controllability | Users can focus, minimize, restore, maximize, reset, switch, cancel, close, or keep editing; route state and sibling windows remain independent. |
| Use-error robustness | Stable identity deduplicates launches; consumer dirty/busy guards cover every exit path; unavailable renderers fail locally; authority transitions purge stale windows. |
| User engagement | Restrained elevation, no route scrim, standardized semantic state colors, predictable overlap, and compact safe-area handling keep attention on the active task; the accepted matrix records the current perceptual judgment. |

The human-centred lifecycle evidence evaluates one coherent shared boundary and exercises adverse width, locale, text spacing, theme, motion, overlap, minimized overflow, dirty/busy state, unavailable data, navigation, sign-out, and authority-transition conditions. Technical evidence and project-owner review are complete for this review unit.

## Ownership and retirement

- `ManagedDialog` is the only expanded-window anatomy and geometry owner and requires one of the six finite `managed-task-window` consumer ids or the host fallback id.
- `ManagedWindowProvider`, `ManagedWindowHost`, and `ManagedDialog` are the only app-scoped descriptor, identity, activation, dock, switcher placement, renderer, z-order, and teardown owners. `ManagedDialog` renders the active-footer switcher; `ManagedWindowHost` renders its all-minimized recovery fallback.
- `ManagedDialogTabs` owns the shared General/business/system section order and mounted panel behavior without owning product section meaning.
- Business Objects, Rules, Memberships, Product Roles, Service Identities, and Solution Delivery retain query, authorization, form, mutation, validation, dirty/busy decision, status, and localized product semantics. Shared `ManagedDialogAction` adapters own footer target geometry.
- The supported path contains no route-owned modal editor, parallel managed-window registry, persisted geometry/draft compatibility path, provider-specific window composition, or legacy fallback. The retirement sweep is `rg -n "ManagedDialog|ManagedWindowProvider|ManagedWindowHost|managed-window|managed-task-window" frontend/src frontend/tests frontend/e2e` and must be reviewed with the typed registry evidence.
- Compact command/dialog primitives remain separate owners and are not retired or absorbed. The unused Process Workbench path is retired rather than retained as a parallel owner or compatibility wrapper.

## Review disposition

Technical assessment: **accepted for the declared Managed Task Window**. Human-facing lifecycle roll-up: **Complete**. All eighteen current profile requirements are acceptance-traced with accepted perceptual evidence, criterion-level standards assessment, typed consumer ownership, rendered owner markers, and current retirement proof.
