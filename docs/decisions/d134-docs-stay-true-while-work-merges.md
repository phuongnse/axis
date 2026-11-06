# D134. Docs stay true while work merges

Status: **Agreed**

## Context

NexKit merges many pull requests a day, several at once, and each can make a doc statement false. The
rewrite of the docs to [D133](d133-docs-state-each-current-rule-once.md) found three kinds of drift that
the existing rules did not stop:

- **Planned work that was built** still carried its planned marker, because the pull request that built
  it did not touch the page.
- **Lists of what the code defines,** such as the commands of `Axis.Admin`, the host settings, the
  routes and the folders of each resource kind, missed entries or held old ones.
- **Statements nobody checked,** such as a feature described as offered that no code offers, or a
  decision that promised what nothing builds.

Sweeping every doc in every pull request would cost much and miss much. The checks have to sit where
each kind of drift starts.

## Decision

Three layers, each closing one kind of drift. The rules are in
[how the docs stay correct](../writing-docs.md#how-the-docs-stay-correct).

- **Every pull request states its docs.** Its plan and its description name the doc sections it
  changes, or say why it changes none. It removes the planned marker of what it builds, and it searches
  the docs for every name it renames or removes. The review checks the docs against the code it
  changes.
- **A list of what the code defines is checked by a test, or not written.** A doc that lists settings,
  routes, commands, codes, kinds, environment variables, metrics or anything else the code defines has
  a test that compares the list with the code each time it runs. Without one, the doc names the file
  that holds the list instead.
- **A weekly audit reads the docs against the code.** A routine reads a share of the pages each week,
  in turn, so every page is read about once a month. It opens a NexKit issue for what it finds: a
  docs fix for a statement the code contradicts, and a code fix for an agreed rule the code does not
  follow. The audit itself changes nothing, so all work still goes through NexKit.

## Consequences

- The drift that a pull request causes is caught in that pull request, by its review or by a test,
  before it merges.
- A test reads both sides every run, so it never holds a copy of the values that could go stale.
- A list that nobody tests is not written, so the docs link the code a little more often.
- Drift that slips past both is found within about a month, and the milestone docs sweep is the last
  check before a milestone closes.
- The audit spends a routine's runs, and its issues add to NexKit's queue.

## Alternatives

- **Sweep all the docs in every pull request.** Too slow and too costly at this rate of merges, and a
  reader of everything checks nothing closely.
- **Generate the lists from the code.** It cannot drift either, but it needs a generator for each list,
  and generated text reads worse than text written for a person.
- **Rely on the milestone docs sweep alone.** A milestone runs for weeks, and the drift found in the
  rewrite had outlived several sweeps.
