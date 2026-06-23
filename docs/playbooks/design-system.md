# Design System

> **Navigation**: [← docs/README.md](../README.md) · [← Design source](./design-source.md) · [← Frontend playbook](./frontend.md) · [← Wireframe playbook](./wireframes.md) · [← AGENTS.md](../../AGENTS.md)

This playbook owns the Axis design-system workflow: tokens, reusable UI components, implementation rules, and deterministic contract checks. It is separate from use-case design sources. Low-fidelity wireframes describe flow and screen intent; the design system defines final UI decisions.

For repeatable execution, use `$axis-design-system`.

## Source Boundaries

| Source | Owns | Does not own |
|---|---|---|
| Use-case docs | Product behavior, actors, acceptance criteria, visible states | Visual style, spacing, colors, component internals |
| Design sources | Layout intent, flow, content hierarchy, state inventory | New product behavior, acceptance criteria, endpoints, or data contracts |
| Design system | Tokens, component anatomy, variants, state matrices, visual rules | New product behavior or acceptance criteria |
| Frontend code | Executable components, accessibility behavior, responsive implementation | New visual rules not represented in the design system |

Design-system work must not use Excalidraw files as the final visual source. Use Penpot design sources per [design-source.md](./design-source.md); legacy Excalidraw wireframes remain low-fidelity artifacts until migrated.

## Pixel-Perfect Definition

A screen or component is pixel-perfect only when all of these are true:

| Area | Requirement |
|---|---|
| Design source | Approved target exists for the component or screen, including responsive size and state coverage |
| Tokens | Colors, typography, spacing, radius, borders, shadows, and motion come from named design-system tokens |
| Component mapping | The implementation uses shared design-system components instead of page-local geometry |
| States | Default, hover, focus, active, disabled, loading, empty, success, warning, error, and long-copy states are covered when applicable |
| Responsive | Mobile, tablet, and desktop viewports are intentionally designed and verified |
| Theme | Light, dark, and system behavior use the same semantic tokens |
| Accessibility | Keyboard focus, labels, contrast, and non-color state cues are verified |
| Verification | Behavior, accessibility, responsive layout, and component contract checks pass for the covered states |

Do not mark a UI surface pixel-perfect from a single happy-path render.

## Token Taxonomy

Tokens are named by intent, not by current color or one screen. Raw values belong in one token source; components consume semantic tokens.

| Token family | Examples | Rule |
|---|---|---|
| Color scale | `color.teal.600`, `color.amber.500` | Raw palette only; components do not consume scale tokens directly |
| Semantic color | `surface.default`, `text.muted`, `action.primary.bg`, `state.danger.text` | Components consume semantic tokens |
| Typography | `font.family.sans`, `type.body.sm`, `type.heading.md` | Include size, line height, weight, and letter spacing |
| Spacing | `space.1`, `space.2`, `space.4`, `space.6` | Drives padding, gaps, and layout rhythm |
| Sizing | `size.control.sm`, `size.icon.md`, `size.sidebar` | Stable dimensions for controls and fixed UI regions |
| Radius | `radius.control`, `radius.panel`, `radius.full` | Avoid arbitrary radius in component code |
| Border | `border.width.default`, `border.color.subtle` | Use semantic border colors |
| Shadow | `shadow.panel`, `shadow.popover` | Use only where elevation improves hierarchy |
| Motion | `motion.duration.fast`, `motion.easing.standard` | Keep motion purposeful and reduced-motion aware |
| Breakpoint | `breakpoint.mobile`, `breakpoint.tablet`, `breakpoint.desktop` | Matches frontend responsive verification sizes |

Token changes are design-system changes. They should not be bundled with unrelated feature behavior.

## Token Export/Import Convention

Penpot owns the approved design token decisions. Frontend code owns the executable token contract that components can consume.

