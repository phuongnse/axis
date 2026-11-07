# Axis agent instructions

Axis is a general-purpose low-code platform. An application is configuration: Axis validates it,
publishes it as an immutable release and runs it on shared engines. The backend is C# on .NET 10,
the frontend React, TypeScript and Ant Design, and the storage PostgreSQL.

## Where to look

- [docs/README.md](docs/README.md) maps every doc and says when to read it. Read only the docs and
  sections that your change touches, not whole folders.
- For product or architecture work, read the [product brief](docs/product-brief.md), then find the
  decisions it touches in the [decisions index](docs/decisions.md) and read only those.
- [Concepts](docs/domain/concepts.md) is the vocabulary. Use its terms in code, APIs and UI.
- [Knowledge](docs/domain/knowledge.md) says why the design rules exist. Check it before you change
  configuration, execution, storage or security behaviour.
- [Development](docs/development.md) covers running Axis locally, the ports, the samples and their
  development users, and the test data.

## Working rules

### Docs and decisions

- Use English for code, comments, configuration, commit messages, issues and docs. The owner may
  write in Vietnamese.
- Docs are the source of truth, and an issue links the doc section it implements rather than copying
  it. A change keeps every doc true, as [how the docs stay correct](docs/writing-docs.md#how-the-docs-stay-correct) says.
- Keep the decision status words exact: **Agreed** means agreed and **Proposed** means still open.
  Never present a proposal as agreed.
- A comment in the code links the reference section that holds its rule, or cites a decision in force.
- Write each locale of a screen's text for its purpose, not as a translation, and name concepts by the
  [glossary](docs/reference/ui-glossary.json), see [UI writing](docs/reference/ui-writing.md#the-glossary).
- AGENTS.md holds only what [short agent instructions](docs/writing-docs.md#structure) allow.

### Code

- One concern has one mechanism. Search for what already does the job and extend it. Never copy, fork
  or work round a [shared widget](docs/reference/shared-widgets.md).
- No application-specific code in the platform. Samples, modules and templates live where
  [repository layout](docs/repository-layout.md#repository-layout) shows.
- Never commit secrets, tokens, local logs or machine snapshots.

### Tests

- Build in vertical slices. Every behaviour change needs a test, and a removal needs none, see
  [testing](docs/testing.md#rules-for-tests). Test denied access as well as allowed access.

### GitHub and NexKit

- One issue is one behaviour that fits one agent session, with 2–5 acceptance criteria that a test or
  command verifies. Its text names the concepts it touches, see
  [the life of an issue](docs/delivery.md#the-life-of-an-issue).
- On GitHub, follow [writing on GitHub](docs/delivery.md#writing-on-github). Name the role that acts,
  never a development user.
- All work goes through NexKit, see [delivery](docs/delivery.md). Only changes in paths NexKit may
  not change are [hand-made pull requests](docs/delivery.md#hand-made-pull-requests).

## Commands

Run from the repository root. NexKit and CI run `setup`, then `build`, `lint`, `test`, `smoke`,
`integration` and `e2e` as their checks. What each script starts and writes is in
[development](docs/development.md#scripts).

| Script                   | What it does                                                               | Needs   |
| ------------------------ | -------------------------------------------------------------------------- | ------- |
| `scripts/setup.sh`       | Restores .NET and npm packages, the Playwright browser and Helm tools      | Network |
| `scripts/build.sh`       | Builds the SPA and the solution, installs the samples in `artifacts/`      |         |
| `scripts/lint.sh`        | Format, analyzer, type, Markdown link and docs structure checks, Helm lint |         |
| `scripts/test.sh`        | .NET unit tests, Vitest and the scripts' own tests                         |         |
| `scripts/integration.sh` | .NET tests and sample scenarios on real PostgreSQL and Keycloak            | Docker  |
| `scripts/e2e.sh`         | Playwright against the full stack                                          | Docker  |
| `scripts/smoke.sh`       | Smoke tests of `scripts/dev.sh`, Compose, the images and on-premises       | Docker  |
| `scripts/performance.sh` | The `*PerformanceTests` classes alone, as the performance workflow runs    | Docker  |
| `scripts/dev.sh`         | The local stack with hot reload                                            | Docker  |

While you work, run only the tests that cover your change:

- .NET unit tests: `dotnet test --project tests/<project> -c Release --filter-class '<pattern>'`
- Vitest: `npm test --prefix web -- <file>`
- Integration tests: `scripts/integration.sh --filter-class '<pattern>'`
- Playwright: `scripts/e2e.sh <spec>`

Run `scripts/build.sh` and `scripts/lint.sh` before you finish. Run `scripts/test.sh`,
`scripts/integration.sh` or `scripts/e2e.sh` without arguments at most once, at the end, and only
when the change can reach tests you did not run. NexKit's checks run every suite in full after the
session, in parallel jobs.

Formatting fixes: `dotnet format Axis.slnx` and `npm run format --prefix web`.
