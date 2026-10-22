# D8. Custom UI widgets are packaged React components

Status: **Agreed, deferred**

A custom widget is a React and TypeScript component in an extension package.

- **What it is built from:** it builds on Axis widgets, Axis theme tokens and
  the text resources. It never imports Ant Design or another UI library
  directly
  ([D58](d58-ant-design-is-a-detail-of-the-spa-without-procomponents.md)).
- **What it declares:** a schema for its properties and data bindings.
- **What it may call:** the backend only through platform APIs.
- **Not allowed:** raw HTML or JavaScript in configuration.
- **When:** after Studio.
- **Plan:** [D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md) places custom widgets in M17,
  after the page, form and process designers.

The standard widgets and form components must be good enough that custom
widgets stay rare. Each custom widget is a signal of a missing platform
capability.
