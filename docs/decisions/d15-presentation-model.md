# D15. Presentation model

Status: **Agreed**

- **Pages:** a page is a route in a site.
- **Widgets:** widgets are a page's content. A page has no entity or
  template of its own.
- **Layout:** layout comes later as container widgets, such as tabs,
  sections and columns, that hold other widgets.

- **Data:** see [D18](d18-data-sources.md).
- **Forms:** a `form` resource serves both a `form` widget and a process
  task. See [D20](d20-human-tasks-task-inbox-and-forms.md).
- **Navigation:** see
  [D61](d61-navigate-actions-and-page-parameters.md).

_Why:_ the page and widget split shapes data sources (M2), process task forms
(M3) and authorization (M4). Keeping the page free of an entity or template
lets more widgets and a layout be added without a format change.
