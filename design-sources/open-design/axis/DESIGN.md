# Axis
> Category: Product
> Low-code SaaS control plane for data models, workflows, forms, and pages.

## 1. Product Character

Axis is a work-focused control plane for teams building internal workflow
applications without end-user coding. The interface should feel calm, precise,
and operational: dense enough for repeated use, but never cramped or theatrical.

Use visual hierarchy to help users compare state, choose the next action, and
recover from errors. Avoid marketing-page composition inside authenticated
workspace screens.

## 2. Visual Language

- Prefer quiet surfaces, crisp borders, readable density, and restrained
  elevation.
- Use color to signal priority and state, not as decoration.
- Use neutral work surfaces with teal primary actions, amber accents, and clear
  success, warning, error, and info states.
- Keep corners modest: controls and repeated items use small radii; panels use
  at most the shared panel radius.
- Avoid decorative gradients, large hero cards, floating orbs, and one-note
  color palettes.

## 3. Layout Principles

- Build screens around the user's active job: inspect, configure, validate,
  publish, or recover.
- Authenticated workspace screens should prioritize scanning, comparison, and
  repeated action over storytelling.
- Put primary actions near the decision point.
- Keep route-level pages thin: shared shells and feature components own visual
  structure.
- Design mobile first, then tablet and desktop. Navigation and account actions
  must remain reachable at small widths.

## 4. Typography

- Use a modern sans-serif voice with compact labels, clear headings, and short
  helper copy.
- Reserve large display type for public entry screens. Workspace panels,
  tables, drawers, and forms use restrained heading sizes.
- Do not scale type with viewport width.
- Keep letter spacing normal.
- Long English and Vietnamese labels must wrap or truncate intentionally.

## 5. Component Anatomy

Production UI must be expressed through Axis primitives:

- Commands use `Button`, `IconButton`, or `ActionLink`.
- Form controls use `Input`, `Textarea`, `Select`, `Checkbox`, `CheckboxField`,
  `Label`, and `FormField`.
- Status uses `Notice`, `Badge`, `Spinner`, `Skeleton`, `Progress`, and
  `EmptyState`.
- Work surfaces use `Panel`, `Card`, `Toolbar`, `PageHeader`, and
  `ContentGrid`.
- Domain builders use feature-owned patterns that compose primitives.

Generated artifacts may sketch layout and state coverage, but they must not
invent new production component APIs.

## 6. Interaction States

Every component or screen design should account for the states users actually
encounter:

- default
- hover and focus-visible
- active or selected
- disabled
- loading
- empty
- success
- warning
- error
- long-copy stress
- mobile, tablet, and desktop widths
- light and dark themes when the surface is theme-aware

Use text or icon cues alongside color for warning and error states.

## 7. Product Copy

Copy should be short, specific, and action-oriented. Use product language that a
workspace admin or builder understands:

- "data model"
- "workflow"
- "execution"
- "form"
- "page"
- "workspace"

Avoid exposing implementation terms such as gateway, boundary, aggregate,
module, saga, or provisioning token unless the screen is explicitly for
operators who need that detail.

## 8. Design Source Handoff

Open Design artifacts are upstream design sources. Production implementation
still follows Axis repo contracts:

- approved design direction
- semantic design tokens
- UI primitive contracts
- component tests
- route consumer contracts
- use-case acceptance criteria

Do not paste generated HTML or CSS into the SPA. Translate approved visual
decisions into Axis tokens, primitives, and feature components.

## 9. Anti-Patterns

- Do not create product behavior, acceptance criteria, roles, endpoints, or data
  contracts from a design artifact.
- Do not use raw hex values, arbitrary spacing, or one-off component clones when
  an Axis token or primitive exists.
- Do not use fake operational metrics or fake workspace data in authenticated
  screens.
- Do not use decorative panels, oversized borders, or hero layouts in dense
  operational tools.
- Do not replace editable design sources with exported SVG or bitmap-only
  artifacts.
