# Axis UI Constitution

> **Navigation**: [docs/foundations/visual-system/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Define one product-level visual and interaction grammar so Axis remains coherent as features and component implementations change. Components conform to this constitution; component names, markup, and provider APIs are not design policy.

## Consumers

- Every authenticated, public, entry, authoring, administration, and product-runtime surface.
- Theme generation, upstream primitives, shared app patterns, feature composition, review, and frontend policy.
- Future component providers and specialized workbenches that must preserve the same semantic roles.

## Activation

- Any change to visible hierarchy, interaction state, feedback, density, typography, color, spacing, surface, motion, layering, responsive behavior, scroll ownership, or page anatomy.
- Any new or replaced primitive, shared visual API, page archetype, component provider, or cross-feature convention.

## Guarantees

- Axis is a calm, precise, data-first enterprise workspace. Neutral hierarchy carries structure; brand color marks primary intent; semantic color communicates meaning.
- Every UI slice inherits the project-wide [Enterprise Production Baseline](../../PLATFORM_STRATEGY.md#enterprise-production-baseline). This constitution specializes visible experience evidence; it cannot establish production readiness for the product, API, data, security, deployment, or operational slice by itself.
- [theme/axis-theme.json](../../../theme/axis-theme.json) is the sole machine-readable source for reusable visual values and timing. The generated CSS and typed runtime projection are the only implementation inputs for those values.
- This constitution owns semantic roles and invariants. Upstream primitives own accessible mechanics; app patterns map roles to reusable composition; features own product state, content, relationships, and outer layout only.
- Equivalent meaning looks and behaves equivalently across navigation, tables, forms, menus, windows, dialogs, and compact or desktop layouts.
- A component may change provider, markup, or internal implementation without changing product meaning. A component-specific test proves its mapping; it cannot establish a new product convention.

## Architecture

| Layer | Owns | Must not own |
|---|---|---|
| Constitution and theme | Semantic roles, reusable values, interaction-state grammar, and cross-surface invariants | JSX, provider APIs, feature state, or screen-specific choices |
| Accessible primitives | Provider mechanics, semantics, focus behavior, and registry provenance | Product variants, page anatomy, or business meaning |
| Surface contracts | A finite set of frame, entry, resource, workbench, managed-task, and account anatomies with narrow semantic slots | Free-form visual overrides, feature data access, or alternate local anatomies |
| Feature composition | Product state, content, relationships, authorized actions, and recovery inside declared slots | Surface anatomy, reusable visual or mechanical capabilities, or contract variants |
| Conformance system | Active-consumer ownership, contract API tests, focused state/a11y tests, browser journeys, visual comparisons, and acceptance state | Product design decisions or project-owner acceptance as a substitute for technical evidence |

Dependencies flow in that order only. A feature cannot import a lower-level provider to recreate a higher-level contract. A surface-owner API exposes meaning, not visual or mechanical capability: leaf content slots are rendered inside anatomy owned by the surface, while a reusable region with its own states, relationships, or actions crosses the boundary as a typed semantic model and is rendered by the owner. Generic JSX cannot stand in for subsystem anatomy. This ownership rule governs all styling and mechanics without creating a policy per CSS property, component, or DOM symptom. Component evidence proves semantic API, state, and ownership; browser and visual evidence prove rendered geometry and perceptual equivalence. Implementation-level selectors may diagnose a failure but are not durable conformance evidence.

`frontend/ui-coverage-profile.json` owns the versioned, machine-readable conformance requirements, evidence kinds, representative modes, and invalidation triggers. `frontend/ui-foundation.json` records each defined-or-later contract's lifecycle state, owning spec, profile version, acceptance state, declared evidence, and exact covered/gap/not-applicable partition; it is not a catalog of source paths or screen designs. Its contract keys are imported as TypeScript types by `frontend/src/lib/ui-foundation.ts`, which maps finite active surface ids to those contracts. The manifest's separate `enforcedContracts` registry supplies the enforced-contract union, and the policy gate rejects profile, registry, state, evidence, acceptance, traceability, or coverage drift. `frontend/src/lib/active-surface-registry.ts` must then exhaustively bind real owner and implementation symbols. Every surface owner requires a contract-compatible id and emits both ids for rendered evidence. Parser-backed module restrictions keep owners independent of feature and route state. This makes registration, lifecycle truth, and dependency direction part of composition instead of inferring them from filenames or raw source text. A new consumer of an unchanged enforced contract needs typed registration, rendered conformance, and product-state evidence, not bespoke visual approval. Changing the shared owner, constitution, profile, or relevant evidence reopens the contract lifecycle for every affected consumer.

## Semantic grammar

| Axis | Roles | Invariant |
|---|---|---|
| Hierarchy | page, section, component, body, label, metadata | Use typography, spacing, and contrast before adding containers or decoration. One level has one role across the product. |
| Typography | display, title, section, body, label, metadata, code | Size, weight, line height, and tracking express the hierarchy role; features do not tune them independently. |
| Color and tone | base, muted, emphasis, brand intent, information, success, warning, destructive | Neutral tone carries structure; brand marks primary intent; semantic color communicates meaning and is never the only cue. |
| Space | inline, region, section, page-compact, page-default, page-wide | Use the shared rhythm by relationship. A feature does not invent reusable gaps or page gutters. |
| Density | compact-control, default-control, touch-target | Desktop density stays efficient; pointer size never weakens the compact touch target. |
| Surface | base, floating, managed | Base content uses structure without elevation; contextual overlays float; long-lived tasks use managed elevation. |
| Radius | flat, control, floating, managed | Radius communicates boundary depth, not decoration. Features do not choose a new tier. |
| Elevation | none, floating, managed, dock | Elevation follows surface depth and remains equivalent in light and dark modes. |
| Icon | control, navigation, empty | Use one vector family and one size/stroke role at each hierarchy. Icons support meaning and never replace an accessible name. |
| Motion | state, floating, content-pending, context | Motion communicates causality, uses opacity or transform, is interruptible, and respects reduced motion. |
| Layer | base, sticky, floating, modal, managed, notification | Layer order is semantic and finite; components do not invent numeric stacking values. |

Exact role values live only in [theme/axis-theme.json](../../../theme/axis-theme.json). The generator projects those roles into CSS, the typed `axisStyles` runtime contract, and its Tailwind merge semantics so independent roles are preserved and equivalent roles resolve deterministically. Authored product code composes that typed contract with standard utilities; it does not spell reusable `*-axis-*` utilities or copy their current values. The parser-backed consumption gate prevents that bypass, while the generated contract—not a deny-list of class names—remains the source of truth.

## Interaction state model

| State | Product meaning | Required expression |
|---|---|---|
| Rest | Available and inactive | Owning surface and foreground pair. |
| Transient | Pointer hover, keyboard highlight, or open context | Lower-emphasis transient surface; never replaces persistent state. |
| Persistent | Selected, current, expanded, or toggled | Stronger persistent surface retained through hover. Choice semantics determine whether a native indicator is also required. |
| Focus | Keyboard position | Visible semantic ring independent of fill, color, and selection. |
| Disabled | Unavailable action | Semantic disabled state and native non-interactive behavior; never opacity alone. |
| Busy | Authoritative work in progress | Lock only the affected boundary and mirror user-invoked action feedback for exactly the authoritative pending interval. A choice transition emphasizes its target immediately without claiming selected or current semantics before authority accepts it. Delay passive content or context feedback by its semantic threshold; keep geometry and labels stable. |
| Destructive | Irreversible or harmful consequence | Destructive semantic pair plus explicit language; never color alone. |
| Feedback | Information, success, warning, or failure | Semantic pair, text, accessible announcement, and recovery when recovery exists. |

Initial loading reserves the owning region and does not render empty content first; its visual indicator appears only after the content threshold. Background refresh preserves current content and scroll position. User actions lock their boundary and replace the stable icon slot with pending feedback immediately, then restore it as soon as authoritative work ends. Context transitions keep the authenticated frame and account context mounted, make stale content inert, and obscure it only after the context threshold. Fast passive loads therefore complete without spinner flashes, while invoked actions remain truthful without label shifts, layout shifts, or transient document scrollbars.

## Layout and composition

- The authenticated frame owns the viewport and global navigation. Each route has exactly one page archetype and one scroll owner.
- Resource management defaults to one primary collection with long-lived create, view, and edit tasks in app-scoped managed windows.
- Overview pages summarize outcomes and navigation; they do not imitate management tables.
- A dedicated workbench requires a canvas, builder, comparison, long-running process, or dependent panels that cannot remain usable in a managed window.
- Entry and informational pages may use a focused anatomy while retaining the same semantic roles and state model.
- Compact layouts preserve task priority, touch targets, labels, and recovery; desktop layouts increase density without changing meaning.

### Entry Surface contract

`EntrySurface` is the sole focused public-entry owner. `EntryLayout` owns the document-height canvas, responsive page gutters, top-end utilities, one `main` region, one centered width boundary, horizontal-overflow prevention, and document vertical scrolling for tall content. `EntrySurface` owns one Card with ordered brand/title, optional banner, leaf content, and optional footer regions. The typed `surfaceId` identifies one of email confirmation, invitation acceptance, registration, session unavailable, sign-in, or email verification; feature consumers supply product state and text without recreating that anatomy.

Entry form controls use owner-provided input, primary-action, navigation-action, async-action, and consent-label mappings. They preserve the `touch-target` role in compact layouts and return to `compact-control` density from the small breakpoint. Inline text links retain native link semantics and the WCAG inline-target exception. Public Preferences remains outside the Card but inside the Entry layout, and uses the same responsive density. Registration is the maximal visual representative because it exercises utilities, title, all form roles, consent/legal content, primary action, footer, tall-document scrolling, and localized reflow. Consumer-specific loading, validation, retry, success, authorization, invitation, and authentication behavior remains owned by the relevant use case.

## Conformance and exceptions

1. Select the semantic role before selecting a component.
2. Reuse an accessible upstream primitive or existing app pattern when it maps the role.
3. Keep component internals and provider variants local to their owner; keep feature styling to outer relationship layout.
4. When no role fits, stop. Extend this constitution and the canonical theme only if the need is cross-feature; otherwise record a bounded exception with one owner and proving evidence.
5. Remove the retired mapping when replacing a convention. Do not keep parallel visual paths, compatibility wrappers, or feature-local fallbacks without a real supported consumer constraint.

### UI conformance profile

UI conformance has five layers; none can substitute for another:

1. **Project production baseline** — [Platform Strategy](../../PLATFORM_STRATEGY.md#enterprise-production-baseline) remains the sole project-wide owner. The Design Gate classifies every applicable production concern and blocks a slice that lacks current proof.
2. **External UI standards floor** — [WCAG 2.2](https://www.w3.org/TR/WCAG22/) Level AA applies to complete pages and processes, including responsive variations. The [WAI-ARIA Authoring Practices Guide](https://www.w3.org/WAI/ARIA/apg/) informs accessible widget semantics and keyboard behavior but does not replace WCAG conformance or native HTML. [ISO 9241-110:2020](https://www.iso.org/standard/75258.html) informs interaction principles, and [ISO 9241-210:2019](https://www.iso.org/standard/77520.html) informs human-centred design and evaluation throughout the lifecycle.
3. **Axis UI constitution** — this document and the canonical theme define the product-specific semantic and visual system.
4. **Versioned UI coverage profile** — `frontend/ui-coverage-profile.json` translates the current UI standards and constitution into auditable visual, behavioral, standard, and lifecycle requirements. Its requirement count is not a universal checklist, a permanent claim of completeness, or a replacement for the project production baseline; changing the profile version is a governed foundation change. Policy preserves the four UI categories plus the WCAG 2.2 AA, interaction-principles, human-centred-evaluation, consumer-ownership, and retirement/compatibility floor so a profile edit cannot silently remove them.
5. **Surface evidence and lifecycle** — every contract classifies every profile requirement exactly once as `covered`, `gap`, or allowed `notApplicable`. A covered entry names owning acceptance-test ids, evidence declared by the contract and its evidence sidecar, and all required representative modes. `notApplicable` is valid only when the profile permits it and the surface records a specific rationale.

The UI profile is a traceability control, not an automated declaration of standards compliance or overall production readiness. In particular, a WCAG conformance claim requires an applicable-success-criteria assessment of the complete page or process; screenshots, automated accessibility checks, component tests, or project-owner review alone are insufficient. Standard requirements and retirement/compatibility claims therefore require a declared `docs/foundations/**/*.assessment.md` artifact in addition to the relevant runtime evidence and review. Automated policy proves that required trace structure is present and internally consistent. Human review remains responsible for semantic adequacy, standards applicability, perceptual judgment, authentic project-owner acceptance, and reconciliation with every applicable project-level production concern.

## Contract lifecycle

| State | Meaning | Required evidence |
|---|---|---|
| `requested` | A new or changed shared contract is proposed | Read-only inventory and owning requirements. |
| `authorized` | Design Gate, one review unit, and required sign-off are current | Owner, invariants, clean-cutover decision, scope, stop conditions, and verification plan. |
| `defined` | The durable contract and machine-readable owner are current | Pending acceptance plus semantic, anatomy, state, responsive, accessibility, and evidence matrices; perceptual artifacts are missing or candidate only. |
| `verified` | The owner has no profile gaps and passes project-owner review | Accepted, acceptance-traced component, browser, accessibility, responsive, standards-assessment, and perceptual evidence for every applicable profile requirement. |
| `enforced` | Every active consumer is compiler-bound to its owner contract, rendered evidence confirms that boundary, and retired compositions are absent | Accepted verification plus passing typecheck, consumer conformance, and retirement evidence. |

Each change declares exactly one review unit before implementation: the constitution/foundation, one surface owner, or one consumer of an unchanged enforced owner. The unit is the largest coherent boundary with one owner, contract, invalidation set, and review decision—normally a whole foundation/surface or a complete consumer. Typography, spacing, individual regions, and similar implementation details are not separate units when they share that boundary; splitting requires genuinely independent ownership, acceptance, verification, or safe rollback and an explicit Design Gate rationale. Requested and authorized work remains task-local. A surface enters `frontend/ui-foundation.json` at `defined` with pending acceptance; version-controlled captures remain `candidate` until the project owner accepts the rendered result. Project-owner acceptance is necessary for `verified` or `enforced`, but it cannot replace technical evidence. The policy gate requires an exact profile partition and traces every covered requirement to real acceptance-test rows, their evidence-sidecar entries, declared evidence kinds, and required modes. `verified` and `enforced` permit no gaps; enforcement also requires consumer ownership and retirement evidence. This exposes missing proof without turning file presence, a passing checker, or a review decision into a design or standards claim.

Those independent machine controls are not three reviewer-facing workflows. Every human handoff leads with one derived status for the declared unit: `In progress` while implementation or non-review evidence remains, `Awaiting review` when technical evidence and the candidate are ready and only project-owner review remains, or `Complete` after acceptance and the intended verified/enforced target is satisfied. Review changes return the same unit to `In progress`. Raw manifest values appear only as audit detail; this roll-up is not stored as another state machine.

Only entries whose state is `enforced` and whose id is present in `enforcedContracts` may be consumed as an enforced shared contract. Revising the constitution or an enforced owner returns the affected contract to task-local `authorized`, then `defined` with pending acceptance while the replacement is under review. Scope expansion, an unclear owner, a new semantic role, a failed gate, an unexpected baseline, or a post-acceptance foundation change stops the unit and reopens its Design Gate. One accepted unit does not authorize the next consumer.

## Alternate / error flows

- Loading, empty, forbidden, missing, unavailable, validation, conflict, disabled, pending, success, and stale/retry states retain the same hierarchy and geometry wherever they apply.
- Permission and missing states remain non-disclosing. Unavailable states are distinguishable and recoverable when retry can help.
- Long-lived drafts survive supported navigation and window focus changes; dirty dismissal is explicit.
- Reduced motion removes spatial transition while preserving immediate state and focus. Localization may change copy length but not role, hierarchy, or behavior.
- Unsupported compact geometry, contrast, focus, overflow, or provider behavior blocks contract verification and enforcement; it is not deferred as polish.

## Acceptance Criteria

- **AC-001** One semantic constitution and canonical theme own every reusable visual value, interaction role, timing, and layer; no component or feature creates a parallel convention.
- **AC-002** Equivalent hierarchy, state, feedback, density, surface depth, icon role, and motion remain perceptually equivalent in light/dark and desktop/compact modes.
- **AC-003** Async initial load, refresh, action, and context transition preserve geometry, content continuity, scroll ownership, focus, and accessible status without fast-operation flashing.
- **AC-004** Every route uses one approved archetype and one scroll owner; resource management is collection-first with managed task windows unless a documented workbench contract applies.
- **AC-005** Keyboard, screen reader, contrast, localization, touch target, reduced motion, responsive overflow, and focus recovery meet the enterprise accessibility baseline.
- **AC-006** The contract lifecycle separates authorization, definition, verification, and enforcement; supported surfaces cannot bypass their registered owner or retain a legacy composition.
- **AC-007** Component/provider replacement preserves semantic roles and removes the retired mapping unless an evidence-backed compatibility constraint exists.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Static frontend | Strict semantic schema deterministically projects CSS and a typed runtime style contract and rejects missing, unknown, or stale roles. | AC-001, AC-002, AC-003 | Frontend CI | Yes |
| AT-002 | Static frontend | Ownership checks reject generated-style bypass, hard-coded semantic values, provider leakage, feature-local interaction visuals, and profile, manifest lifecycle, candidate/accepted, coverage-partition, acceptance-trace, evidence-mode, acceptance-state, or registry drift. | AC-001, AC-006, AC-007 | Frontend CI | Yes |
| AT-003 | UI component | Representative role mappings prove hierarchy, state priority, async geometry, scroll ownership, accessibility, and reduced motion without establishing component-local policy. | AC-002, AC-003, AC-004, AC-005 | UI component test | Yes |
| AT-004 | Layout smoke | Each registered surface contract proves its representative states in light/dark and desktop/compact layouts without document overflow or console errors. | AC-002, AC-004, AC-005 | Browser automation | Yes |
| AT-005 | Browser journey | Pointer and keyboard task flow proves independent managed work, focus, draft, pending, recovery, navigation, and context continuity. | AC-003, AC-004, AC-005 | Browser automation | Yes |
| AT-006 | UI component | The typed active-surface catalog maps finite ids to contracts, the real-symbol inventory is complete, and owner markers confirm composition without filename or source-text inference. | AC-001, AC-006, AC-007 | UI component test + Frontend CI | Yes |
| AT-007 | UI component | The complete Entry Surface owner and six-consumer inventory proves one clean-cutover anatomy, criterion-level standards applicability, responsive/touch density, consumer state coverage, and accepted review disposition without absorbing authentication or invitation semantics. | AC-001, AC-002, AC-003, AC-004, AC-005, AC-006, AC-007 | UI component test + Browser automation | Yes |

## Out Of Scope

- Product-specific content, workflow, authorization, or business state.
- Server-defined product vocabulary or remote layout schemas.
- A permanent component catalog, provider API reference, or rule for every component name.
- Forcing specialized builders, canvases, reporting, or future experiences into a resource workspace.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Contract | Done |
> | Frontend | Partial |
> | Tests | Partial |
>
> **Gaps vs spec:** Account, Authenticated Frame, Entry Surface, and Resource Workspace are enforced `axis-ui-v1` contracts: each has all eighteen requirements acceptance-traced and five version-controlled captures accepted. Their human-facing roll-up is `Complete`. Managed Task Window is a technically complete candidate with fifteen non-review requirements covered and the three standards requirements intentionally pending project-owner review. Process Workbench still classifies all profile requirements as gaps. Existing evidence does not advance any lifecycle state without complete requirement traces and the review required by that state.
>
> **Deferred follow-ups:** N/A; missing contract evidence remains current work and cannot be converted into an exception.
>
> **Verification:** Current verification is recorded in [docs/foundations/visual-system/axis-visual-system.evidence.md](./axis-visual-system.evidence.md); stale or unregistered consumer evidence does not establish enforcement.
>
> **Decisions:** External standards are the floor; the versioned coverage profile translates the current standards and Axis constitution into machine-checkable trace requirements without claiming that a fixed concern count is universally sufficient. The constitution owns semantic invariants, the canonical theme owns reusable values, contract owners own reusable surface anatomy, and features own product state and declared slots. `frontend/ui-coverage-profile.json` owns requirement definitions; `frontend/ui-foundation.json` owns lifecycle, acceptance, and contract/evidence traces; the typed catalog and real-symbol registry own active consumers. Project-owner acceptance authorizes lifecycle advancement only when the declared technical evidence is complete; no filename convention, source-text pattern, screenshot, automated check, or approval alone is a parallel source of truth.
