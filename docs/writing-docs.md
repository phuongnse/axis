# Writing the docs

The docs say what Axis is, why it is built the way it is, and exactly how it behaves. They are
written for people: the owner, a contributor who joins later, an operator who installs Axis. Agents
read the same docs, and a page that a person reads easily is also the best input for an agent. This
page says what goes where, how to write it, and how the docs stay correct while Axis grows. The
reasons are in [D133](decisions/d133-docs-state-each-current-rule-once.md).

## What goes where

Each kind of content has one home. Write a fact in its home and link to it from everywhere else.

| Content                                                        | Home                                                                                                                  |
| -------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| What Axis is, who it serves, the proving samples               | [Product brief](product-brief.md)                                                                                     |
| The vocabulary                                                 | [Concepts](domain/concepts.md)                                                                                        |
| How the parts fit together, and the pitfalls the design avoids | [Architecture](architecture.md) and [Knowledge](domain/knowledge.md)                                                  |
| A choice between real alternatives, with its reasons           | A [decision](#decisions)                                                                                              |
| How Axis behaves: configuration, APIs, screens, commands       | A [reference page](#reference-pages)                                                                                  |
| How the work is done                                           | [Delivery](delivery.md), [Development](development.md) and this page                                                  |
| How an agent applies a rule, step by step                      | A skill under `.claude/skills/`, which links the rule and does not restate it                                         |
| Milestone status                                               | The [roadmap](roadmap.md)                                                                                             |
| Who builds what, and its progress                              | GitHub issues and pull requests                                                                                       |
| What changed, when and at whose request                        | Git and GitHub                                                                                                        |
| Values that code or a tool owns: token values, sizes, settings | The code or the tool. A doc names the token, the setting or the file, and a test compares any list a doc must repeat |

So no doc says that something "is built" or "is not built yet", other than by the
[planned marker](#planned-work), and no doc holds dates of discussions, who confirmed what, issue or
pull request numbers, or how a rule used to read. Links that track work, such as the roadmap's links
to GitHub milestones, are the exception.

## Decisions

A decision records a choice that had real alternatives and is costly to undo: an architectural
choice, a public contract, a security property, a way of working. The layout of a screen, the style
of a button or the columns of a list are not decisions: they are rules on the reference page of that
screen or widget.

A decision is a file `decisions/d<n>-<slug>.md` with a row in [the index](decisions.md), and it reads
like this:

```markdown
# D<n>. <title that states the choice>

Status: **Proposed**

## Context

What forces the choice, in a few sentences.

## Decision

What was chosen, as a few rules. The detail lives in the reference pages, which this links.

## Consequences

What becomes easier, what becomes harder, and what it costs.

## Alternatives

Each option not taken, and why. Optional.
```

A decision holds only the choice and its reasons, so it stays true while the reference pages around
it change. It never holds progress, dates, names or issue numbers.

- **Proposed** is a working default that can still change, and it may be edited freely.
- **Agreed** means the owner has accepted it and work may rely on it. A wording or a link may still be
  fixed, but a change to the choice is a new decision.
- **A new decision that changes an agreed one replaces it whole.** It restates the parts that still
  hold, so the old file is never half valid. In the same change the old file is deleted, its row in
  the index names the decision, or decisions, that superseded it, as `Superseded by Dn`, with a link to
  its last text in git, and every reference page and
  link follows the new decision. A number is never used again.

## Reference pages

A reference page describes how Axis behaves, as it is, and the agreed work that is not built yet.

- **Write the current rule, never the change.** When a rule changes, rewrite it where it lives and
  delete the old text. A page does not say "no longer", "now" or "this replaces", and does not point
  at a decision that changed it.
- **One home for each rule.** Other pages link to it. A page may summarize another page's rule in a
  sentence that links it, but never restates its detail.
- **Give the reason** when a reader would ask why, in a sentence next to the rule. A longer argument
  is a decision, which the page links.
- **Leave values to the code.** Name the token (`bar`), not its value (48 px), and name the file that
  holds a list rather than copying it. When a page must list what the code defines, such as the
  [diagnostic codes](reference/diagnostic-codes.md), a test compares the two.
- **Describe screens by what a person sees and does:** what shows, in what order, what each action
  does and what is refused. Sizes, spacing and colours are the design tokens' and the
  [shared widgets](reference/shared-widgets.md)'.

### Planned work

Work that is agreed but not built yet is marked `_(planned for Mx)_`, where Mx is the milestone that
builds it, in one of two places: on a line of its own under the title of a page or the heading of a
section that is planned as a whole, so that no anchor ever changes, or right after the bold lead-in of
a single planned rule in a list. A marker never sits in the middle of a sentence, and nothing else on a page
says what is built.

A planned rule describes the agreed result. It does not also describe what Axis does until then: the
issue that builds it knows the starting point. The docs sweep of the milestone removes the marker, or
moves it to a later milestone when the work moved.

## Style

Write for a reader who knows software but not Axis.

- **Open with the purpose.** Each page, and each `##` section, starts with a sentence or two that say
  what it covers and why it matters.
- **Write whole sentences.** Join related facts with "and", "because" or "so". A sentence that must be
  decoded is too short, and one that needs a second reading is too long.
- **Use the terms of [concepts](domain/concepts.md),** and name the role that acts, never a
  development user.
- **Pick the form that fits.** A table for things with the same attributes, a numbered list for steps,
  a bulleted list for rules a reader scans, prose for an explanation. Use a bold lead-in only in a list
  of rules.
- **Show an example** when a rule is abstract: a snippet of configuration, a request and its answer,
  the words a screen shows.

For example, not "**The row stays.** The application row stays in place, as the band beside it does,
and only the sidebar's entries scroll under it." but "The application row and the band stay in place
while the page scrolls. Only the sidebar's entries scroll, under the row."

## Structure

The docs are split so that a reader, or an agent, reads only what a change needs, and so that two
pull requests rarely edit the same lines.

- **One file for each topic.** [docs/README.md](README.md) lists every file with what it holds, and a
  new file gets its row in the same change. A new topic that needs more than a few sections starts in
  a file of its own, and a table that many changes add rows to lives in a file of its own.
- **Small files.** A doc under `docs/` stays at or under 1,000 lines, and a change that grows a doc
  past 900 lines splits it, so that two changes that each add a few lines both still pass. The split
  moves whole `##` sections, unchanged, to a new file in the same folder, named after its topic. The
  base for "grows" is the `NEXKIT_BASE_SHA` commit when NexKit sets it, else the merge base with
  `origin/main`.
- **Short agent instructions.** [AGENTS.md](../AGENTS.md) holds only where to look, the working rules
  and the commands, in at most 100 lines, and `CLAUDE.md` only imports it. A skill's `SKILL.md` stays
  at or under 250 lines. A skill does not describe an external tool's behaviour that the tool's own
  docs cover at the version the repository pins.
- **Stable links.** Link to a file, or to a heading in it, never to text that changes with status.
  Renaming or moving a heading changes its anchor, so the same change fixes every link to it.

## How the docs stay correct

Docs drift in three ways: a change adds a rule and leaves the old one beside it, progress is written
into the text and goes stale, and one fact lives in two places that later disagree. These rules close
each of them.

1. **Every change updates the home of its rules.** A pull request that changes behaviour rewrites the
   section that describes it and deletes what it replaces. A pull request that makes any doc statement
   false fixes it. The review of every pull request checks that each rule it touches has one home,
   that nothing of an old rule is left, and that no progress or history entered the text.
2. **A changed decision is a new decision,** which replaces the old one as
   [Decisions](#decisions) says.
3. **A design review writes reference pages.** The docs change that ends a
   [design review](delivery.md#design-changes) rewrites the rules it agreed where they live: the
   Studio pages, the [shared widgets](reference/shared-widgets.md),
   [UI writing](reference/ui-writing.md). It adds a decision only for a choice between real
   alternatives.
4. **Status lives in the roadmap and on GitHub.** The roadmap records what each milestone delivers and
   its status. The docs mark only planned work.
5. **The threat model follows new trust boundaries.** A change that adds a new kind of route, host,
   caller, stored secret or integration updates [the threat model](reference/threat-model.md) in the
   same change, as [D106](decisions/d106-a-threat-model-kept-current-like-the-asvs-tracking.md) says.
6. **Each milestone's docs sweep tidies what the milestone touched.** Besides the planned markers, the
   sweep reads the sections that the milestone's pull requests changed, merges what is said twice,
   deletes what a later rule replaced, and checks that the decisions they link still hold only live
   choices. Its checklist is in [the life of a milestone](delivery.md#the-life-of-a-milestone).
7. **Checks catch what a machine can.** `scripts/lint.sh` runs them on every change. A docs check is
   added only once the whole tree passes it: there are no baselines and no lists of exceptions.
   - [`scripts/check-links.mjs`](../scripts/check-links.mjs) checks every relative link and anchor in
     the repository.
   - [`scripts/check-docs.mjs`](../scripts/check-docs.mjs) checks that `docs/README.md` lists every
     doc, that the decisions index matches the decision files and that a superseded row names a live
     decision, that no planned marker names a milestone the roadmap calls Done, the size limits, the
     [ASVS](reference/asvs.md#requirements) and threat model tables, and the
     [development users](reference/security.md#development-users) table against the development
     realm.
   - A doc fact that code can check, such as the diagnostic code table, has a unit test that compares
     the two and finds the table under `docs/` by its header, wherever it moves.