| File | Owns |
|---|---|
| `frontend/src/design-system/tokens.css` | Raw CSS variable values for light and dark themes |
| `frontend/src/design-system/tokens.ts` | Typed token registry used by tests and future design-system tooling |
| `frontend/src/design-system/primitive-contracts.ts` | UI primitive registry: file ownership, readiness status, variant/state/accessibility matrix, token families, and test coverage |
| `frontend/src/design-system/consumer-contracts.ts` | Route-bound UI consumer registry: product surface, route, owner, readiness, primitive usage, state coverage, evidence, and tests |
| `frontend/tailwind.config.js` | Tailwind names that map to semantic CSS variables |

When importing token updates from Penpot:

1. Translate approved Penpot token names into the semantic CSS variable names in `tokens.css`.
2. Update `tokens.ts` in the same change so tests know the complete token contract.
3. Map semantic tokens in `tailwind.config.js`; components should consume Tailwind token classes such as `bg-primary`, `text-muted-foreground`, `border-border`, and `rounded-md`.
4. Map shadows and reusable gradients as named Tailwind tokens such as `shadow-surface`, `shadow-panel`, and `bg-gradient-inverse-panel`.
5. Keep raw hex, raw HSL, raw neutral colors, raw shadows, and one-off visual values out of component files when a semantic token exists.

Do not generate frontend tokens from unapproved design files. If a Penpot export changes a visual decision, treat that as design-system work and include the registry/test update in the same PR.

## Component Inventory

Build the design system from primitives upward. A feature screen can use only components that exist in this inventory or are added to it in the same PR.

| Layer | Components |
|---|---|
| Primitives | Button, IconButton, Input, Textarea, Select, Checkbox, Radio, Switch, Label, Link |
| Feedback | Notice, Alert, Toast, ErrorMessage, Spinner, Skeleton, Progress |
| Structure | Card, Panel, Modal, Drawer, Popover, Tooltip, Tabs, Menu |
| Data | Table, List, EmptyState, Pagination, FilterBar, Badge |
| Forms | FormField, FieldGroup, ValidationSummary, HelpText |
| Layout | PublicAuthShell, AppShell, PageHeader, Sidebar, Toolbar, ContentGrid |
| Domain patterns | WorkflowCanvasShell, FormBuilderShell, DataModelTable, ExecutionTimeline |

Each component needs a variant matrix before broad screen use.

## Variant Matrix

Every component entry should document:

| Field | Required content |
|---|---|
| Purpose | User job the component supports |
| Anatomy | Required slots, optional slots, icon behavior, text rules |
| Variants | Intent, size, density, layout, and destructive/success/warning variants |
| States | Interaction, validation, async, and disabled states |
| Responsive behavior | Wrapping, truncation, min/max dimensions, layout changes |
| Accessibility | Role, labels, keyboard behavior, focus order, ARIA expectations |
| Token map | Tokens used by each part and state |
| Test evidence | Vitest, Testing Library, Playwright, or equivalent checks that prove the component states |

If a variant is not documented, do not infer it from a one-off screen.

## Primitive Contracts

The executable primitive contract lives in `frontend/src/design-system/primitive-contracts.ts`. Keep it updated before using a new primitive broadly in migrated screens. Reviewers inspect that file together with the listed test files; do not maintain a second manual matrix for primitive readiness.

The table below is the human summary of the current executable contract:

