# D89. Modules that hold processes, pages and policies and depend on other modules

Status: **Agreed**

The owner agreed this on 2026-10-25. It replaces two points of
[D40](d40-reusable-modules.md): that processes, pages, roles and policies stay
out of a module, and that a module does not depend on other modules. Roles stay
out. Everything else in D40 holds. Everything here is _(planned for M20)_. See
[modules](../reference/modules-and-templates.md#processes-pages-policies-and-dependencies).

Role slots in `module.json` and their mapping in an application's `modules` entry are built, with `AXC0292` to `AXC0294`. Policies that name a slot are built too, with `AXC0304` and `AXC0305`. Task assignees that name a slot are not.

Module processes compile inside their module, named `<module id>/<name>` with a derived id, and see only the resources of their module and the modules it declares. Role slots in task assignees, connector slots, module packages and the engines are not built yet.

The `modules` list in `module.json`, flat resolution of the graph, one copy of each module, and the rule that the application names only modules it declares are built, with `AXC0295` and `AXC0296`. The slot maps of a dependency entry and a module naming resources of its dependencies are not.

- **New kinds in a module:** a module may also hold `process`, `page` and
  `policy` resources. It still holds no `role`, `site`, `connector`,
  `endpoint`, `trigger` or `webhook`. Those need application bindings, an
  inbound surface or a site, which a module cannot know.
  - [D103](d103-modules-hold-triggers-scenarios-and-connector-slots.md) replaces
    this point: a module also holds triggers and scenarios, and names the
    connectors it calls through connector slots.
- **Role slots instead of roles:**
  - `module.json` declares `roles`, a list of role slots, each with a `name`
    and a `label`.
  - Module policies and module task assignees name a slot. Naming anything
    else is a diagnostic.
  - The application's `modules` entry maps each slot to one of its own roles,
    as `"roles": { "approver": "Manager" }`. A slot left unmapped, or mapped to
    a role the application does not declare, is a diagnostic.
  - After the mapping, a slot is that application role. A user who holds the
    role holds the slot.
  - A module policy grants only on resources of its own module.
- **Pages:**
  - A module page is named `<module id>/<name>`, as other module resources
    are. Its widgets name resources of its own module.
  - A module holds no site. The application's site navigation lists a module
    page by that name.
- **Processes:**
  - A module process works on an entity of its own module. Its steps name
    resources of its own module, and call operations of the module's
    `packages`. It calls no connector, because a module holds none.
  - An application starts it like its own processes: from a trigger, from a
    `startProcess` action, or from a sub-process step, by the name
    `<module id>/<name>`.
  - It is pinned with the rest of the release, as every module file is.
- **Dependencies:**
  - `module.json` gets `modules`, with the same shape as in
    `application.json`. A module names resources and calls rules only of the
    modules it declares, with the same `<module id>/<name>` and
    `<module id>:<rule>(…)` forms an application uses.
  - The application resolves the whole graph flat. Each module id gets one
    version per application: the highest installed version that meets every
    range that asks for it. No such version is a diagnostic that names every
    module that asks.
  - A cycle between modules is a diagnostic.
  - A depending module maps the role slots of the module it depends on to its
    own slots, in its `modules` entry.
  - Each module gets one copy per application, with ids derived from the
    application, as D40 says. A module that two others depend on is copied
    once.
  - An application names resources only of the modules it declares itself.
- **Studio:** shows module resources read-only, as it shows module entities
  today. Studio does not edit modules.
  - [D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md)
    adds in M25 that Studio writes an application's customization layer over
    a module. Modules themselves stay read-only.
- **Upgrades:** a module upgrade must still be additive for its entities.
  Adding a process, page or policy is additive.
  - [D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md)
    replaces this point in M25: a module ships migrations.

_Why:_ a module that holds only data and rules cannot ship a working feature,
such as an approval with its screens and permissions. Role slots let a module
name who may act without knowing an application's roles, and the mapping
stays in the application, where its roles are declared. One version per module
in each application keeps the derived ids and tables of D40 unique.
Connectors, endpoints and triggers stay out, so a module never needs bindings
or an inbound surface of its own.
