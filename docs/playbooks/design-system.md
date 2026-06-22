# Design System

> **Navigation**: [← docs/README.md](../README.md) · [← Design source](./design-source.md) · [← Frontend playbook](./frontend.md) · [← Wireframe playbook](./wireframes.md) · [← AGENTS.md](../../AGENTS.md)

This playbook owns the Axis design-system workflow: tokens, reusable UI components, implementation rules, and visual QA. It is separate from use-case wireframes. Wireframes describe low-fidelity flow and screen intent; the design system defines final UI decisions.

## Source Boundaries

| Source | Owns | Does not own |
|---|---|---|
| Use-case docs | Product behavior, actors, acceptance criteria, visible states | Visual style, spacing, colors, component internals |
| Wireframes | Layout intent, flow, content hierarchy, state inventory | Final theme, exact component styling, pixel-perfect approval |
| Design system | Tokens, component anatomy, variants, state matrices, visual rules | New product behavior or acceptance criteria |
| Frontend code | Executable components, accessibility behavior, responsive implementation | New visual rules not represented in the design system |
| Visual QA | Evidence that implementation matches the approved design-system target | Product acceptance by itself |

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
| Visual QA | Screenshot evidence passes the configured threshold for the approved component or screen target |

Do not mark a UI surface pixel-perfect from a single happy-path screenshot.

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
| Visual QA targets | Screenshot target names and viewport/theme coverage |

If a variant is not documented, do not infer it from a one-off screen.

## Implementation Rules

- Add or update tokens before component styles that need those values.
- Add or update shared components before feature screens that need those components.
- Keep route files and feature pages thin; visual geometry belongs in shared components or feature-owned pattern components.
- Do not write raw hex, HSL, arbitrary spacing, arbitrary radius, or page-local component clones when a token or shared component exists.
- Do not add visual polish to wireframes to compensate for missing design-system decisions.
- Treat existing UI as legacy until it is migrated through this workflow.
- Keep behavior changes in use-case PRs; keep design-system foundation and token/component work in focused PRs.

## Visual QA

Visual QA proves that implementation matches the approved design-system target.

| Target | Minimum coverage |
|---|---|
| Component catalog | All documented variants and states |
| Responsive sizes | 360px, 768px, and 1280px unless the component needs additional sizes |
| Themes | Light and dark; system behavior verified where preference logic is involved |
| Interaction states | Hover, focus-visible, active, disabled, loading, error where applicable |
| Text stress | Long English, long Vietnamese, empty optional content, and validation copy |

Use Playwright screenshot comparisons for deterministic visual checks. Component and screen tests still need behavior assertions; screenshots do not replace accessibility or user-flow tests.

## PR Sequence

Keep design-system work isolated from feature work.

| PR | Scope |
|---|---|
| 1 | Charter, source boundaries, token taxonomy, component inventory, visual QA rules |
| 2 | Design-source adoption and shared design-system file structure |
| 3 | Token export/import convention and frontend token wiring |
| 4 | Component catalog route or Storybook decision with visual QA harness |
| 5+ | Primitive components and their screenshot baselines |
| Later | Pattern/layout components, then legacy UI migration by screen or use case |

Do not migrate existing UI in the foundation PRs unless the migration is needed to prove the design-system workflow itself.