| Component | Purpose | Variants and states | Accessibility | Token map |
|---|---|---|---|---|
| `Button` | Visible command with required icon support | `cta`, `default`, `outline`, `secondary`, `ghost`, `destructive`, `link`; `xs`, `sm`, `default`, `lg`, icon sizes; disabled, loading, invalid, focus-visible | Native button semantics through Base UI; loading sets `aria-busy` and disables interaction | `bg-primary`, `bg-accent`, `bg-secondary`, `bg-destructive`, `border-border`, `ring-ring`, `rounded-md` |
| `IconButton` | Icon-only command for dense tool surfaces | Shared button variants; icon sizes; disabled and loading | Requires `label`; label becomes the accessible name; loading label replaces the action label while busy | Same button tokens; icon size utilities only |
| `ActionLink` | Navigation CTA on public/auth surfaces | `primary`, `secondary`; `default`, `inverted`, `adaptive` surfaces | Renders TanStack Router `Link`; icon is decorative and text remains the accessible name | `bg-accent`, `text-accent-foreground`, `border-border`, inverse action tokens |
| `Input`, `Textarea`, `Select` | Text, long text, and option input | Default, disabled, invalid, focus-visible, long-copy wrapping | Requires `Label` or `aria-label`; invalid state uses `aria-invalid`; descriptions flow through `FormField` | `bg-background`, `border-input`, `ring-ring`, `text-foreground`, `text-muted-foreground`, `rounded-md` |
| `Checkbox`, `CheckboxField` | Binary choice with optional validation | Checked, unchecked, disabled, invalid | `CheckboxField` links label and error copy through `aria-describedby` | `accent-primary`, `border-input`, `ring-ring`, `text-destructive`, `rounded-sm` |
| `FormField` | Label, help text, description IDs, and error copy wrapper | Help text, error, externally supplied description IDs | Provides deterministic `aria-describedby` IDs to child controls | `text-muted-foreground`, `text-primary`, `text-destructive` |
| `Notice` | Inline async/status feedback | `info`, `success`, `warning`, `error`; optional title/body/icon | Warning/error use `role="alert"`; info/success use `role="status"`; icon is decorative | `state.*`, `destructive`, `rounded-lg`, `border-*` |
| `Badge` | Compact status or metadata marker | `neutral`, `primary`, `accent`, `info`, `success`, `warning`, `destructive`, `outline` | Text remains the accessible name; icons remain optional/decorative | `bg-muted`, `primary`, `accent`, `state.*`, `destructive`, `rounded-md` |
| `Card`, `Panel` | Reusable framed work surfaces | Default, muted, inset, attention, inverse panels; card header/content/footer anatomy | Semantic HTML remains caller-owned; surface must not hide focusable content | `bg-card`, `bg-muted`, `bg-background`, `bg-inverse`, `border-border`, `shadow-*` |
| `Skeleton`, `Progress`, `EmptyState` | Loading, quantitative progress, and empty-state feedback | Fixed skeleton blocks; determinate/indeterminate progress; empty state title/description/action | Progress requires an accessible label; empty-state icon is decorative | `bg-muted`, `accent-primary`, `rounded-md`, `rounded-full` for progress track |
| `PageHeader`, `Toolbar`, `ContentGrid` | Shared page-level layout rhythm | Header with eyebrow/title/description/actions; compact toolbar; responsive 1/2/3-column grid | Keeps heading/action order stable across viewports | `text-foreground`, `text-muted-foreground`, `border-border`, `bg-card`, shared spacing scale |

## Consumer Contracts

The executable consumer contract lives in `frontend/src/design-system/consumer-contracts.ts`. It covers route-bound product UI surfaces: public pages, auth pages, authenticated shell layout, and routed workspace views. Reviewers inspect that file together with the listed tests to see which screens consume which primitives and which states are covered.

Every routed product surface must have exactly one consumer contract entry before the route is introduced or changed. Mark the surface `candidate` when the current implementation is intentionally transitional, but still list the primitives, states, evidence, and test files that exist today. Do not skip a surface because the UI is small; small surfaces are where drift starts cheapest.

Consumer contracts are not a substitute for use-case acceptance criteria. They prove design-system consumption and screen-state evidence only; product behavior still belongs to the owning use-case docs and tests.

## Enforceable Contract

Design-system rules should fail deterministically when they can be checked without intent.

