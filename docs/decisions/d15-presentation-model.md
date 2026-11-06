# D15. Presentation model

Status: **Agreed**

## Context

Data sources, task forms and authorization all attach to the presentation model, so its shape comes
first, and it must let widgets and layouts be added without a change to the page format.

## Decision

- **A page is a route in a site,** and widgets are its content. A page has no entity or template of
  its own.
- **Layout is container widgets** that hold other widgets
  ([D61](d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md)).
- **A `form` resource serves both a `form` widget and a process task**
  ([D20](d20-human-tasks-task-inbox-and-forms.md)).

Widgets read data through data sources ([D18](d18-data-sources.md)), and pages open each other
through navigate actions ([D63](d63-navigate-actions-and-page-parameters.md)). The detail is in
[frontend](../reference/frontend.md).

## Consequences

- A new widget or layout needs no change to the page format.

## Alternatives

- **A page bound to an entity or a template.** Every new layout would change the page format.
