# Running tests in Studio

This page is part of [Studio](studio.md): the buttons that run an application's tests and the list of their results. [Test scenarios](test-scenarios.md) says what a scenario is.

## Running tests

Studio runs the application's [test scenarios](test-scenarios.md) through the
[test route](authoring-publishing.md#test) and lists each scenario's result.

- **Two buttons.** Run tests, on the band, sends `POST .../test` with no body, so every scenario runs.
  Run, on an open `scenario` resource, sends `{ "scenarios": ["<name>"] }`, so only that scenario runs.
  Both run the saved files, not an unsaved draft, as Validate does.
- **Who sees them.** Both show only when `GET /api/me` answers `studioTest: true`, and not when it
  answers `false` or fails. They are off while a request runs and while the reconnect message shows.
- **The status line** shows queued, running and the outcome, as for Validate: Tests passed or Tests
  failed, and never a release number.
- **Test results.** Each result reads as [a scenario's result](ai-authoring-in-studio.md#scenario-results)
  does. Each scenario of the request's `scenarios` field shows its tag in a column of its own: Passed (`success`),
  Failed (`danger`) or Skipped (`muted`). Then come its name, its kind and its time. A failure reads as one
  sentence, "Failed at step N: <message>", in the text colour. Steps count from 1, as the scenario editor's step
  cards do. The name is the only link, with its kind muted after it. A failure opens at the item's `path`, a pass
  at its top.
- **No empty pass.** While the request is queued or running, the list shows a note instead of rows, so
  an empty list never reads as a pass. A failed run with no results says that no scenario ran and
  points to the diagnostics: compile errors, a missing setting and an unknown scenario name fail a run
  this way.
- **Not kept.** The list is not kept across a reload, and the request's diagnostics go in the Problems
  panel.
