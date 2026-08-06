# Detail Sections

> **Navigation**: [docs/foundations/data-display/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Provide one product-neutral section composition for detail and editor surfaces without owning their fields, data, mutations, authorization, or containing overlay.

## Consumers

- Product workflows that present one or more semantic sections for a resource or task.

## Activation

- A consumer supplies a stable ordered section description and content renderers for a detail or editor surface.

## Guarantees

- Places the localized General section first, consumer-owned business sections next, and optional user-relevant system information last.
- Omits tab semantics when only one section is available.
- Keeps section panels mounted and preserves the selected section while the containing consumer remains mounted.
- Provides localized accessible line-tab behavior that remains usable on compact layouts.

## Alternate / error flows

- Single section: render its content directly without a tablist.
- Optional section unavailable: omit it without leaving an empty tab or changing another section's identity.
- Selected section disappears after authorized data refresh: select the first available section and move focus predictably.
- Compact width: keep one horizontal tab row and allow horizontal scrolling instead of wrapping or clipping labels.
- System-only metadata has no user-facing identification, support, audit, or integration value: do not expose it.

## Acceptance Criteria

- **AC-001** The composition places localized General first, consumer-owned business sections in declared order, and optional user-relevant system information last.
- **AC-002** A surface with one section renders no tablist while preserving the same section content contract.
- **AC-003** Section panels remain mounted and the selected stable section survives container minimize, restore, and other lifecycle changes that preserve the consumer mount.
- **AC-004** Multi-section navigation uses keyboard-operable localized line tabs with programmatic selected state, predictable focus, and horizontally scrollable compact overflow.
- **AC-005** System information is absent unless it helps the user identify, support, audit, or integrate the current resource or task.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | Shared detail sections enforce semantic order, single-section behavior, mounted state, keyboard operation, compact overflow, and selected-section preservation while the consumer remains mounted | AC-001, AC-002, AC-003, AC-004 | UI component test | Yes |
| AT-002 | Browser journey | Consuming workflows expose product-owned content in the declared section and omit internal-only system data without changing their containing window behavior | AC-001, AC-005 | Browser automation | Yes |

## Out Of Scope

- Product fields, copy, validation, mutations, authorization, dirty-state policy, and system-information eligibility decisions.
- Window, dialog, drawer, route, or page lifecycle and geometry.
- Nested section hierarchies, vertical navigation, accordions, and user-configurable section order.

## Screen flow

| Surface | Required contract |
|---|---|
| Single section | Render section content without a tablist. |
| Multiple sections | Render General first, business sections next, and optional user-relevant system information last. |
| Compact layout | Keep labels on one horizontally scrollable line with visible keyboard focus and selected state. |

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Contract | Done |
> | Frontend | Done |
> | Tests | Done |
>
> **Implemented:** The shared section composition provides stable mounted panels, semantic ordering, single-section rendering, accessible line tabs, compact overflow, and selected-section preservation for current consumers.
>
> **Gaps vs spec:** None.
>
> **Deferred follow-ups:** Nested hierarchies, alternate navigation patterns, and user-configurable section order remain out of scope.
>
> **Verification:** Acceptance proof is tracked in [docs/foundations/data-display/detail-sections.evidence.md](./detail-sections.evidence.md).
>
> **Decisions:** Section composition is independent from its page or overlay container. Consumers own product meaning and content; the foundation owns only stable section identity, semantic ordering, mounted-state behavior, and accessible navigation.
