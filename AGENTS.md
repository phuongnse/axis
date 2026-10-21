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
- [Development](docs/development.md) covers running Axis locally, ports and test data: the three
  samples (purchase requests, leave requests and the service desk), the realm users for each
  sample's roles in the [development users table](docs/reference/security.md#development-users),
  the sample templates in `artifacts/templates`, the `Studio:ApplicationsPath` folder where Studio
  creates applications, and the `axis_scenario` role that runs Studio's test scenarios.

## Working rules

- Use English for code, comments, configuration, commit messages, issues and docs. The owner may
  write in Vietnamese.
- Docs are the source of truth. An issue links to the doc section it implements and does not copy
  it. A change that alters agreed behaviour, or makes any doc statement false, fixes the docs in the
  same change. Follow [keeping the docs current](docs/delivery.md#keeping-the-docs-current).
- Keep the decision status words exact: **Agreed** means agreed and **Proposed** means still open.
  Never present a proposal as agreed.
- One issue is one behaviour that fits one agent session, with 2–5 acceptance criteria that a test
  or command verifies. Its text names the concepts it touches. See
  [the life of an issue](docs/delivery.md#the-life-of-an-issue).
- Build in vertical slices. Every behaviour change needs a test: real PostgreSQL through
  Testcontainers for storage and engine behaviour, Playwright for UI journeys. Test denied access as
  well as allowed access.
- No application-specific code in the platform. Sample applications live under `samples/apps/`,
  extension packages under `samples/packages/`, modules under `samples/modules/` and templates
  under `samples/templates/`.
- Never commit secrets, tokens, local logs or machine snapshots.
- On GitHub, follow [writing on GitHub](docs/delivery.md#writing-on-github). Name the role that
  acts, never a development user.
- All work goes through NexKit, see [delivery](docs/delivery.md). Changes in paths NexKit may not
  change are hand-made pull requests.

## Commands

Run from the repository root. NexKit and CI run `setup`, then `build`, `lint`, `test`, `integration`
and `e2e` as their checks. What each script starts and writes is in
[development](docs/development.md#scripts).

| Script                   | What it does                                                               | Needs   |
| ------------------------ | -------------------------------------------------------------------------- | ------- |
| `scripts/setup.sh`       | Restores .NET and npm packages, the Playwright browser and Helm tools      | Network |
| `scripts/build.sh`       | Builds the SPA and the solution, installs the samples in `artifacts/`      |         |
| `scripts/lint.sh`        | Format, analyzer, type, Markdown link and docs structure checks, Helm lint |         |
| `scripts/test.sh`        | .NET unit tests, Vitest and the scripts' own tests                         |         |
| `scripts/integration.sh` | .NET tests and sample scenarios on real PostgreSQL and Keycloak            | Docker  |
| `scripts/e2e.sh`         | Playwright against the full stack, then the smoke tests                    | Docker  |
| `scripts/dev.sh`         | The local stack with hot reload                                            | Docker  |

While you work, run only the tests that cover your change:

- .NET unit tests: `dotnet test --project tests/<project> -c Release --filter-class '<pattern>'`
- Vitest: `npm test --prefix web -- <file>`
- Integration tests: `scripts/integration.sh --filter-class '<pattern>'`
- Playwright: `scripts/e2e.sh <spec>`

Run `scripts/build.sh` and `scripts/lint.sh` before you finish. Run `scripts/test.sh`,
`scripts/integration.sh` or `scripts/e2e.sh` without arguments at most once, at the end, and only
when the change can reach tests you did not run. NexKit's checks run every suite in full after the
session, which takes about 25 minutes.

Formatting fixes: `dotnet format Axis.slnx` and `npm run format --prefix web`.
