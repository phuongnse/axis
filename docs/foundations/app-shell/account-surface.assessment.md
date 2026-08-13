# Account Surface Standards Assessment

> **Navigation**: [docs/foundations/app-shell/app-frame.md](./app-frame.md) · [docs/foundations/app-shell/app-frame.evidence.md](./app-frame.evidence.md) · [docs/foundations/visual-system/axis-visual-system.md](../visual-system/axis-visual-system.md) · [docs/playbooks/design-gate.md](../../playbooks/design-gate.md)

## Decision and scope

This is the criterion-level technical assessment for the complete `account-surface` review unit. The evaluated page/process starts on the authenticated `/dashboard` page, whose main content intentionally remains empty, opens the Account trigger, orients the signed-in identity, exposes eligible Workspace choices, changes language/theme, presents pending/recovery state, and hands off create-Organization or sign-out commands to their owning workflows.

The assessment does not cover Account profile editing, the implementation behind create Organization or sign-out, route-specific content, other surfaces, backend/session correctness, or product-wide certification. Those are different owners. Theme-linked technical evidence and project-owner review are complete for this scoped surface. The accepted presentation keeps the identity icon top-aligned with the primary identity text, and that invariant is captured and mechanically verified.

Normative and informative references:

- [WCAG 2.2 Recommendation](https://www.w3.org/TR/WCAG22/), evaluated at Levels A and AA for the complete scoped page/process and its responsive variations.
- [WCAG-EM](https://www.w3.org/WAI/test-evaluate/conformance/wcag-em/) for evaluation scope and representative-state discipline.
- [Understanding Reflow](https://www.w3.org/WAI/WCAG22/Understanding/reflow.html), [Resize Text](https://www.w3.org/WAI/WCAG22/Understanding/resize-text.html), [Text Spacing](https://www.w3.org/WAI/WCAG22/Understanding/text-spacing.html), [Target Size](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html), and [Focus Not Obscured](https://www.w3.org/WAI/WCAG22/Understanding/focus-not-obscured-minimum.html) for the exercised boundary conditions.
- [ISO 9241-110:2020](https://www.iso.org/standard/75258.html) for interaction principles and [ISO 9241-210:2019](https://www.iso.org/standard/77520.html) for human-centred design throughout the lifecycle. This project mapping is not an ISO certification claim.

## Method and representative matrix

| Dimension | Evaluated evidence |
|---|---|
| Page/process | Empty authenticated Dashboard, Account trigger, identity, Workspace selection/create handoff, language/theme selection, retry/status, sign-out handoff |
| Viewports | 1280×720 desktop, 390×844 compact, 390×600 constrained height, 320×900 reflow boundary |
| Appearance | Light and dark; deterministic reduced-motion media; default, hover, current, pending, failure/retry, and keyboard-focus states |
| Content | Canonical EN plus VI; long human name, long email/domain, and long Workspace label; WCAG text-spacing overrides |
| Input/semantics | Pointer and keyboard journeys, named landmarks/regions/groups, `aria-current`, `aria-pressed`, `aria-busy`, live status, accessible-name composition, and browser accessibility-tree snapshot |
| Visual | Candidate light/dark × desktop/compact captures plus a VI 320 CSS-pixel text-spacing/reflow capture; identity avatar top-aligned with primary text |
| Deterministic checks | Component contracts, browser geometry/overflow/contrast/runtime-error assertions, typecheck/lint/style policy, manifest trace policy, and clean-cutover inventory |

Automation is supporting evidence, not the conformance decision. The browser contrast check enumerates every visible Account text node in light and dark states and applies the WCAG relative-luminance threshold. Reflow evidence uses a 320 CSS-pixel viewport, the W3C equivalent boundary for 1280 CSS pixels at 400%, and separately applies the specified letter, word, and line spacing. It does not simulate browser zoom by changing the root font size.

## WCAG 2.2 A/AA applicability

`Pass` means no failure was found within the declared page/process and current representative matrix. `N/A` means the scoped page/process contains no content or operation to which the criterion applies. The project-owner acceptance decision applies only to this declared review unit.

| Criterion | Result | Scoped evidence or rationale |
|---|---|---|
| 1.1.1 Non-text Content | Pass | Decorative icons/logo image are hidden or empty-alt; identity and action meaning retain text names. Component semantics and the accessibility-tree snapshot cover the rendered state. |
| 1.2.1 Audio-only and Video-only (Prerecorded) | N/A | No prerecorded audio or video. |
| 1.2.2 Captions (Prerecorded) | N/A | No prerecorded synchronized media. |
| 1.2.3 Audio Description or Media Alternative (Prerecorded) | N/A | No prerecorded synchronized media. |
| 1.2.4 Captions (Live) | N/A | No live audio. |
| 1.2.5 Audio Description (Prerecorded) | N/A | No prerecorded video. |
| 1.3.1 Info and Relationships | Pass | Header/main/footer landmarks, Account dialog, named identity/Workspace/preference regions, language/theme groups, current choice and status relationships are programmatic. |
| 1.3.2 Meaningful Sequence | Pass | DOM and accessibility-tree order is identity, Workspace, preferences, then sign-out; component tests assert the same order. |
| 1.3.3 Sensory Characteristics | N/A | No instruction relies on shape, position, sound, or sensory location alone. |
| 1.3.4 Orientation | Pass | No orientation lock; compact and desktop reflow are exercised. |
| 1.3.5 Identify Input Purpose | N/A | No personal-data input fields exist in scope. |
| 1.4.1 Use of Color | Pass | Current, pressed, pending, destructive, and error meaning retain text, icons, semantic state, or status messaging; color is supplementary. |
| 1.4.2 Audio Control | N/A | No audio. |
| 1.4.3 Contrast (Minimum) | Pass | Browser enumeration checks every visible Account text node at >=4.5:1 in light and dark. The check caught and caused correction of the prior light destructive-action value. |
| 1.4.4 Resize Text | Pass | Long text wraps, controls grow, and the complete surface remains operable at the 320 CSS-pixel equivalent boundary without clipping. |
| 1.4.5 Images of Text | Pass | No image of text; the brand image is decorative beside live page-context text. |
| 1.4.10 Reflow | Pass | 320 CSS-pixel VI journey asserts no page or surface horizontal overflow and no two-dimensional content dependency. The Account surface owns bounded vertical scrolling when height is constrained. |
| 1.4.11 Non-text Contrast | Pass | Canonical boundary, focus-ring, selected/current, icon, and control roles come from the Axis theme; state-difference and focus evidence exercise their rendered presence in both themes. |
| 1.4.12 Text Spacing | Pass | The 320 VI journey applies WCAG letter/word/line spacing, then rechecks wrapping, target geometry, surface overflow, document scroll, focus reachability, and the accepted capture. |
| 1.4.13 Content on Hover or Focus | N/A | No information-only hover/focus popup exists; the Account popup is explicitly invoked and dismissible. |
| 2.1.1 Keyboard | Pass | Trigger, choices, retry, create, and sign-out are native/accessible primitive buttons; keyboard journeys reach and operate the sequence. |
| 2.1.2 No Keyboard Trap | Pass | Escape dismisses the ordinary popup and focus can traverse to sign-out; the deliberate transition lock persists only while the authoritative operation is pending. |
| 2.1.4 Character Key Shortcuts | N/A | No single-character shortcut. |
| 2.2.1 Timing Adjustable | N/A | No user-facing time limit. |
| 2.2.2 Pause, Stop, Hide | N/A | No moving, blinking, scrolling, or auto-updating content that requires a pause control. |
| 2.3.1 Three Flashes or Below Threshold | Pass | No flashing content; reduced-motion mode is exercised. |
| 2.4.1 Bypass Blocks | Pass | The scoped page exposes a single programmatic `main` landmark between banner and contentinfo; repeated chrome can be navigated by landmarks. |
| 2.4.2 Page Titled | Pass | Browser evidence asserts the non-empty `Axis Platform` document title. |
| 2.4.3 Focus Order | Pass | Focus order follows the meaningful region/action order and reaches the terminal sign-out action even with a long Workspace list. |
| 2.4.4 Link Purpose (In Context) | Pass | The only scoped brand link is named by adjacent live page-context content; action controls use explicit localized names. |
| 2.4.5 Multiple Ways | N/A | The scoped Dashboard/Account task is a process step rather than a destination set requiring multiple discovery mechanisms. |
| 2.4.6 Headings and Labels | Pass | Account, Workspace, Preferences, Language, Theme, recovery, and actions use descriptive localized labels. |
| 2.4.7 Focus Visible | Pass | Shared native primitives expose the canonical focus-visible boundary/ring; keyboard browser journeys retain visible focused controls. |
| 2.4.11 Focus Not Obscured (Minimum) | Pass | Constrained-height and 320 journeys focus sign-out and assert it remains in the viewport while the Account surface, not the document, owns vertical scroll. |
| 2.5.1 Pointer Gestures | N/A | No multipoint or path-based gesture. |
| 2.5.2 Pointer Cancellation | Pass | Actions use standard button activation; no down-event-only behavior. |
| 2.5.3 Label in Name | Pass | The Account trigger accessible name includes the visible Workspace/person context; other visible control labels are their accessible names. |
| 2.5.4 Motion Actuation | N/A | No device/user motion input. |
| 2.5.7 Dragging Movements | N/A | No dragging operation. |
| 2.5.8 Target Size (Minimum) | Pass | Browser geometry asserts at least 44 CSS pixels in compact/reflow and at least 32 CSS pixels on desktop, both above the 24 CSS-pixel AA minimum; component tests bind the owning density roles. |
| 3.1.1 Language of Page | Pass | Browser evidence asserts `lang=en` and `lang=vi` in their respective modes. |
| 3.1.2 Language of Parts | N/A | Each evaluated page is rendered in one selected language; names/email/brand identifiers do not require a language-part override. |
| 3.2.1 On Focus | Pass | Focusing controls does not change context. |
| 3.2.2 On Input | Pass | Choices change only after explicit activation; pending state is announced and the Account surface stays open through authoritative transitions. |
| 3.2.3 Consistent Navigation | Pass | Shared frame and Account ordering stay stable across routes and responsive modes. |
| 3.2.4 Consistent Identification | Pass | Workspace, preference, retry, create, and sign-out controls retain consistent names, roles, and icons. |
| 3.2.6 Consistent Help | N/A | No help mechanism is present in the scoped page/process. |
| 3.3.1 Error Identification | Pass | Workspace, preference, and sign-out failures render adjacent localized status notices and identify the failed operation. |
| 3.3.2 Labels or Instructions | Pass | Choice groups and actions have programmatic localized labels; no free-form input requires additional instructions. |
| 3.3.3 Error Suggestion | Pass | Recoverable Workspace/preference failures expose a named retry action; unrecoverable handoff errors remain explicitly identified. |
| 3.3.4 Error Prevention (Legal, Financial, Data) | N/A | No legal/financial commitment or user-controlled data mutation is completed in this surface. |
| 3.3.7 Redundant Entry | N/A | No information-entry flow. |
| 3.3.8 Accessible Authentication (Minimum) | N/A | Authentication is outside this authenticated surface. |
| 4.1.2 Name, Role, Value | Pass | Native/Base UI primitives expose dialog/button/group roles plus current, pressed, expanded, busy, disabled, and labelled relationships; component and browser semantic evidence assert them. |
| 4.1.3 Status Messages | Pass | Pending and recovery state uses `role=status`, live regions, or status notices without moving focus. |

## Interaction and human-centred evaluation

| Principle | Current assessment |
|---|---|
| Suitability for the task | The surface contains only identity orientation, Workspace context, preferences, recovery, create handoff, and sign-out handoff. Dashboard content and profile editing remain outside the owner. |
| Self-descriptiveness | Ordered named regions, explicit current state, stable labels, icons plus text, pending announcements, and adjacent recovery expose what the surface and each action do. |
| Conformity with expectations | Native/accessible button, toggle-group, popover, region, status, Escape, Tab, and focus conventions are preserved; destructive tone does not relocate the action. |
| Learnability | One leading scan axis and repeated option anatomy make Workspace, language, and theme choices predictable without instruction text. |
| Controllability | The user explicitly opens/closes the surface and activates choices; authoritative transitions lock dismissal only while context integrity requires it, then return control. |
| Use-error robustness | Current/pending choices are protected from duplicate activation; outcome-unknown, refresh, preference, and sign-out failures remain visible and expose recovery where safe. |
| User engagement | Calm neutral hierarchy, restrained depth, readable identity, localized copy, and consistent spacing support orientation without decorative competition; project-owner review accepted the scoped result. |

The human-centred lifecycle evidence evaluates one coherent Account surface boundary, keeps Dashboard content empty, exercises representative user tasks and adverse content/mode conditions, and requires the identity icon to remain top-aligned. Responsive overflow and contrast failures discovered by the evaluation were resolved, and the complete matrix was reverified and accepted.

## Ownership and retirement

- `AccountSurface` is the only shared owner and requires the typed `account-actions` id; the real-symbol registry binds that consumer to the owner.
- `AppHeader` is the application adapter and supplies typed identity, Workspace, preference, and sign-out models. Feature components do not inject generic JSX into Account anatomy.
- The supported path contains no parallel legacy Account menu, feature-local identity menu, typography wrapper, spacing wrapper, or alternative preference/Workspace composition. The retirement sweep is `rg -n "AccountSurface|account-actions|data-slot=\\\"account-surface\\\"|accountMenu" frontend/src frontend/tests frontend/e2e` and must be reviewed with the typed registry evidence.
- `OptionList` remains the shared semantic option-row owner. The Account change consumes it rather than copying its anatomy; no compatibility alias or fallback was introduced.
- Dashboard route content remains empty and is not used as a visual-evidence host.

## Review disposition

Technical assessment: **complete for the declared Account surface**. Lifecycle disposition: **enforced**. The revalidated theme matrix and criterion-level assessment are accepted; all 18 profile requirements, typed consumer ownership, and retirement proof are current.