| Rule | Mechanism |
|---|---|
| UI primitive files must have a registry contract | `python scripts/axis.py check frontend-component-composition` compares `frontend/src/components/ui/*.tsx` to `primitive-contracts.ts` |
| Primitive contracts must name readiness and test coverage | Same check verifies readiness fields, variant/state/accessibility metadata, token families, and test file paths |
| Route-bound product UI surfaces must have a consumer contract | Same check compares frontend route imports to `consumer-contracts.ts` and verifies owner, readiness, primitive/state/evidence metadata, and test files |
| Component code must use semantic color and shadow tokens | `python scripts/axis.py check frontend-style` rejects raw neutral color utilities, raw shadow utilities, and arbitrary color/gradient classes in TS/TSX |
| Token registry must match executable Tailwind mapping | `frontend/tests/design-tokens.test.ts` checks CSS variables, color, radius, shadow, and background-image mappings |

Do not add per-file allowlists for design-system violations. If a component needs a new visual value, add the token and Tailwind mapping first, then consume the named class from component code.

## Automation Boundary

The default design-system workflow is Penpot source → executable tokens/contracts → component code/tests. Do not add a `/design-system` route, component catalog, committed visual snapshot baseline, or image-comparison harness as part of routine token/component work. Reintroduce one only after an explicit Design Gate decision names the owner, review job, retention rule, trigger, and cleanup path.

New design-system guards must pass the same quality bar as repo policy guards:

| Question | Required answer |
|---|---|
| Reusable invariant? | The rule applies to a class of future changes, not one screen, class string, route, or translation key |
| Source of truth? | The checked value comes from Penpot-approved tokens, executable registries, generated contracts, or a documented owner file |
| Cheap proof? | A negative policy test can prove the bad class fails without requiring visual or product-intent judgment |

If any answer is no, keep the finding review-only in [REVIEW_FINDINGS.md](../REVIEW_FINDINGS.md) or turn the pattern into an explicit registry/contract first. Do not solve a subjective visual review problem with a brittle regex.

## Implementation Rules

- Add or update tokens before component styles that need those values.
- Add or update shared components before feature screens that need those components.
- Add or update `primitive-contracts.ts` before broad use of a new UI primitive.
- Add or update `consumer-contracts.ts` before exposing or changing a route-bound product UI surface.
- Keep route files and feature pages thin; visual geometry belongs in shared components or feature-owned pattern components.
- Do not write raw hex, HSL, arbitrary spacing, arbitrary radius, or page-local component clones when a token or shared component exists.
- Do not add visual polish to low-fidelity wireframes to compensate for missing design-system decisions.
- Treat existing UI as legacy until it is migrated through this workflow.
- Keep behavior changes in use-case PRs; keep design-system foundation and token/component work in focused PRs.

## Component Verification Coverage

Component verification proves that implementation surfaces consume the registered tokens, primitives, and states. It does not replace Penpot as the design source of truth.

| Target | Minimum coverage |
|---|---|
| Primitive tests | All documented variants and states that affect behavior, accessibility, or contract semantics |
| Responsive sizes | 360px, 768px, and 1280px unless the component needs additional sizes |
| Themes | Light and dark; system behavior verified where preference logic is involved |
| Interaction states | Hover, focus-visible, active, disabled, loading, error where applicable |
| Text stress | Long English, long Vietnamese, empty optional content, and validation copy |

Use Playwright for route and responsive product verification, and use Vitest or Testing Library for primitive behavior and accessibility assertions. Image comparison baselines are intentionally out of scope until the project explicitly reintroduces them.

## PR Sequence

Keep design-system work isolated from feature work.

| PR | Scope |
|---|---|
| 1 | Charter, source boundaries, token taxonomy, component inventory, and contract rules |
| 2 | Design-source adoption and shared design-system file structure |
| 3 | Token export/import convention and frontend token wiring |
| 4+ | Primitive components and their contract/test coverage |
| Later | Pattern/layout components, then legacy UI migration by screen or use case |

Do not migrate existing UI in the foundation PRs unless the migration is needed to prove the design-system workflow itself.
