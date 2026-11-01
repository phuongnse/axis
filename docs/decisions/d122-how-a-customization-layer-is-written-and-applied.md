# D122. How a customization layer is written and applied

Status: **Agreed**

The owner agreed this on 2026-11-01. It sets the shapes that
[D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md) left open for customization
layers, so that M25 can be broken into issues. It adds to D102 and changes one point of
[D121](d121-how-a-module-contract-is-declared-and-checked.md): a layer may add states and
transitions to a contract. Everything here is _(planned for M25)_.
[Customization layers](../reference/customization-layers.md) holds the detail.

- **One file for each changed resource:** a `layer` resource at
  `customizations/<module id>/<resource name>.json` names its `target` and holds `changes` in the
  target's own shape, with only what changes.
- **Every list element has a key:** lists merge by `name`, which becomes required wherever an
  element a layer can change lacks one, in modules and applications alike.
  - Lists with a natural key keep it: a form section's fields by `field`, a data source's group by
    `field`, and seed records by `id`.
  - Lists of plain values, such as `actions` or `to`, and expressions are one value, replaced whole.
  - Studio names each new element, and the samples, templates and built-in modules get names in the
    same change.
- **Merge rules:**
  - Objects merge by property. Named lists merge by key, and `"remove": true` removes an element.
  - An added element goes last, or `after` or `before` a named element. A changed element may move
    the same way.
  - A key or an anchor that the module lacks is a diagnostic, never a silent change.
- **Lifecycles grow, protected states stay closed:**
  - A layer adds states and transitions to a module's lifecycle, with its own guards. It never
    changes or removes the module's states and transitions.
  - The contract marks the states its promise is about as `protected`, and only the contract's own
    transitions enter them.
- **Studio writes the difference:** module resources open in their editors with the layer applied.
  Added, changed and removed elements are marked, and each can be reset to the module. The authoring
  API compares the edited resource with the pinned module version and writes only the difference.
- **Until M29:** a newer module version in range applies with the same layer by keys. A missing key
  fails the activation, so the release in use stays.

_Why:_ merging by a key for each list is how Kubernetes' strategic merge patch keeps patches
readable and safe across versions. Matching by position would move a change onto the wrong element
after any upgrade. `after` and `before` are how Odoo's view inheritance places an addition and still
upgrades its modules, and a business that cannot put a field where it wants it does not use the
layer. One file for each resource keeps diffs small, keeps two authors from editing one file, and
gives M29's conflicts a resource to sit in. A lifecycle that never grows makes a business eject a
module for one extra state. A lifecycle that grows anywhere lets an added transition go around the
module's guard. Protected states are the module's postconditions: the business adds the steps it
needs, and the outcomes the module promises still pass its guards. Writing the difference from the
edited resource, rather than editing the layer, lets Studio keep its editors and keeps the layer
minimal.
