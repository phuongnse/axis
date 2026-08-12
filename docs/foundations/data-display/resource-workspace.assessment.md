# Resource Workspace Standards Assessment

> **Navigation**: [docs/foundations/data-display/collection-page.md](./collection-page.md) · [docs/foundations/data-display/collection-page.evidence.md](./collection-page.evidence.md) · [docs/foundations/visual-system/axis-visual-system.md](../visual-system/axis-visual-system.md) · [docs/playbooks/design-gate.md](../../playbooks/design-gate.md)

## Decision and scope

This is the criterion-level technical assessment for the complete `resource-workspace` review unit. The evaluated page/process includes the shared `ResourceWorkspace` page anatomy, its one primary `DataTable`, and all five registered consumers: Business Objects, Memberships, Product Roles, Rules, and Service Identities. It covers page orientation, collection status, search/filter/group/column controls where enabled, row and bulk selection where enabled, sort, paging, loading, empty, error/retry, ready, denied-action, and collection-preserving record-launch states.

The assessment does not absorb managed-task-window anatomy, record-editor content, feature authorization, API or persistence correctness, product validation, route-specific copy, Process Workbench, or product-wide certification. Managed windows remain closed in the accepted visual matrix. The current outcome is **accepted for the declared Resource Workspace**; all profile requirements have current technical evidence and project-owner review.

Normative and informative references:

