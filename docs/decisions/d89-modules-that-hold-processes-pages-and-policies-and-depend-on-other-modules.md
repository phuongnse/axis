# D89. Modules that hold processes, pages and policies and depend on other modules

Status: **Agreed**

## Context

A [module](d40-reusable-modules.md) that holds only data and rules cannot ship a working feature,
such as an approval with its screens and permissions. A module cannot know the roles of the
applications that use it, and a feature often builds on another, such as approvals on an
organization's departments.

## Decision

- **A module holds processes, pages and policies,** besides the kinds of D40, and triggers and
  scenarios as [D103](d103-modules-hold-triggers-scenarios-and-connector-slots.md) adds. It holds no
  `role`, `site`, `identityProvider`, `connector`, `endpoint` or `webhook`, because those need application bindings, an
  inbound surface or a site.
- **Role slots stand for roles.** `module.json` declares role slots, and module policies and task
  assignees name a slot. The application's `modules` entry maps each slot to one of its own roles,
  and a user who holds that role holds the slot. A module policy grants only on resources of its own
  module.
- **Module processes and pages work inside their module.** Their steps and widgets name resources of
  their own module, and a process calls operations of the module's packages and, as D103 adds,
  connectors through connector slots. The application starts a module process and lists a module
  page in its site navigation by the name `<module id>/<name>`.
- **A module may depend on other modules.** `module.json` declares `modules` as `application.json`
  does, and a module names resources and calls rules only of the modules it declares. A depending
  module maps the slots of a module it depends on to its own slots.
- **The application resolves the graph flat.** Each module gets one version per application, the
  highest installed version that meets every range that asks for it, and one copy with ids derived
  from the application. A cycle is a diagnostic, and the application names resources only of the
  modules it declares itself.
- **Studio never edits a module's own files.** An author's change to a module resource is saved as
  the application's customization layer, as
  [D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md) agrees, which also gives
  modules migrations for their upgrades.

The detail is in
[processes, pages, policies and dependencies](../reference/modules-and-templates.md#processes-pages-policies-and-dependencies).

## Consequences

- A module can ship an approval with its process, screens and permissions.
- The mapping of slots to roles stays in the application, where its roles are declared.
- Two modules that ask for versions of a third that no single version meets cannot be used together
  in one application.
- A module never needs bindings or an inbound surface of its own.

## Alternatives

- **Roles inside a module.** A module would have to know the roles of every application that uses
  it.
- **Several versions of one module in an application.** The ids and tables that D40 derives from
  the application would no longer be unique.
