# D133. Docs state each current rule once

Status: **Agreed**

## Context

The docs grew to about 240 files and 470,000 words, and they started to drift in ways that no check
caught:

- Decisions held three things at once: the choice and its reasons, the detailed rules that the
  reference pages also hold, and progress such as "is built" or "the owner confirmed on an issue". The
  detail and the progress went stale while the choice still held.
- A changed decision stayed in place with the status **Agreed**. Each design review added a decision
  of the kind "Rules from the X review" that changed parts of the earlier ones, so the rule in force
  could only be found by reading several files in order.
- Pages were written to pass the rules on structure rather than to be read, in clipped sentences that
  a person has to decode, and they repeated values, such as sizes in pixels, that the code owns.

## Decision

[Writing the docs](../writing-docs.md) holds the rules. In short:

- **Each kind of content has one home.** A decision holds a choice and its reasons, a reference page
  holds how Axis behaves, the roadmap holds status, GitHub and git hold progress and history, and the
  code holds the values it defines.
- **A decision is short and holds only the choice:** context, decision, consequences and the
  alternatives not taken.
- **A changed decision is replaced whole.** The new decision restates what still holds, the old file is
  deleted, and its row in the index says which decision superseded it and links its last text in git.
- **A reference page states the current rule,** never the change, and marks planned work only at a
  heading.
- **The docs are written for people,** in whole sentences that open with their purpose.
- **A design review rewrites reference pages** and adds a decision only for a choice between real
  alternatives.
- **A docs check is added only once the whole tree passes it,** with no baselines and no lists of
  exceptions, so a check never carries the problems it was meant to remove.

This decision supersedes the "Rules from" decisions of the design reviews: D96, D98, D114, D128, D129
and D132. Their rules live in the reference pages, the UI rules on the shared widgets, the shell, the
Studio and the identity provider pages.

## Consequences

- The `decisions/` folder holds only decisions in force, and a reader finds a rule in one place.
- The history of a decision is in git, one link from the index, rather than in the docs.
- Existing docs are brought to these rules area by area, Studio first, each as its own change. Until an
  area is done its pages keep the old form, and the checks that need the whole tree, such as planned
  markers only at headings, come with the last area.
- The review of each pull request and each milestone's docs sweep gain a little work: looking for an
  old rule left beside a new one.

## Alternatives

- **Keep superseded decisions with a Superseded status,** as many projects keep their architecture
  decision records. The folder would keep growing with rules no longer in force, which a reader or an
  agent can read by mistake, and git already keeps every past text.
- **Keep each review's rules in a decision of their own** and fold them into the pages later. That is
  the drift this decision ends.
- **A check for every rule now, with a baseline of today's exceptions.** A baseline hides the problems
  it lists and grows into a list nobody owns.
