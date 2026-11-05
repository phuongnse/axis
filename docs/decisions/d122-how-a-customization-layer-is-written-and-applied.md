# D122. How a customization layer is written and applied

Status: **Agreed**

## Context

[D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md) stores a business's changes
to a module as a layer over named elements and leaves its shapes open. A layer must stay readable,
apply to the right element after an upgrade, let a business put an addition where it wants it, and
still never let a change go around the module's contract.

## Decision

- **One file for each changed resource.** A `layer` resource at
  `customizations/<module id>/<resource name>.json` names its `target` and holds `changes` in the
  target's own shape, with only what changes.
- **Every list element has a key.** Lists merge by `name`, which becomes required wherever an element
  that a layer can change lacks one, in modules and applications alike. Lists with a natural key keep
  it, such as a form section's fields by `field` and seed records by `id`. Lists of plain values and
  expressions are one value, replaced whole.
- **Objects merge by property, lists by key.** `"remove": true` removes an element, and an added or
  changed element goes last, or `after` or `before` a named element. A key or an anchor that the
  module lacks is a diagnostic, never a silent change.
- **Lifecycles grow, protected states stay closed.** A layer adds states and transitions to a
  module's lifecycle, with its own guards, and never changes or removes the module's. The contract
  marks the states its promise is about as `protected`, and only the contract's own transitions enter
  them.
- **Studio writes the difference.** Module resources open in their editors with the layer applied,
  and each added, changed or removed element is marked and can be reset to the module. The
  authoring API compares the edited resource with the pinned module version and writes only the
  difference.

The detail is in [customization layers](../reference/customization-layers.md).

## Consequences

- Diffs stay small, two authors rarely edit one file, and an upgrade's conflicts have a resource to
  sit in.
- Every resource format gains required names, so the samples, templates and built-in modules are
  named in the same change.
- A business adds the steps and states it needs, and the outcomes the module promises still pass its
  guards.
- Studio keeps its editors, and the layer stays minimal.

## Alternatives

- **Match list elements by position.** A change would move onto the wrong element after any
  upgrade. Kubernetes' strategic merge patch merges by a key for the same reason.
- **Add elements only at the end.** A business that cannot put a field where it wants it does not use
  the layer. `after` and `before` are how Odoo's view inheritance places an addition and still
  upgrades its modules.
- **A lifecycle that never grows.** A business would eject a module for one extra state.
- **A lifecycle that grows anywhere.** An added transition could go around the module's guard.
- **Studio edits the layer itself.** Studio would need editors of its own for layers, beside the
  editors of every resource kind.