- [WCAG 2.2 Recommendation](https://www.w3.org/TR/WCAG22/), evaluated at Levels A and AA for the declared collection page/process and its responsive variations.
- [WCAG-EM](https://www.w3.org/WAI/test-evaluate/conformance/wcag-em/) for evaluation scope and representative-state discipline.
- [Understanding Reflow](https://www.w3.org/WAI/WCAG22/Understanding/reflow.html), [Resize Text](https://www.w3.org/WAI/WCAG22/Understanding/resize-text.html), [Text Spacing](https://www.w3.org/WAI/WCAG22/Understanding/text-spacing.html), [Target Size](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html), and [Focus Not Obscured](https://www.w3.org/WAI/WCAG22/Understanding/focus-not-obscured-minimum.html) for the exercised boundary conditions.
- [WAI-ARIA Authoring Practices: Grid and Table Properties](https://www.w3.org/WAI/ARIA/apg/practices/grid-and-table-properties/) as informative guidance; the collection uses native table semantics rather than claiming an interactive ARIA grid.
- [ISO 9241-110:2020](https://www.iso.org/standard/75258.html) for interaction principles and [ISO 9241-210:2019](https://www.iso.org/standard/77520.html) for human-centred design throughout the lifecycle. This project mapping is not an ISO certification claim.

## Method and representative matrix

| Dimension | Evaluated evidence |
|---|---|
| Page/process | Shared page header/status/content anatomy; DataTable delayed loading, empty, error/retry, ready, query, sort, grouping, selection, paging, and progressive-loading behavior; five product consumers and their collection-preserving launch/recovery behavior |
| Viewports | 1280×720 desktop, 390×844 compact, and 320×900 reflow boundary |
| Appearance | Light and dark; deterministic reduced-motion media; rest, hover, keyboard-focus, disabled, current-page, selection, status, and retry states |
| Content | Canonical EN plus VI; Business Objects as the maximal visual representative; twenty rows, long localized description/control text, and WCAG text-spacing overrides |
| Input/semantics | Pointer and keyboard journeys; one `h1`; named collection region; native table/header/cell semantics; labels, current page, selected rows, busy/status feedback, and named controls |
| Visual | Five accepted captures: light/dark × desktop/compact plus VI at the 320 CSS-pixel text-spacing/reflow boundary |
| Deterministic checks | Typed owner/id contract; five-consumer real-symbol registry; component state evidence; browser surface markers, accessibility-tree snapshot, focus treatment, 32/44 CSS-pixel geometry, internal/document overflow, theme, locale, reduced motion, and exact screenshot comparison; clean-cutover sweep |

Automation supports but does not make the perceptual or standards-review decision. Business Objects is representative because it exercises the complete page header, description, search, column control, primary action, maximal column set, status badges, paging, and both internal scroll axes. Focused component evidence covers owner options and states not visible in one ready-state capture, while the five consumer suites establish product adoption without treating feature semantics as surface-owned.

Reflow evidence uses a 320 CSS-pixel viewport, the W3C equivalent boundary for 1280 CSS pixels at 400%, and separately applies the specified letter, word, and line spacing. The page retains viewport ownership while the table viewport alone owns overflow. Compact hit targets are at least 44 CSS pixels and desktop controls at least 32 CSS pixels; the column-resize drag handle retains its separate 24 CSS-pixel minimum-target contract and is not represented as a toolbar action.

## WCAG 2.2 A/AA applicability

`Technical pass` means no failure was found within the declared page/process and current representative matrix. `N/A` means the scoped page/process contains no content or operation to which the criterion applies. These accepted findings apply only to this declared review unit and are not a product-wide certification claim.

| Criterion | Result | Scoped evidence or rationale |
|---|---|---|
| 1.1.1 Non-text Content | Technical pass | Decorative table/action icons are hidden from the accessibility tree; named controls and adjacent live text preserve their purpose. |
| 1.2.1 Audio-only and Video-only (Prerecorded) | N/A | No prerecorded audio or video. |
| 1.2.2 Captions (Prerecorded) | N/A | No prerecorded synchronized media. |
| 1.2.3 Audio Description or Media Alternative (Prerecorded) | N/A | No prerecorded synchronized media. |
| 1.2.4 Captions (Live) | N/A | No live audio. |
| 1.2.5 Audio Description (Prerecorded) | N/A | No prerecorded video. |
| 1.3.1 Info and Relationships | Technical pass | One `h1`, a named collection region, native table structure, column headers, row-selection labels, status badges, paging state, and associated controls preserve programmatic relationships. |
| 1.3.2 Meaningful Sequence | Technical pass | Page context precedes status and the collection; toolbar, table header/body, and footer retain logical DOM order across responsive modes. Horizontal table overflow does not reorder content. |
| 1.3.3 Sensory Characteristics | N/A | No instruction relies on shape, position, sound, or sensory location alone. |
| 1.3.4 Orientation | Technical pass | No orientation lock; compact and desktop layouts are exercised. |
| 1.3.5 Identify Input Purpose | N/A | Search/filter inputs do not collect personal information covered by this criterion. |
| 1.4.1 Use of Color | Technical pass | Sort, current page, selection, status, disabled, error, and focus states retain text, icon, native state, or boundary in addition to color. |
| 1.4.2 Audio Control | N/A | No audio. |
| 1.4.3 Contrast (Minimum) | Technical pass | Canonical semantic theme roles and accepted inspection cover representative body, muted, link, badge, header, control, and footer text in light and dark. |
| 1.4.4 Resize Text | Technical pass | The VI 320 CSS-pixel boundary keeps page context, toolbar, table, paging, and footer usable without clipping or document overflow. |
| 1.4.5 Images of Text | Technical pass | No image of text. |
| 1.4.10 Reflow | Technical pass | At 320 CSS pixels, the page has no horizontal document overflow; the table exposes its own horizontal and vertical scrolling as required by the data relationship. |
| 1.4.11 Non-text Contrast | Technical pass | Control, table, current-page, status, selection, and focus boundaries use canonical semantic theme pairs and are rendered in both themes. |
| 1.4.12 Text Spacing | Technical pass | The VI matrix applies the WCAG letter/word/line spacing values, then rechecks document overflow, internal table scrolling, target geometry, focus, and the accepted capture. |
| 1.4.13 Content on Hover or Focus | Technical pass | Toolbar menus remain dismissible and do not make essential information hover-only; the representative hover treatment is reduced-motion safe. |
| 2.1.1 Keyboard | Technical pass | Search, table sort/actions, selection, paging, retry, and consumer actions use native or accessible controls; browser and component evidence operate them by keyboard. |
| 2.1.2 No Keyboard Trap | Technical pass | The collection itself creates no trap; toolbar popups use standard dismissible behavior. Managed windows are outside this review unit. |
| 2.1.4 Character Key Shortcuts | N/A | No single-character shortcut. |
| 2.2.1 Timing Adjustable | N/A | No collection interaction has a user-facing time limit. |
| 2.2.2 Pause, Stop, Hide | N/A | No moving, blinking, scrolling, or auto-updating content requires a pause control. |
| 2.3.1 Three Flashes or Below Threshold | Technical pass | No flashing content; reduced-motion behavior is exercised. |
| 2.4.1 Bypass Blocks | Technical pass | The containing Authenticated Frame supplies the single `main` landmark; the collection exposes a named region and heading within it. |
| 2.4.2 Page Titled | Technical pass | The containing application supplies the non-empty `Axis Platform` document title. |
| 2.4.3 Focus Order | Technical pass | Focus follows page actions, query controls, table controls/content, and paging without CSS reordering across layout families. |
| 2.4.4 Link Purpose (In Context) | Technical pass | Record links and toolbar/paging actions have resource- or operation-specific accessible names. |
| 2.4.5 Multiple Ways | N/A | Each collection is an application process destination rather than a page corpus requiring a second discovery method. |
| 2.4.6 Headings and Labels | Technical pass | Each consumer supplies one descriptive localized page heading; search, query, column, sort, selection, retry, and paging controls have descriptive names. |
| 2.4.7 Focus Visible | Technical pass | Browser evidence verifies canonical focus-visible treatment on the primary action and localized search control. |
| 2.4.11 Focus Not Obscured (Minimum) | Technical pass | Focused controls remain in the viewport; fixed overlays stay closed and the table owns its internal scroll boundary. |
| 2.5.1 Pointer Gestures | N/A | No multipoint or path-based gesture is required. |
| 2.5.2 Pointer Cancellation | Technical pass | Standard button, link, checkbox, select, and drag-handle activation avoids down-event-only completion. |
| 2.5.3 Label in Name | Technical pass | Visible action, column, record, search, filter, and paging labels are included in accessible names. |
| 2.5.4 Motion Actuation | N/A | No device/user motion input. |
| 2.5.7 Dragging Movements | Technical pass | Column resizing has keyboard-restorable layout and is optional; no essential collection operation requires dragging. |
| 2.5.8 Target Size (Minimum) | Technical pass | Browser geometry asserts 44 CSS-pixel compact and 32 CSS-pixel desktop toolbar, table, and paging targets; checkbox labels expand their hit area, and the resize handle retains the 24 CSS-pixel AA minimum. |
| 3.1.1 Language of Page | Technical pass | Browser evidence asserts `lang=en` and `lang=vi` in their respective modes. |
| 3.1.2 Language of Parts | N/A | Each evaluated page is rendered in one selected language; product/resource identifiers do not require language-part overrides. |
| 3.2.1 On Focus | Technical pass | Focusing query, sort, paging, selection, or resource controls does not mutate data or navigate. |
| 3.2.2 On Input | Technical pass | Search/filter/sort/paging changes are the expected direct result of operating their named controls; data mutation requires an explicit action. |
| 3.2.3 Consistent Navigation | Technical pass | Page context, collection toolbar, table, and paging order remain stable across the five consumers. |
| 3.2.4 Consistent Identification | Technical pass | Equivalent query, sort, column, selection, retry, and paging controls use one DataTable mapping. |
| 3.2.6 Consistent Help | N/A | No help mechanism is present in the scoped page/process. |
| 3.3.1 Error Identification | Technical pass | Loading failures use explicit localized error content and a named retry action; feature validation remains in the launched workflow owner. |
| 3.3.2 Labels or Instructions | Technical pass | Query inputs and controls have labels/placeholders and column-derived option labels sufficient for their operations. |
| 3.3.3 Error Suggestion | Technical pass | Recoverable collection failures expose retry; unsupported feature actions are omitted or accompanied by explicit status/recovery content. |
| 3.3.4 Error Prevention (Legal, Financial, Data) | N/A | The collection surface does not itself complete legal, financial, or destructive data mutation; feature workflows own those decisions. |
| 3.3.7 Redundant Entry | N/A | The collection does not require repeated information entry. |
| 3.3.8 Accessible Authentication (Minimum) | N/A | Authentication is outside the authenticated collection scope. |
| 4.1.2 Name, Role, Value | Technical pass | Native controls and table semantics expose names and sorted, selected, expanded, current, disabled, invalid, and busy values; component and accessibility-tree evidence assert the contract. |
| 4.1.3 Status Messages | Technical pass | Loading, error, result count, selection count, and consumer status feedback use visible text and semantic status/live mappings where dynamic announcement is required. |

## Interaction and human-centred evaluation

These are accepted technical findings for the declared Resource Workspace review unit.

| Principle | Current technical assessment |
|---|---|
| Suitability for the task | Each route provides page orientation, one primary collection, the enabled query controls, and consumer-owned record actions without competing workspaces. |
| Self-descriptiveness | Localized heading/description, named collection region, visible column labels, search/filter/column controls, status badges, counts, and paging expose current state and available actions. |
| Conformity with expectations | Native table structure, familiar search/sort/filter/paging controls, internal data overflow, explicit actions, and stable browser history follow enterprise web conventions. |
| Learnability | All five consumers reuse one page/table grammar, semantic state mapping, density system, and paging/query placement. |
| Controllability | Query and paging changes require direct operation; record launch is explicit; retry is local; table state remains available when a separate managed workflow opens. |
| Use-error robustness | Loading and failure states preserve page context; retry remains local; selection/query state is explicit; feature workflows retain validation, authorization, conflict, and dirty-close policy. |
| User engagement | Calm page hierarchy, one dominant collection, restrained borders/elevation, semantic accent use, and stable internal scrolling keep attention on the data task. The accepted matrix records the current perceptual judgment. |

The human-centred lifecycle evidence evaluates one coherent shared boundary and exercises adverse width, locale, text spacing, theme, motion, row volume, query, paging, loading, empty, error, denial, and recovery conditions. Technical evidence and project-owner review are complete for this review unit.

## Ownership and retirement

- `ResourceWorkspace` is the only shared collection-page owner and requires one of the five finite `resource-workspace` ids.
- `DataTable` is the only shared primary collection table owner. Its toolbar, query builder, table viewport, row states, and footer share centralized app-owned density and checkbox hit-area mappings.
- `BusinessObjectsPage`, `MembershipManagementPage`, `ProductRoleAssignmentsPage`, `RulesPage`, and `ServiceIdentitiesPage` are the five active consumers; the real-symbol registry compiler-binds each implementation and id to the owner.
- Consumers retain API, authorization, localized copy, record workflow, validation, mutation, and recovery semantics. `ResourceWorkspace` and `DataTable` own only the shared page/collection presentation contract.
- `ManagedWindowManager` and its windows remain a separate review unit even when launched from a collection. No managed window is open in Resource Workspace perceptual evidence.
- The supported path contains no parallel collection-page wrapper, feature-owned primary table, action-column fallback, compatibility alias, or legacy composition. The retirement sweep is `rg -n "ResourceWorkspace|resource-workspace|<DataTable|data-slot=\"resource-workspace\"" frontend/src frontend/tests frontend/e2e` and must be reviewed with the typed registry evidence.

## Review disposition

Technical assessment: **accepted for the declared Resource Workspace**. Human-facing lifecycle roll-up: **Complete**. All eighteen current profile requirements are acceptance-traced with accepted perceptual evidence, criterion-level standards assessment, typed consumer ownership, rendered owner markers, and current retirement proof.
