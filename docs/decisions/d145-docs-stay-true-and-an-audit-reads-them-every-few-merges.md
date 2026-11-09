# D145. Docs stay true, and an audit reads them every few merges

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

A fourth kind came from NexKit's settings. Their values were copied into the docs and the decisions,
such as how many issues run at once and how many merges the audit waits. The owner tunes those
settings, so each change made the docs false, or needed a new decision when a decision held the value.

Sweeping every doc in every pull request would cost much and miss much. The checks have to sit where
each kind of drift starts, and the audit should run as the repository's other checks of `main` run.

## Decision

Three layers, each closing one kind of drift, and one rule for NexKit's settings. The rules are in
[how the docs stay correct](../writing-docs.md#how-the-docs-stay-correct).

- **Every pull request states its docs.** Its plan and its description name the doc sections it
  changes, or say why it changes none. It removes the planned marker of what it builds, and it searches
  the docs for every name it renames or removes. The review checks the docs against the code it
  changes.
- **A list of what the code defines is checked by a test, or not written.** A doc that lists settings,
  routes, commands, codes, kinds, environment variables, metrics or anything else the code defines has
  a test that compares the list with the code each time it runs. Without one, the doc names the file
  that holds the list instead.
- **A doc names a NexKit setting, never its value.** The docs, the decisions and the skills say which
  setting does a job and why it is set as it is. The value lives in `.nexkit/config.json` alone, so
  the owner tunes it there with no change to the docs or the decisions.
- **An audit reads the docs against the code every few merges.** A workflow, which NexKit starts on
  `main` with its `merge.after_workflows` setting as it starts CodeQL and the performance tests, reads
  a quarter of the pages in turn, so every page is read once in four runs. Its first job runs
  Claude Code with read-only tools and no GitHub token and prints the findings. Its second job runs no
  model and opens a NexKit issue for each group of findings that no open issue covers: a docs fix for
  a statement the code contradicts, and a code fix for an agreed rule the code does not follow. NexKit
  takes those issues because it trusts the issues that this workflow of the default branch opens.

## Consequences

- The drift that a pull request causes is caught in that pull request, by its review or by a test,
  before it merges.
- A test reads both sides every run, so it never holds a copy of the values that could go stale.
- A list that nobody tests is not written, so the docs link the code a little more often.
- Drift that slips past both is found within four runs of the audit, and the milestone docs sweep is
  the last check before a milestone closes. The audit runs more often when more merges, which is when
  the docs drift most.
- A change of a NexKit setting is a change to `.nexkit/config.json` alone.
- A page that tries to steer the model reaches nothing that can write: the model reads, and a job
  with no model writes.
- The audit's runs show on the repository's Actions, and its issues add to NexKit's queue.
- NexKit takes the audit's issues only while its trusted workflows setting lists the workflow, and
  only from a run on `main` that NexKit or a writer started.

## Alternatives

- **Sweep all the docs in every pull request.** Too slow and too costly at this rate of merges, and a
  reader of everything checks nothing closely.
- **Generate the lists from the code.** It cannot drift either, but it needs a generator for each list,
  and generated text reads worse than text written for a person.
- **A routine that relays the issues.** A Claude Code routine on a weekly clock, or the hourly
  driver opening the audit's issues, would work today, but a relay is one more moving part, and the
  runs would not show in the repository.
- **Rely on the milestone docs sweep alone.** A milestone runs for weeks, and the drift found in the
  rewrite had outlived several sweeps.
