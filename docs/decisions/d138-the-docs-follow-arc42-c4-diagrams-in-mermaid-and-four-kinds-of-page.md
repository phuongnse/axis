# D138. The docs follow arc42, C4 diagrams in Mermaid and four kinds of page

Status: **Agreed**

## Context

The docs are the specification that agents build Axis from. As the product grows, they will also be
what operators and application authors read. They have grown to about 240 pages with no named
standard for their shape:

- The map lists 105 reference pages in one flat table. Pages for authors, operators, Studio,
  security and the platform's internals sit side by side.
- The architecture page holds topics in no fixed order, and some of them belong to other pages.
- Apart from the threat model's data flow diagrams, the docs hold one diagram. No diagram shows a
  runtime flow or the data model.

## Decision

- **arc42 shapes the architecture page.** Its sections come in arc42's order: context and scope,
  building blocks, runtime view, deployment view, and crosscutting concepts. The other parts of
  arc42 already have homes, and the page links them rather than repeating them:
  - goals and constraints: the product brief;
  - decisions: the decisions;
  - quality goals: the knowledge page;
  - risks: the threat model;
  - glossary: the concepts.
- **Diagrams follow the C4 model and are Mermaid in the Markdown.** The architecture page holds the
  context diagram and the container diagram. A component diagram is drawn only where a container's
  parts are not obvious. A runtime flow is a sequence diagram, the data model is an entity
  relationship diagram, and an installation is a deployment diagram. A diagram lives in the page that
  owns its topic, next to the text it illustrates, and shows one view.
- **Each page is one of the four kinds of Diátaxis.** A page is a reference page, an explanation, a how-to
  guide or a tutorial, and it does not mix them. The pages are now references and explanations.
  How-to guides and tutorials are written when operators and authors outside the team need them.
- **The map groups pages by area.** The docs map groups the pages by the area that a reader works in,
  with when to read each. The files stay where they are.

The rules are in [writing the docs](../writing-docs.md#structure) and
[diagrams](../writing-docs.md#diagrams).

## Consequences

- A reader finds the area first, then the page. A page has a known shape, so a reader knows where to
  look in it.
- Each diagram is text, so a review reads its diff and an agent can read and change it. The docs
  audit reads it with its page.
- A diagram has to be kept true like any other statement, so the docs draw only the views above.

## Alternatives

- **Keep a shape of our own.** Each page then invents its own, and every new reader has to learn it.
- **Diagrams as image files**, such as draw.io or PNG. A diff of an image cannot be reviewed, and an
  agent cannot read or change one.
- **PlantUML for C4.** It needs a renderer that GitHub does not have, so the diagram shows only as
  code on GitHub.
- **Folders by Diátaxis kind or by area.** Moving about 240 files changes thousands of links for
  little gain over a map that groups them.
