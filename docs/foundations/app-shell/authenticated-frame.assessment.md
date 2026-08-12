# Authenticated Frame Standards Assessment

> **Navigation**: [docs/foundations/app-shell/app-frame.md](./app-frame.md) · [docs/foundations/app-shell/app-frame.evidence.md](./app-frame.evidence.md) · [docs/foundations/visual-system/axis-visual-system.md](../visual-system/axis-visual-system.md) · [docs/playbooks/design-gate.md](../../playbooks/design-gate.md)

## Decision and scope

This is the criterion-level technical assessment for the complete `authenticated-frame` review unit. The evaluated page/process starts on the authenticated `/dashboard` page, whose main content intentionally remains empty, and covers the viewport shell, App Header outside the Account popup, module-navigation boundary, route-content/context-transition boundary, global managed-window and notification layers, and footer.

The assessment does not absorb Account popup anatomy, module-navigation taxonomy or destination behavior, managed-task-window anatomy, notification content, Dashboard content, route-specific content, profile behavior, sign-out/session correctness, backend correctness, or product-wide certification. Those are different owners. The current outcome is **accepted and enforced for this scoped surface**.

Normative and informative references:

- [WCAG 2.2 Recommendation](https://www.w3.org/TR/WCAG22/), evaluated at Levels A and AA for the complete scoped page/process and its responsive variations.
- [WCAG-EM](https://www.w3.org/WAI/test-evaluate/conformance/wcag-em/) for evaluation scope and representative-state discipline.
- [Understanding Reflow](https://www.w3.org/WAI/WCAG22/Understanding/reflow.html), [Resize Text](https://www.w3.org/WAI/WCAG22/Understanding/resize-text.html), [Text Spacing](https://www.w3.org/WAI/WCAG22/Understanding/text-spacing.html), [Target Size](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html), and [Focus Not Obscured](https://www.w3.org/WAI/WCAG22/Understanding/focus-not-obscured-minimum.html) for the exercised boundary conditions.
- [ISO 9241-110:2020](https://www.iso.org/standard/75258.html) for interaction principles and [ISO 9241-210:2019](https://www.iso.org/standard/77520.html) for human-centred design throughout the lifecycle. This project mapping is not an ISO certification claim.

## Method and representative matrix

| Dimension | Evaluated evidence |
|---|---|
| Page/process | Empty authenticated Dashboard; header orientation and Account trigger; visible module destinations; empty route work area; Workspace context transition/recovery boundary; managed-window/notification host boundaries; footer metadata |
| Viewports | 1280×720 desktop, 390×844 compact, 320×900 reflow boundary |
| Appearance | Light and dark; deterministic reduced-motion media; rest and keyboard-focus states; component evidence for pending, obscured, blocked, recovery, and persistent-layer states |
| Content | Canonical EN plus VI; long signed-in identity; WCAG text-spacing overrides; intentionally empty Dashboard main content |
| Input/semantics | Pointer and keyboard journeys, banner/navigation/main/contentinfo landmarks, named controls and destinations, `aria-current`, `aria-busy`, live context status, and browser accessibility-tree snapshot |
| Visual | Accepted light/dark × desktop/compact captures plus a VI 320 CSS-pixel text-spacing/reflow capture, all with Account closed |
| Deterministic checks | Component owner/slot contracts, browser geometry/overflow/contrast/runtime-error assertions, typecheck/lint/style policy, manifest trace policy, typed consumer inventory, and clean-cutover assessment |

Automation is supporting evidence, not the conformance or perceptual decision. The browser contrast check enumerates every visible Authenticated Frame text node in light and dark states and applies the WCAG relative-luminance threshold. Reflow evidence uses a 320 CSS-pixel viewport, the W3C equivalent boundary for 1280 CSS pixels at 400%, and separately applies the specified letter, word, and line spacing. It does not simulate browser zoom by changing the root font size. Account remains closed in accepted captures so its independently enforced popup is not counted as frame evidence.

## WCAG 2.2 A/AA applicability

`Pass` means no failure was found within the declared page/process and current representative matrix. `N/A` means the scoped page/process contains no content or operation to which the criterion applies. The acceptance decision applies only to this declared review unit.

| Criterion | Result | Scoped evidence or rationale |
|---|---|---|
| 1.1.1 Non-text Content | Pass | The Axis mark is decorative/empty-alt, destination icons are hidden from the accessibility tree, and their meaning remains in live page-context or link text. |
| 1.2.1 Audio-only and Video-only (Prerecorded) | N/A | No prerecorded audio or video. |
| 1.2.2 Captions (Prerecorded) | N/A | No prerecorded synchronized media. |
| 1.2.3 Audio Description or Media Alternative (Prerecorded) | N/A | No prerecorded synchronized media. |
| 1.2.4 Captions (Live) | N/A | No live audio. |
| 1.2.5 Audio Description (Prerecorded) | N/A | No prerecorded video. |
| 1.3.1 Info and Relationships | Pass | Browser and component evidence expose ordered banner, named module navigation, main, and contentinfo landmarks plus explicit route, context-transition, managed-window, and notification boundaries. |
| 1.3.2 Meaningful Sequence | Pass | The DOM and accessibility tree preserve header, navigation/main work area, global layers, then footer; compact presentation changes flow without changing meaning. |
| 1.3.3 Sensory Characteristics | N/A | No instruction relies on shape, position, sound, or sensory location alone. |
| 1.3.4 Orientation | Pass | No orientation lock; compact and desktop reflow are exercised. |
| 1.3.5 Identify Input Purpose | N/A | No personal-data input field exists in the closed-Account frame scope. |
| 1.4.1 Use of Color | Pass | Current destination, focus, busy, and boundary meaning retain semantic state, text, icons, or live status; color is supplementary. |
| 1.4.2 Audio Control | N/A | No audio. |
| 1.4.3 Contrast (Minimum) | Pass | Browser enumeration checks every visible frame text node at >=4.5:1 in light and dark. |
| 1.4.4 Resize Text | Pass | The frame remains usable at the 320 CSS-pixel equivalent boundary; text-spacing stress grows content without clipping or document overflow. |
| 1.4.5 Images of Text | Pass | No image of text; the product mark is decorative beside live page-context text. |
| 1.4.10 Reflow | Pass | The 320 CSS-pixel VI journey transforms the side rail into a horizontal navigation row, preserves the full main region, stacks footer metadata, and asserts no horizontal overflow or document scrolling. |
| 1.4.11 Non-text Contrast | Pass | Canonical boundary, current-state, icon, and focus roles come from the Axis theme and remain rendered in both themes; focused destinations and the Account trigger retain visible boundaries. |
| 1.4.12 Text Spacing | Pass | The VI journey applies WCAG letter/word/line spacing to the complete frame, then rechecks geometry, targets, contrast, overflow, document scroll, and the accepted capture. |
| 1.4.13 Content on Hover or Focus | N/A | No information-only hover/focus popup exists in the evaluated closed-Account frame. |
| 2.1.1 Keyboard | Pass | Module destinations and the Account trigger are native/accessible links or buttons; browser focus evidence operates the frame without pointer-only behavior. |
| 2.1.2 No Keyboard Trap | Pass | The closed-Account frame introduces no modal boundary or focus trap; global layer owners retain responsibility when opened. |
| 2.1.4 Character Key Shortcuts | N/A | No single-character shortcut. |
| 2.2.1 Timing Adjustable | N/A | No user-facing time limit. |
| 2.2.2 Pause, Stop, Hide | N/A | No moving, blinking, scrolling, or auto-updating content requiring a pause control. |
| 2.3.1 Three Flashes or Below Threshold | Pass | No flashing content; reduced-motion mode is exercised. |
| 2.4.1 Bypass Blocks | Pass | A single programmatic `main` landmark follows the banner/navigation boundary and permits repeated frame chrome to be bypassed by landmarks. |
| 2.4.2 Page Titled | Pass | Browser evidence asserts the non-empty `Axis Platform` document title. |
| 2.4.3 Focus Order | Pass | Focus follows visible header/navigation/main/footer order and remains coherent when navigation reflows from rail to row. |
| 2.4.4 Link Purpose (In Context) | Pass | Business objects and Rules have explicit destination names; product orientation remains live text rather than an unlabeled image link. |
| 2.4.5 Multiple Ways | N/A | The scoped empty Dashboard/frame is a shared process boundary, not a destination corpus requiring an additional discovery mechanism. |
| 2.4.6 Headings and Labels | Pass | Localized page context, navigation landmark, destinations, Account trigger, context status, and footer metadata use descriptive names. |
| 2.4.7 Focus Visible | Pass | Browser evidence focuses the Rules destination and verifies the canonical focus-visible boundary in both layout families. |
| 2.4.11 Focus Not Obscured (Minimum) | Pass | The focused destination is asserted in the viewport; header, footer, and empty main content do not overlap the active control. |
| 2.5.1 Pointer Gestures | N/A | No multipoint or path-based gesture. |
| 2.5.2 Pointer Cancellation | Pass | Frame actions use standard link/button activation; no down-event-only behavior. |
| 2.5.3 Label in Name | Pass | Visible destination labels are their accessible names; the Account trigger inherits the accepted context-bearing name contract from `account-surface`. |
| 2.5.4 Motion Actuation | N/A | No device/user motion input. |
| 2.5.7 Dragging Movements | N/A | No dragging operation in the frame. |
| 2.5.8 Target Size (Minimum) | Pass | Browser geometry asserts at least 44 CSS pixels in compact/reflow and at least 32 CSS pixels on desktop, both above the 24 CSS-pixel AA minimum. |
| 3.1.1 Language of Page | Pass | Browser evidence asserts `lang=en` and `lang=vi` in their respective modes. |
| 3.1.2 Language of Parts | N/A | Each evaluated page is rendered in one selected language; product names and user initials do not require a language-part override. |
| 3.2.1 On Focus | Pass | Focusing frame controls does not change route or context. |
| 3.2.2 On Input | Pass | Navigation and Account actions require explicit activation; context transition status does not trigger from focus alone. |
| 3.2.3 Consistent Navigation | Pass | Shared frame order and destination presentation remain stable across routes, themes, and responsive modes. |
| 3.2.4 Consistent Identification | Pass | Header, navigation destinations, Account trigger, work area, context status, and footer retain consistent semantic roles. |
| 3.2.6 Consistent Help | N/A | No help mechanism is present in the scoped page/process. |
| 3.3.1 Error Identification | Pass | Component/browser context-transition evidence preserves localized failure state and recovery ownership without removing the frame. |
| 3.3.2 Labels or Instructions | N/A | The frame contains no data-entry operation requiring instructions; all interactive destinations have explicit labels. |
| 3.3.3 Error Suggestion | Pass | Recoverable Workspace context failures retain the adapter's explicit retry path while the frame blocks stale route interaction. |
| 3.3.4 Error Prevention (Legal, Financial, Data) | N/A | No legal/financial commitment or user-controlled data mutation is completed by the frame. |
| 3.3.7 Redundant Entry | N/A | No information-entry flow. |
| 3.3.8 Accessible Authentication (Minimum) | N/A | Authentication is outside this authenticated frame. |
| 4.1.2 Name, Role, Value | Pass | Native landmarks/controls expose names and current, expanded, busy, blocked, and labelled relationships; component and accessibility-tree evidence assert the rendered contract. |
| 4.1.3 Status Messages | Pass | Workspace context transitions use a polite live surface and busy state without moving focus into a transient message. |

## Interaction and human-centred evaluation

| Principle | Current technical assessment |
|---|---|
| Suitability for the task | The frame contains only global orientation, discovery, route/context continuity, global layer hosts, and product metadata. Dashboard and subsystem workflow content remain outside the owner. |
| Self-descriptiveness | Ordered landmarks, live page context, named destinations, current state, stable metadata, and localized transition feedback identify the frame and available next actions. |
| Conformity with expectations | Native header/nav/main/footer landmarks, links/buttons, focus, responsive navigation, live status, and route continuity preserve familiar web application behavior. |
| Learnability | The same product/page/account orientation and destination order persists while the navigation changes from side rail to horizontal row. |
| Controllability | Navigation and Account entry require explicit activation; a context transition blocks only stale content while the authoritative Workspace is restored, then returns control. |
| Use-error robustness | The shell keeps global structure mounted during refresh/recovery, prevents interaction with stale route state, and separates retry/application logic from presentation slots. |
| User engagement | Calm neutral hierarchy, restrained boundaries, one accent focus treatment, and stable whitespace keep global chrome legible without competing with future route content. The accepted matrix records the current perceptual judgment. |

The human-centred lifecycle evidence evaluates one coherent frame boundary, intentionally keeps Dashboard content empty, and exercises adverse locale, content length, width, text spacing, theme, motion, and context-transition conditions. No technical or review gap remains in the accepted matrix.

## Ownership and retirement

- `AuthenticatedFrame` is the only shared viewport-shell owner and requires the typed `authenticated-frame` surface id.
- `AppShell` is the application adapter. It supplies header, navigation, route/context state, managed-window host, notifications, and footer through explicit owner slots; it does not reproduce frame anatomy.
- `src/routes/_authenticated.tsx` is the single active consumer, and the real-symbol registry binds that consumer to the owner at compile time.
- Account popup, module-navigation item anatomy, managed windows, and notifications retain independent owners; consuming their hosts is not ownership of their internal contracts.
- The supported path contains no parallel authenticated layout, route-local header/footer, alternate viewport scroll owner, compatibility alias, or fallback composition. The retirement sweep is `rg -n "AuthenticatedFrame|authenticated-frame|data-slot=\"authenticated-work-area\"|<AppShell" frontend/src frontend/tests frontend/e2e` and must be reviewed with the typed registry evidence.
- Dashboard route content remains empty and is not used as a visual-evidence host.

## Review disposition

Technical assessment: **accepted for the declared Authenticated Frame**. Lifecycle disposition: **enforced** with all current profile requirements covered, accepted perceptual evidence, typed consumer ownership, and current retirement proof. Any later change applies the profile invalidation map and reopens only the affected review unit.
