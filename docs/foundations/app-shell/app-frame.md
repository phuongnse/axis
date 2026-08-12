# App Frame

> **Navigation**: [docs/foundations/app-shell/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Provide the shared frame for authenticated Axis Platform routes without owning dashboard content, account profile behavior, or session lifecycle behavior.

## Consumers

- Authenticated routes that need shared page chrome.

## Activation

- An authenticated route renders inside the application frame.

## Current review unit

- **Boundary:** the complete `authenticated-frame` owner and its `/_authenticated` consumer: viewport shell, App Header outside the Account popup, module-navigation boundary, route-content/context-transition boundary, global managed-window and notification layers, and footer.
- **Why this is one unit:** these regions share one owner, contract marker, viewport and document-scroll boundary, lifecycle decision, and perceptual matrix. Typography, spacing, density, iconography, tone, responsive flow, and accessibility are concerns within that frame rather than separate review units.
- **Excluded owners:** Account popup internals, module-navigation taxonomy and destinations, managed-task-window anatomy, notification content, Dashboard content, route-specific content, Account profile workflow, and sign-out session behavior. Dashboard main content remains intentionally empty so the frame is evaluated without borrowing a product screen.
- **Stop/reopen conditions:** a new frame region or semantic role, a change to the Account/module-navigation/managed-window owner contracts, a change to the visual constitution/profile/theme, a second frame composition, an unexpected baseline or gate failure, or project-owner feedback that changes the contract.

## Guarantees

- Renders authenticated route content within shared top-bar, main-content, and footer regions.
- Presents product identity, page context, signed-in identity, eligible Workspace context, preferences, and sign-out entry points without taking ownership of their workflows.
- Keeps route content full-width and leaves route-specific layout decisions to the consuming route.
- Presents version and copyright metadata in the footer.

### Authenticated frame anatomy

| Region | Relationship | Presentation contract |
|---|---|---|
| Header | Product and page orientation | Presents the decorative Axis mark, localized page context, and one Account trigger on a stable leading/trailing axis. Account popup anatomy stays with `account-surface`. |
| Module-navigation boundary | Global discovery | Places the separately owned module-navigation contribution set in a desktop side rail and compact horizontal row without creating a second page scroll owner. |
| Route work area | Current task | Owns the single `main` landmark and a full-width, internally bounded route-content slot; the consuming route owns its content and any route-level scrolling. |
| Context transition | Continuity and recovery | Keeps the frame mounted, marks the work area busy, blocks stale interaction, and overlays localized live status while the authoritative Workspace context is restored. |
| Managed windows and notifications | Global layers | Reserves stable layers outside route content while their separately owned hosts retain workflow anatomy, focus, recovery, and z-order behavior. |
| Footer | Product metadata | Presents localized version and copyright metadata on one responsive boundary without constraining route width. |

`AuthenticatedFrame` owns this order and these semantic slots. `AppShell` supplies application state and adapters; it does not create an alternate layout. The frame uses one viewport-height overflow boundary, keeps header/footer stable, transforms navigation from rail to row at compact width, preserves reduced-motion behavior, and leaves the Account popup closed for frame-level perceptual evidence.

### Account surface anatomy

| Region | Relationship | Presentation contract |
|---|---|---|
| Signed-in identity | Orientation | Leading-aligned identity content with one owner-controlled region inset. |
| Workspace and preferences | Choices | Section labels and option rows share one leading scan axis; each region owns its symmetric inset and compact internal rhythm. The personal Workspace uses the localized `Personal` relationship label, while Organization workspaces retain their projected names. |
| Standalone section actions | Action | Create Organization and sign-out share centered geometry; destructive tone changes emphasis, not placement. |
| Feedback and recovery | Result and action | Feedback stays adjacent to its owning region without changing action names, geometry, or the surface scroll owner. |

Separators mark major region boundaries and do not supply layout spacing. Feature content supplies state and commands; the Account surface owns every region inset, gap, alignment, and action placement.

Account text uses the canonical Axis label and metadata roles; user-provided identity content wraps without clipping or changing the surface's scroll ownership. Compact controls retain the touch-target role, desktop controls retain the compact-control role, icons use the control-icon role, and destructive meaning may tint the boundary/icon without reducing readable text contrast. The trigger's accessible name includes its visible context label. The surface preserves semantic regions, groups, current/pending state, recovery announcements, keyboard reachability, focus visibility, reduced-motion behavior, and one-dimensional reflow at the 320 CSS-pixel boundary with WCAG text-spacing overrides.

## Alternate / error flows

- Narrow viewport: frame content reflows without horizontal page overflow.
- Constrained height: the app frame prevents document-level scrolling; route content owns any needed internal scroll container.
- Missing user label or initials: account actions menu uses the user fallback copy and initial placeholder.
- Sign-out selected: session lifecycle behavior is handed off to the sign-out use case.

## Acceptance Criteria

- **AC-001** Authenticated routes render page content inside the shared app frame.
- **AC-002** The frame exposes a top bar with product identity, page context, and one account-actions entry point ordered as signed-in identity, one flat eligible-Workspace choice set, preferences, then spatially separated sign-out; option rows retain a leading scan axis while standalone section actions share centered geometry independent of tone.
- **AC-003** The frame exposes footer app metadata with version information and Axis Platform copyright.
- **AC-004** The complete Account surface uses canonical Axis typography, spacing, density, icon, tone, depth, motion, and state roles; long localized/user-provided content wraps without clipping, compact targets remain at least 44 CSS pixels high, desktop targets remain at least 32 CSS pixels high, and its light/dark × desktop/compact plus VI reflow candidates remain deterministic.

*Quality*
- **AC-005** The frame fits supported desktop and mobile widths without horizontal page overflow, document-level scrolling, or a maximum content width on authenticated routes.
- **AC-006** Visible frame copy uses the frontend translation layer.
- **AC-007** The scoped Dashboard page and Account interaction preserve page title/language, landmarks, semantic names/roles/values, label-in-name, meaningful order, keyboard operation, visible/unobscured focus, status announcements, readable contrast, reduced motion, text spacing, and one-dimensional reflow; criterion-level applicability and retirement decisions are recorded without claiming product-wide certification.
- **AC-008** The complete Authenticated Frame uses canonical Axis typography, spacing, density, icon, tone, depth, motion, and state roles; retains banner/navigation/main/contentinfo order, readable contrast, 32 CSS-pixel desktop and 44 CSS-pixel compact targets, viewport/document-scroll containment, and deterministic light/dark × desktop/compact plus VI reflow candidates.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | App frame renders top bar, main content, footer metadata, and the complete ordered Account surface with canonical semantic typography, icon, target, state, long-content, recovery, and ownership mappings without a placeholder route navigation bar. | AC-001, AC-002, AC-003, AC-004, AC-006, AC-007 | UI component test | Yes |
| AT-002 | Browser journey | Desktop, compact, and 320 CSS-pixel VI reflow modes render an empty Dashboard route and the complete Account surface without console errors, page/surface overflow, document scrolling, obscured keyboard focus, clipped long identity content, undersized targets, or shell-level width caps; text-spacing stress and the semantic accessibility tree remain intact. | AC-001, AC-002, AC-003, AC-004, AC-005, AC-006, AC-007 | Browser automation | Yes |
| AT-003 | Static frontend | Frame code typechecks, lints, and keeps localized copy keys valid. | AC-006 | Frontend CI | Yes |
| AT-004 | Static frontend | The scoped Dashboard page plus complete Account process is assessed criterion-by-criterion against applicable WCAG 2.2 A/AA requirements, interaction/human-centred principles, ownership, clean cutover, and accepted perceptual evidence. | AC-002, AC-004, AC-005, AC-006, AC-007 | Frontend CI | Yes |
| AT-005 | Browser journey | The closed-Account Authenticated Frame proves landmark order, keyboard-visible focus, all-visible-text contrast, target geometry, overflow/document-scroll containment, reduced motion, and deterministic EN light/dark × desktop/compact captures plus a VI 320 CSS-pixel text-spacing/reflow capture. | AC-001, AC-003, AC-005, AC-006, AC-007, AC-008 | Browser automation | Yes |
| AT-006 | Static frontend | The complete Authenticated Frame is assessed criterion-by-criterion against applicable WCAG 2.2 A/AA requirements and interaction/human-centred principles; typed owner/consumer adoption, clean cutover, accepted evidence, and review are current. | AC-001, AC-003, AC-005, AC-006, AC-007, AC-008 | Frontend CI | Yes |

## Out Of Scope

- Dashboard content and information architecture.
- Route-specific contained, fluid, or canvas workspace layout decisions.
- Account profile behavior.
- Sign-out backend/session behavior.
- Future navigation destinations beyond currently implemented routes.
- Module-navigation contribution taxonomy, grouping, destinations, availability policy, and item-level behavior; those remain owned by [docs/foundations/app-shell/module-navigation.md](./module-navigation.md).
- Canvas-specific tool, layer, property, or asset panels; those belong to the owning canvas workspace feature rather than global app shell chrome.

## Screen flow

| Screen | Required contract |
|---|---|
| Authenticated app frame | Render top bar, main content, and footer around authenticated route content. |
| Top bar | Show the Axis Platform brand mark, page context, and a compact account trigger with profile context across the available viewport width. |
| Account actions menu | Orient with the signed-in human identity, present one flat eligible-Workspace choice set with the current state, then language/theme preferences and a spatially separated sign-out action. Label the personal relationship `Personal` through the frontend translation layer instead of repeating the signed-in person's name; retain projected names for Organization workspaces. Keep choices leading-aligned and render Create Organization plus sign-out as the same centered standalone-action role with independent tone. Do not add profile editing or separate Personal/Organization grouping labels already conveyed by each option icon. |
| Main content | Preserve the owning route content in a full-width, non-document-scrolling region, including an empty route surface when no product screen exists yet. |
| Footer | Show version information on the left and Axis Platform copyright metadata on the right across the available viewport width. |

Required UI quality: frame landmarks and controls must be keyboard-reachable, visible copy must be localized, and the shell must not create document-level overflow at supported mobile or desktop widths.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Contract | Done |
> | Frontend | Done |
> | Tests | Done |
>
> **Implemented:** Authenticated routes render inside the shared App Frame with a stable header, responsive module-navigation boundary, full-width main/content-transition boundary, global layer hosts, and footer. The frame keeps visible copy localized, preserves an empty Dashboard route surface, contains document-level overflow, does not impose route-content width caps, and retains one semantic and responsive owner across light/dark, desktop/compact, and VI reflow modes. Account remains an independently enforced owner whose popup anatomy is unchanged by this review unit.
>
> **Gaps vs spec:** N/A for `authenticated-frame` and `account-surface`; both owners cover all current `axis-ui-v1` requirements and are enforced. Other surfaces remain governed by their independently owned contracts.
>
> **Deferred follow-ups:** N/A.
>
> **Verification:** Authenticated Frame component/runtime ownership, browser semantics, geometry, contrast, responsive overflow, document-scroll containment, localization, text spacing, five accepted version-controlled captures, and the scoped standards review are recorded with [the accepted assessment](./authenticated-frame.assessment.md). Account verification remains recorded separately in [its accepted assessment](./account-surface.assessment.md). A passing checker establishes trace integrity, not product-wide standards certification.
>
> **Decisions:** App Frame is a foundation contract, not a use case. `AuthenticatedFrame` owns the product-neutral anatomy and semantic slots; `AppShell` is the application adapter that composes session, Workspace, navigation, and recovery state into those slots. Authenticated use cases may rely on its page chrome, frame structure, and document-scroll containment; route-specific layout, profile behavior, sign-out lifecycle, and global module navigation remain owned elsewhere. Global sidebar behavior is owned by [docs/foundations/app-shell/module-navigation.md](./module-navigation.md) and does not render before visible contributions exist.
