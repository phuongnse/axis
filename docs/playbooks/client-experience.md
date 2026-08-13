# Client Experience

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/frontend.md](./frontend.md) · [AGENTS.md](../../AGENTS.md)

Build the client around the user's outcome. Agents own ordinary UX decisions and ask the user only when a missing product decision could change permissions, business behavior, irreversible consequences, or the journey's meaning.

## Self-Directed Default

- Derive the experience from the owning use case, current product vocabulary, neighboring journeys, and established interaction patterns.
- Present one coherent solution instead of asking the user to choose layout, component, spacing, or decorative variants.
- When several visual approaches are valid, choose the simplest accessible pattern that represents the content relationship and verify it.
- Stop for product ambiguity; do not silently invent actor goals, side effects, authorization, destructive consequences, or new workflow states.
- Treat user feedback that exposes a reusable class as an owner or enforcement improvement, not a screen-local exception.

## Experience Contract

Define this before JSX:

| Decision | Contract |
|---|---|
| User outcome | Actor, task, success signal, and important consequence |
| Journey | Entry, primary path, alternate/recovery paths, exit, and next action |
| Mode | Read-only, editable, mixed, and transitions between them |
| State model | Initial, loading, empty, ready, dirty, validating, pending, success, unavailable, permission-denied, and failure states that apply |
| Hierarchy | Identity and task first, primary workflow second, supporting metadata last |
| Relationships | Facts, peers, sequence, causality, choice, action, result, feedback, and help |
| Content | Product vocabulary, concise labels, contextual guidance, and actionable errors |
| Quality | Keyboard and screen-reader behavior, focus, localization, compact/desktop layout, overflow, and recovery |

## Server-Owned Product Vocabulary

Apply this boundary across every Axis feature, not as a feature-local convention:

- A backend-owned business value or capability owns its localized display name, summary, usage guidance, examples, constraints, and compatibility metadata in the server contract.
- The client renders that metadata and the current server value. It must not maintain a parallel switch, enum-to-copy map, interpolated translation key, or static documentation table for backend-owned vocabulary.
- Collection labels, selected values, read-only views, contextual references, and full authoring guides resolve from the same server-owned metadata.
- Adding or removing a backend capability must require only server registry/contract work plus generated client types; missing required locale or reference metadata must fail server-side tests.
- The client still owns universal interface copy and behavior: navigation, action labels, section headings, loading/error/retry text, layout, accessibility, and interaction state.
- Do not turn the API into remote JSX or a layout schema. The server owns product meaning; the client owns presentation and interaction.

## Interaction and Composition

1. Keep one obvious primary task and visible next action; remove dead ends and competing emphasis.
2. Prefer recognition over recall, useful defaults over blank configuration, and immediate feedback over hidden state.
3. Keep one canonical reference document for the same server-owned vocabulary across read and edit modes. A semantic value opens that document at its exact entry; editable mode may add insertion actions, while read-only mode keeps them absent. Do not maintain a second focused-reference view. Reference documents group entries by meaning and provide ranked search that tolerates case, diacritics, word order, and minor spelling errors while visibly highlighting matches.
4. Use progressive disclosure only for genuinely secondary content; never hide impact, effect, validation, or required recovery.
5. Represent relationships directly: `dl` for facts, list/table for peers, timeline/stepper for sequence or causality, form controls for input, and notice for feedback.
6. Use spacing and typography for hierarchy. Add a card, border, icon, heading, badge, or container only when it communicates a distinct relationship, state, action, or boundary.
7. Reconcile the full state model after implementation so loading, empty, error, disabled, success, and stale/retry behavior remain distinct and recoverable.

## Semantic Component Selection

| Meaning | Use | Do not use |
|---|---|---|
| Lifecycle, availability, origin, or durable state | `StatusBadge` with its semantic state | Raw `Badge`, plain color, or a status-styled heading |
| Short taxonomy, capability, constraint, identifier, or syntax token scanned among peers | `MetadataTag` | A tag for a sentence, field label, section label, or isolated value |
| Numeric count attached to a control | The owning shared control's count treatment | A feature-local raw `Badge` |
| Page, section, field, step, or relationship label | Semantic heading, label, or restrained typography | Badge or tag |
| Label/value facts | `dl`/`dt`/`dd` or an existing detail pattern | A row of badges |
| User choice or action | Matching input, menu, toggle, link, or button | Badge or tag that looks selectable |
| Sequence, causality, or progress | Ordered list, timeline, or stepper | Disconnected cards or badges joined only by proximity |
| Result or effect | Plain result copy; `StatusNotice` when feedback needs prominence | Severity badge plus duplicate explanatory prose |
| Guidance or explanation | Concise prose, inline help, tooltip, or a mode-appropriate guide | Badge, tag, or authoring help on read-only views |

The badge family is only for short, non-sentence labels whose compact shape materially improves scanning among peers. It is not a generic emphasis device. Feature code classifies durable meaning as informative, positive, caution, critical, neutral, or inactive; `StatusBadge` alone maps that finite state to color and treatment, and brand color is not a status. Feature code uses `StatusBadge` or `MetadataTag`, never raw `Badge`. Feature and shared consumers use `StatusNotice` for page, form, dialog, and menu feedback; raw `Alert` remains private to that owner. If neither semantic pattern fits, choose structure or typography instead of adding another wrapper.

## Review Evidence

Record the experience contract, semantic component mapping, removed decoration, and unresolved product decisions. Use focused component evidence for states and semantics; use a realistic browser journey for task completion, recovery, keyboard behavior, compact/desktop layout, overflow, and console errors. Screenshots support review but never replace behavior evidence.
