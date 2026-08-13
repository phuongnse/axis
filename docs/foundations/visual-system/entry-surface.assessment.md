# Entry Surface Standards Assessment

> **Navigation**: [docs/foundations/visual-system/axis-visual-system.md](./axis-visual-system.md) · [docs/foundations/visual-system/axis-visual-system.evidence.md](./axis-visual-system.evidence.md) · [docs/playbooks/design-gate.md](../../playbooks/design-gate.md)

## Decision and scope

This is the criterion-level technical assessment for the complete `entry-surface` review unit. The evaluated page/process covers the focused public Entry layout and Card anatomy plus all six registered consumers: registration, sign-in, email confirmation, email verification, session unavailable, and invitation acceptance. It includes their applicable initial, form, validation, pending, warning, error, retry, rate-limited, success, wrong-account, and escape-navigation states.

The assessment does not absorb authentication/session protocol correctness, authorization, invitation secrecy or persistence, email delivery, backend correctness, route-guard correctness, legal-document content, or product-wide certification. Those remain owned by the Register User, Sign In User, and Accept Workspace Invitation use cases and the enterprise production baseline. Theme-linked technical evidence and project-owner review are complete for the declared Entry Surface.

Normative and informative references:

- [WCAG 2.2 Recommendation](https://www.w3.org/TR/WCAG22/), evaluated at Levels A and AA for the complete scoped page/process and its responsive variations.
- [WCAG-EM](https://www.w3.org/WAI/test-evaluate/conformance/wcag-em/) for evaluation scope and representative-state discipline.
- [Understanding Reflow](https://www.w3.org/WAI/WCAG22/Understanding/reflow.html), [Resize Text](https://www.w3.org/WAI/WCAG22/Understanding/resize-text.html), [Text Spacing](https://www.w3.org/WAI/WCAG22/Understanding/text-spacing.html), [Target Size](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html), [Focus Not Obscured](https://www.w3.org/WAI/WCAG22/Understanding/focus-not-obscured-minimum.html), and [Accessible Authentication](https://www.w3.org/WAI/WCAG22/Understanding/accessible-authentication-minimum.html) for the exercised boundary conditions.
- [ISO 9241-110:2020](https://www.iso.org/standard/75258.html) for interaction principles and [ISO 9241-210:2019](https://www.iso.org/standard/77520.html) for human-centred design throughout the lifecycle. This project mapping is not an ISO certification claim.

## Method and representative matrix

| Dimension | Evaluated evidence |
|---|---|
| Page/process | Registration; sign-in including unverified/resend and authorization-request failure; confirmation and resend states; verify loading/success/expired/used/invalid/rate-limited states; session-unavailable retry; invitation guest/wrong-account/review/success/invalid/error states |
| Viewports | 1280×1100 desktop, 390×844 compact, 320×900 reflow boundary with natural document scrolling for tall content |
| Appearance | Light and dark; deterministic reduced-motion media; rest and keyboard-focus states; semantic information, success, warning, destructive, pending, and disabled component states |
| Content | Canonical EN plus VI; registration as the maximal visual consumer; long localized consent/legal copy; WCAG text-spacing overrides |
| Input/semantics | Pointer and keyboard journeys; one `main`; one localized `h1`; labels, descriptions, errors, live/status feedback, native links, form fields, autocomplete purposes, and named controls |
| Visual | Accepted light/dark × desktop/compact captures plus a VI 320 CSS-pixel text-spacing/reflow capture |
| Deterministic checks | Typed owner/slot contract; six-consumer registry; component state evidence; browser geometry, internal/document overflow, contrast, focus, accessibility-tree, runtime-error, theme, locale, and scroll assertions; clean-cutover sweep |

Automation is supporting evidence, not the conformance or perceptual decision. The browser contrast check enumerates visible registration text in both themes and applies the WCAG relative-luminance threshold. Reflow evidence uses a 320 CSS-pixel viewport, the W3C equivalent boundary for 1280 CSS pixels at 400%, and separately applies the specified letter, word, and line spacing. It also checks both document and Entry Card internal overflow. Registration is visually representative because it exercises every owner region and the densest content; focused component and journey evidence covers the additional consumer states.

## WCAG 2.2 A/AA applicability

`Pass` means no failure was found within the declared page/process and current representative matrix. `N/A` means the scoped page/process contains no content or operation to which the criterion applies. These accepted findings apply to the scoped surface, not as a product-wide certification claim.

| Criterion | Result | Scoped evidence or rationale |
|---|---|---|
| 1.1.1 Non-text Content | Pass | The Axis mark is decorative/empty-alt; action and status icons are hidden from the accessibility tree and retain adjacent live text. |
| 1.2.1 Audio-only and Video-only (Prerecorded) | N/A | No prerecorded audio or video. |
| 1.2.2 Captions (Prerecorded) | N/A | No prerecorded synchronized media. |
| 1.2.3 Audio Description or Media Alternative (Prerecorded) | N/A | No prerecorded synchronized media. |
| 1.2.4 Captions (Live) | N/A | No live audio. |
| 1.2.5 Audio Description (Prerecorded) | N/A | No prerecorded video. |
| 1.3.1 Info and Relationships | Pass | One main landmark, one `h1`, native forms/links, associated labels, descriptions, errors, groups, invitation facts, and status roles preserve programmatic relationships. |
| 1.3.2 Meaningful Sequence | Pass | Utilities precede main content; Card title, banner/content, and footer preserve DOM and visual order across consumers and responsive modes. |
| 1.3.3 Sensory Characteristics | N/A | No instruction relies on shape, position, sound, or sensory location alone. |
| 1.3.4 Orientation | Pass | No orientation lock; compact and desktop reflow are exercised. |
| 1.3.5 Identify Input Purpose | Pass | Registration and sign-in fields declare supported `name`, `email`, `new-password`, and `current-password` autocomplete purposes. |
| 1.4.1 Use of Color | Pass | Error, warning, success, pending, checked, disabled, and focus states retain text, role, icon, boundary, or native state in addition to color. |
| 1.4.2 Audio Control | N/A | No audio. |
| 1.4.3 Contrast (Minimum) | Pass | Browser enumeration checks all visible representative text at >=4.5:1 in light and dark; semantic theme roles govern other consumer states. |
| 1.4.4 Resize Text | Pass | The 320 CSS-pixel equivalent boundary keeps the complete form readable and scrollable without clipping. |
| 1.4.5 Images of Text | Pass | No image of text; the product mark is decorative beside live headings. |
| 1.4.10 Reflow | Pass | The VI 320 CSS-pixel matrix has no document or Card horizontal overflow; long content uses natural document vertical scrolling. |
| 1.4.11 Non-text Contrast | Pass | Input, checkbox, Card, action, semantic notice, and focus boundaries use canonical theme pairs in both themes. |
| 1.4.12 Text Spacing | Pass | The VI matrix applies WCAG letter/word/line spacing, then rechecks internal/document overflow, targets, contrast, focus, and the accepted capture. |
| 1.4.13 Content on Hover or Focus | Pass | Preferences is dismissible with Escape and is the only information-bearing popup; no required information exists only on hover. |
| 2.1.1 Keyboard | Pass | Native fields, checkbox, buttons, links, and Preferences are keyboard operable; focus evidence covers form and consent controls. |
| 2.1.2 No Keyboard Trap | Pass | The Entry Card has no focus trap; Preferences opens and closes with standard popover keyboard behavior. |
| 2.1.4 Character Key Shortcuts | N/A | No single-character shortcut. |
| 2.2.1 Timing Adjustable | Pass | Verification success offers an immediate continuation before the bounded automatic handoff; invitation expiry is authoritative domain state rather than a client interaction time limit. |
| 2.2.2 Pause, Stop, Hide | N/A | No moving, blinking, scrolling, or auto-updating content requires a pause control. |
| 2.3.1 Three Flashes or Below Threshold | Pass | No flashing content; reduced-motion mode is exercised. |
| 2.4.1 Bypass Blocks | Pass | A single programmatic `main` landmark bypasses the repeated public utility boundary. |
| 2.4.2 Page Titled | Pass | Browser evidence asserts the non-empty `Axis Platform` document title. |
| 2.4.3 Focus Order | Pass | Focus follows Preferences, heading-adjacent form controls, actions, then footer/escape links without responsive reordering. |
| 2.4.4 Link Purpose (In Context) | Pass | Sign-in, registration, legal, privacy, retry/escape, and invitation actions have explicit localized purpose. |
| 2.4.5 Multiple Ways | N/A | Each Entry route is a step in an authentication/invitation process rather than a destination corpus requiring a second discovery method. |
| 2.4.6 Headings and Labels | Pass | Every state has one descriptive localized heading; fields and actions have visible or explicit accessible labels. |
| 2.4.7 Focus Visible | Pass | Browser evidence verifies canonical focus-visible treatment on a form field and the consent checkbox. |
| 2.4.11 Focus Not Obscured (Minimum) | Pass | Focused controls are asserted in the viewport; natural document scrolling and non-sticky regions do not cover them. |
| 2.5.1 Pointer Gestures | N/A | No multipoint or path-based gesture. |
| 2.5.2 Pointer Cancellation | Pass | Actions use standard link/button/checkbox activation; no down-event-only behavior. |
| 2.5.3 Label in Name | Pass | Visible field, action, link, and Preferences text is present in each accessible name. |
| 2.5.4 Motion Actuation | N/A | No device/user motion input. |
| 2.5.7 Dragging Movements | N/A | No dragging operation. |
| 2.5.8 Target Size (Minimum) | Pass | Owner geometry asserts 44 CSS-pixel compact and 32 CSS-pixel desktop inputs/actions/consent labels; checkbox labeling expands the activation proxy, and inline legal/footer links use the inline-target exception. |
| 3.1.1 Language of Page | Pass | Browser evidence asserts `lang=en` and `lang=vi` in their respective modes. |
| 3.1.2 Language of Parts | N/A | Each evaluated page is rendered in one selected language; product and organization names do not require language-part overrides. |
| 3.2.1 On Focus | Pass | Focusing fields, links, checkbox, and Preferences does not navigate or mutate state. |
| 3.2.2 On Input | Pass | Data entry and theme/language choices do not submit or navigate without the user action associated with that control. |
| 3.2.3 Consistent Navigation | Pass | Preferences and consumer escape links keep stable placement and meaning throughout Entry states. |
| 3.2.4 Consistent Identification | Pass | Equivalent fields, primary actions, notices, retry actions, and escape links retain the same shared mappings. |
| 3.2.6 Consistent Help | N/A | No help mechanism is present in the scoped process. |
| 3.3.1 Error Identification | Pass | Client/server/auth/invitation failures use explicit localized text, field association where applicable, and semantic notice roles. |
| 3.3.2 Labels or Instructions | Pass | Every input has a label, purpose/help text, autocomplete where applicable, and password criteria or confirmation guidance. |
| 3.3.3 Error Suggestion | Pass | Validation identifies correction requirements; recoverable session, resend, wrong-account, and invitation states expose an explicit next action. |
| 3.3.4 Error Prevention (Legal, Financial, Data) | Pass | Registration consent and entered account data remain visible/correctable before explicit submission; validation blocks incomplete or inconsistent commitments. |
| 3.3.7 Redundant Entry | Pass | Previously entered non-secret values persist through correction; password confirmation is an essential security/error-prevention confirmation. |
| 3.3.8 Accessible Authentication (Minimum) | Pass | Sign-in permits paste, password managers, autocomplete, and plain text entry without a cognitive-function test; no CAPTCHA or transcription puzzle is present. |
| 4.1.2 Name, Role, Value | Pass | Native controls expose names and checked, invalid, required, disabled, expanded, busy, current, and described relationships; component and accessibility-tree evidence assert the contract. |
| 4.1.3 Status Messages | Pass | Pending, resend, verification, session, registration, and invitation feedback uses status/live or alert semantics without requiring focus transfer. |

## Interaction and human-centred evaluation

| Principle | Current technical assessment |
|---|---|
| Suitability for the task | The surface contains orientation, only the fields/facts needed by the current entry step, one dominant next action, and a bounded escape/recovery path. |
| Self-descriptiveness | A localized title, labels, help text, password criteria, semantic notices, pending labels, and contextual footer explain the current state and next action. |
| Conformity with expectations | Native forms, autocomplete, links, checkbox consent, explicit submit, document scrolling, and Preferences follow familiar web behavior. |
| Learnability | The same title/content/action/footer anatomy and shared feedback mappings persist across registration, sign-in, verification, session recovery, and invitation states. |
| Controllability | Submission and navigation require explicit activation; pending locks only the authoritative action; retry, resend, switch-account, and escape paths return control. |
| Use-error robustness | Field-level validation preserves entered values, authentication fails closed, invitation states avoid disclosing unsupported facts, and recoverable failures expose one relevant action. |
| User engagement | Calm neutral hierarchy, one branded primary action, restrained semantic feedback, compact card width, and stable whitespace keep attention on the entry task; project-owner review accepted the captures. |

The human-centred lifecycle evidence evaluates one coherent public-entry boundary and exercises adverse locale, text spacing, content height, width, theme, motion, form error, remote failure, wrong-account, pending, and recovery conditions. The technical evaluation and project-owner acceptance are complete after the theme change.

## Ownership and retirement

- `EntrySurface` is the only shared focused-entry Card owner and requires one of the six finite `entry-surface` ids.
- `EntryLayout` is the only public-entry document-height, utility, main, width, gutter, and scroll owner.
- Owner-provided `EntryInput`, `EntryAction`, `EntryActionLink`, `EntryAsyncAction`, `EntryConsentCheckbox`, and `EntryConsentLabel` map reusable control density and first-line consent alignment without feature style escape hatches.
- `EmailConfirmationPage`, `AcceptWorkspaceInvitationPage`, `RegisterPage`, `SessionUnavailablePage`, `SignInPage`, and `VerifyEmailPage` are the six active consumers, and the real-symbol registry binds each consumer to the owner at compile time.
- Consumer hooks and use cases retain API, security, route, state, and recovery semantics; consuming leaf slots does not transfer those responsibilities to the surface owner.
- The supported path contains no parallel public-entry layout, alternate Card anatomy, compatibility alias, legacy wrapper, or fallback composition. The retirement sweep is `rg -n "EntrySurface|entry-surface|data-slot=\"entry-layout\"|data-slot=\"entry-surface\"" frontend/src frontend/tests frontend/e2e` and must be reviewed with the typed registry evidence.

## Review disposition

Technical assessment: **complete for the declared Entry Surface**. Lifecycle disposition: **enforced**. Five accepted perceptual artifacts and all 18 requirement traces are current; typed consumer ownership, first-line consent-alignment regression evidence, and retirement proof remain current.
