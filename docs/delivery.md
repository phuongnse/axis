# Delivery workflow

Axis is built through NexKit. NexKit runs one issue per agent session, and its
autopilot moves issues on by itself, from plan to merge. How NexKit works, what
each setting does, and what a person does where NexKit stops are in NexKit's
own docs and skills, at the version that `.github/workflows/nexkit.yml` pins:
[how it works][nexkit-how], [configuration][nexkit-config],
[troubleshooting][nexkit-troubleshooting], and the [`request`][nexkit-request]
and [`drive`][nexkit-drive] skills. This file holds only what is Axis's own:
who decides what, the settings Axis chooses and why, its rules for issues and
milestones, design changes, the docs, and writing on GitHub. It puts
[D13](decisions/d13-delivery-workflow.md) into practice. Values that NexKit or
the repository settings own, such as protected paths, required checks and the
profiles' models, are not copied here. Look them up where they live.

## Who does what

- **The owner** decides **Proposed** design points, questions that nothing in
  the plan, the issue or the docs settles, whether NexKit merges its own pull
  requests, and when a milestone closes. The owner merges hand-made pull
  requests, with an admin bypass of the rules on `main` once the CI workflow
  passes. NexKit mentions the owner when a run needs a person; the owner's
  login is in NexKit's `notify` setting.
- **The local agent session** works for the owner when the owner asks. It
  writes issues and their order, checks whether a milestone is ready and
  breaks it down, proposes changes to
  the NexKit settings as hand-made pull requests, runs design reviews and
  publishes the Design System artifact in Claude from the repository, as
  [Design changes](#design-changes) describes. Its runbooks are the
  [`axis-nexkit` skill](../.claude/skills/axis-nexkit/SKILL.md) and the
  [`axis-design` skill](../.claude/skills/axis-design/SKILL.md). It does not
  review plans or merged pull requests: NexKit's review stage does.
- **The hourly driver** is a Claude Code routine that follows NexKit's
  [`drive` skill][nexkit-drive] with the
  [`axis-nexkit-routine` skill](../.claude/skills/axis-nexkit-routine/SKILL.md).
  It acts for the owner where NexKit stops for a person: the owner has
  confirmed in advance every option that NexKit, the plan, the issue or the
  docs recommend or support. It pushes the hand merges NexKit cannot push,
  makes the hand-made changes that a plan puts in paths NexKit may not change
  and opens their pull requests. It never approves or merges, and it hands the
  owner only what the owner alone can decide.
- **NexKit's agents** plan, implement, fix and review inside the NexKit
  pipeline. They read [AGENTS.md](../AGENTS.md). They do not read or write
  Claude Design.

## Decisions

These are the settings Axis chooses in `.nexkit/config.json` and on GitHub,
with the reasons. What each one does is in NexKit's
[configuration][nexkit-config].

- **Autopilot takes every open issue,** as
  [D68](decisions/d68-issues-of-several-milestones-run-at-once.md) agrees.
  NexKit's [`autopilot`][nexkit-autopilot] setting is on with its scope set to
  every open issue, so no person posts `/nexkit plan` or `/nexkit go`.
  Milestones group the work and do not order it: the issues of several
  milestones run at once, and only *blocked by* marks make one issue wait for
  another, in the same milestone or across milestones. A `bug` issue for CI on
  `main` is in no milestone.
- **Autopilot answers with the recommended option,** with `answer` set to
  `recommended`, even when the recommendation rests on a **Proposed**
  decision. The owner sees the answers on the issue and can change one with a
  comment before `/nexkit go`, or with `/nexkit fix` on the pull request.
- **Issues run in parallel unless one needs another.** Marks follow the rules
  of NexKit's [`request` skill][nexkit-request]. The milestone's docs sweep is
  marked *blocked by* every other issue of the milestone, with no reason line
  for each. At most three issues run at once, with `max_parallel` at 3.
  Pull requests that must be up to date merge one at a time, about one per
  check round. Each merge sends every waiting pull request through another
  round, and only one of them merges after it, so more issues at once mostly
  spend runner jobs, usage limits and `conflicts.max_rounds` on rounds that
  merge nothing. Each round takes three runner jobs, and CI on `main` takes
  more after each merge.
- **NexKit merges a pull request once it passes,** with
  [`merge.auto`][nexkit-auto-merge] on, because the AI review and the checks
  are the gate Axis relies on. The owner turns the setting off to decide each
  merge again, and then only the owner merges. The switch is in `.nexkit/`, so
  changing it is a hand-made pull request.
- **NexKit resolves conflicts with `main` by itself, and brings a pull
  request up to date before it merges,** with `conflicts.auto_resolve` on.
  The rules on `main` want a branch to be up to date, so a pull request's
  checks always ran on the `main` it merges into, and two pull requests that
  pass alone cannot break `main` together. When the update merges cleanly and
  the pull request's own change is the one the review approved, only the
  checks run again, not the AI review. `conflicts.max_rounds` is 6 instead of
  3, so a pull request that falls behind several merges in a row still
  merges by itself.
- **A second Claude subscription takes over at the usage limit.** NexKit
  pauses only when both are at their limit.
- **A parent issue closes when its last part does,** with
  `close_parent_issues` on.
- **The rules on `main` let NexKit merge.** They require NexKit's own checks
  on a branch that is up to date, and no approval from a person. They do not require the CI workflow, which
  runs on pushes to `main` and to hand-made branches but not on NexKit's
  branches. NexKit's checks run the same scripts there.
- **There is no push token.** NexKit pushes with the Actions token, so it
  cannot push a merge that brings changes to `.github/workflows/`. The hourly
  driver merges those by hand.
- **CI runs on `main` after every merge.** NexKit starts the CI, CodeQL and
  performance workflows on `main` through its `merge.after_workflows` setting, because a
  merge by NexKit starts no `push` workflow. A run on `main` always finishes:
  a newer merge replaces only a run that is still waiting to start, so the
  next run tests both merges. On any other branch a newer push stops the older
  run. CI runs the lint and unit tests, the smoke tests, the integration
  tests and three shards of the E2E tests as six parallel jobs, so a run takes
  as long as its slowest job.
- **The slow checks run in full only in NexKit's checks.** The smoke, integration
  and E2E checks are marked `slow`. The test, smoke, integration and E2E checks name
  their `results`: `artifacts/test-results`, and for E2E also `artifacts/logs`
  and the Playwright traces in `tests/e2e/test-results`. NexKit's
  [`evidence`][nexkit-evidence] setting names the CI workflow's
  `test-results-e2e-1` to `test-results-e2e-3`, `test-results-integration`,
  `test-results-smoke` and `test-results-unit` artifacts, which hold the Playwright traces and
  `error-context.md` files, and the performance workflow's
  `test-results-performance`.
- **NexKit's checks skip suites that a change cannot affect.** NexKit runs the
  checks in three jobs at once, with `parallel_checks` on: the build, lint, unit
  and smoke tests in one job, the group `fast`, and the integration and E2E tests
  each in their own. A check round then takes about as long as its slowest
  job, instead of all of them in a row, which took 20 to 50 minutes. Under
  NexKit's `NEXKIT_BASE_SHA`, a suite that no changed file can affect is
  skipped, and any doubt runs it. CI on `main` sets no base and runs every
  suite, so a slowdown shows after the merge. The integration performance
  tests, whose class names end in `PerformanceTests`, run neither in NexKit's
  checks nor in CI's integration job, because they depend on the runner's
  load. The performance workflow runs them on `main`, as
  [D97](decisions/d97-reliable-test-runs.md) says. Which suite skips
  when is in [test runs and reports](development.md#test-runs-and-reports).
- **NexKit restores the dependency caches that CI on `main` saves:** NuGet,
  npm and the Playwright browsers. The entries are in NexKit's
  [`cache`][nexkit-caches] setting, and the CI workflow has one
  `actions/cache` step for each, with the same paths and key files in the
  same order. A change to what setup downloads updates both in the same
  hand-made pull request.
- **Everything goes through NexKit, except changes in paths NexKit may not
  change.** Those paths are set by NexKit's `protected_paths` setting. Such
  changes are hand-made pull requests on any branch name, for example a
  NexKit version bump. Hand-made pull requests are only for those paths and
  for the docs change that ends a design review, so a design change is never
  one; see [Design changes](#design-changes). The owner
  or the hourly driver opens them, and the owner merges them.

## Profiles

Each issue runs on one NexKit profile, which triage chooses when
`/nexkit plan` starts. The profiles, their `when` texts and their models are
in NexKit's [`profiles`][nexkit-profiles] setting, and how to write profiles is
in NexKit's configuration doc. These are Axis's choices.

- **Two profiles, `standard` and `hard`, by risk and novelty.** The more a
  mistake would cost, or the less the platform has done such work before, the
  stronger the profile. `hard` is the default profile, so an issue whose
  triage fails runs on it. The cost is that more issues run on the stronger,
  dearer models.
- **`standard` names only work that repeats what the platform already does,**
  and `hard` is the rest. So the concepts and kinds of work that a new
  milestone brings land on `hard` with no change to the `when` texts. A `when`
  text may name concepts from [concepts.md](domain/concepts.md) as examples.
- **Plan and review are at least as strong as implement,** and review runs on
  a different model from implement in every profile. Triage runs on a strong
  model, because it is one call that reads only the issue, so it costs little,
  and a wrong choice does not fail: the issue just runs on the wrong profile.
  When unsure, triage chooses the stronger profile.
- **Profile changes are hand-made pull requests,** because `.nexkit/` is a
  path NexKit may not change.

## The life of an issue

An issue then follows NexKit's life of an issue: plan, questions and splits,
implement, pull request, fix and merge, as NexKit's
[how it works][nexkit-how] describes. Changing a plan after it is posted
follows NexKit's rules too. Axis adds these rules for writing it.

- **One behaviour.** One issue is one behaviour, aiming for 2–5 acceptance
  criteria that a test or command can check, as
  [D13](decisions/d13-delivery-workflow.md) says.
- **Docs and concepts.** The issue links to the doc sections it implements, is
  in the milestone that delivers it, and names the concepts it touches in its
  own text,
  in the terms of [concepts.md](domain/concepts.md), so triage can choose the
  [profile](#profiles).
- **Labels.** It gets at least one label, and every repository label whose
  description on GitHub fits its content. There is no fixed list, because the
  owner adds and removes labels.
- **Who opens it.** The owner or the local agent opens it, because autopilot
  takes only issues opened by someone with write access. It marks the issue
  *blocked by* what it must wait for, by the rules under
  [Decisions](#decisions).
- **Design.** It may link a Design artifact as a picture, while the docs stay
  the source of behaviour.
- **Exceptions.** Changes whose purpose is to edit paths NexKit may not change
  are hand-made pull requests, as described under Decisions.

## The life of a milestone

1. **Check it is ready.** When the owner asks whether a milestone can be
   broken down, the local agent runs the readiness check of
   [D68](decisions/d68-issues-of-several-milestones-run-at-once.md) and answers
   with its verdict for that milestone and the other planned ones. A milestone
   that is not ready first gets the decisions and doc sections it lacks. That
   work needs no NexKit, so it can go on while other milestones run.
2. **Break it down.** On a *ready* verdict, the local agent breaks the
   milestone into issues, as the [roadmap](roadmap.md#how-work-is-planned)
   describes, and checks the [profiles](#profiles) against the new milestone's
   concepts and the models now available. Profile fixes are hand-made pull
   requests.
3. **Deliver the issues.** Each one follows the life of an issue above.
   Autopilot runs every ready issue of every milestone at once.
4. **Docs sweep.** Before the owner closes the milestone, one issue in it
   checks the docs and fixes what the milestone made stale. It is marked
   *blocked by* every other issue of the milestone, so it runs last. It
   leaves what other milestones are still building to their own sweeps. It
   checks at least:
   - the roadmap status of this milestone and the next one;
   - the **From** column of [concepts.md](domain/concepts.md) for every
     concept listed for this milestone, moved to a later milestone when the
     concept was not delivered;
   - every *(planned for Mx)* marker for this milestone in any doc, removed
     when the work was built and moved to a later milestone when it was not;
   - the rows of [docs/README.md](README.md), which still say what each file
     holds;
   - every skill under `.claude/skills/`: its links still resolve and its
     statements still hold.
5. **Close.** When the last issue of the milestone closes, NexKit mentions
   the owner once. The owner closes the milestone on GitHub once the docs
   sweep is merged. Nothing waits for the close.

## Design changes

The repository is the source of truth for the Axis design system, and the
local agent session publishes it to Claude as a Design System artifact, as
[D57](decisions/d57-a-design-system-published-from-the-repository.md) agrees.
The token file and the preview pages are in the
[frontend reference](reference/frontend.md). The Design System artifact is at
<https://claude.ai/artifact/N1E4ohLThxhhYtnCCsJgyc>. It is private to the
owner.

- **A design change is a NexKit issue.** It links the artifact and follows the
  [life of an issue](#the-life-of-an-issue). It is never a hand-made pull
  request, because the token file and the widgets are not paths NexKit may
  not change.
- **A design change starts with the owner.** The owner asks the local session
  in words, for example "make the primary colour darker". The session changes
  the artifact first, so the owner sees the result. Then it writes a NexKit
  issue that links the artifact and states the change in Axis terms: tokens
  such as `primary` and `spacing-md`, and widgets such as `table`, `form` and
  `section`. The docs stay the source of behaviour.
- **Several changes are a design review.** When the owner reviews the design
  in words, one change at a time, the session runs a design review. Each
  change is prototyped in the real widget code on a
  `design/<yyyy-mm-dd>-<topic>` branch
  and published to the artifact, so the owner sees the real widgets. The
  branch stays local and is never merged. Before the review's issue its last
  commit gets a `design-review/<yyyy-mm-dd>-<topic>` tag with the same date
  and topic, and only the tag is pushed:
  a tag starts no CI run, and it is never deleted, so the links to the
  prototype never break.
- **The design runs ahead of the repository only by open issues and an open
  review.** While a design issue is open, the artifact holds its change and its
  last change note names the issue. While a review is open, the artifact may
  also differ by the review's changes, and its last change note names the
  review and its branch. Nothing else in the artifact differs from the
  repository.
- **A review ends in one docs change and one issue.** When the owner has
  agreed the changes, one hand-made docs pull request records the rules and
  any decision, including every change to configuration, an API or a decision.
  One NexKit issue then builds all of it and links the docs, the artifact and
  the prototype's tag. NexKit splits it if it needs to: the owner does not.
- **A sync follows the repository.** After a merged change to the tokens or a
  widget, or whenever the owner asks, the session renders the previews again
  with `scripts/design-system.sh`, reads the tokens from
  `web/src/platform/designTokens.ts` and publishes both to the same artifact.
  It keeps the changes of open design issues, and the card groups and their
  order. Before it removes anything, it lists every other difference it
  overwrote and every token the code no longer defines. A review's issue,
  once merged, is followed by a sync.
- **Only the owner and Claude edit in Claude Design.** NexKit's agents neither
  read nor write it. New screens may be drawn as Design artifacts, and an
  issue may link one as a picture, while the docs stay the source of
  behaviour.

The steps for a requested change, a design review and a sync are in the
[`axis-design` skill](../.claude/skills/axis-design/SKILL.md).

## Keeping the docs current

- **The roadmap is the only place for milestone status.**
  [roadmap.md](roadmap.md) records each milestone's status and what it
  delivers. Other docs link to it instead of repeating it.
- **One marker for planned work.** Any doc text that describes something not
  built yet carries *(planned for Mx)*, where Mx is the milestone that builds
  it. This applies to every doc, not only the architecture. The docs sweep
  removes or moves these markers when a milestone closes.
- **No false statements left behind.** A change that makes any doc statement
  false fixes it in the same change. The rule is in the
  [AGENTS.md working rules](../AGENTS.md#working-rules), so the NexKit review
  applies it to every pull request.
- **Rules and reasons, not incident history.** Docs state each rule and why
  it exists. They do not retell incidents or cite issue or pull request
  numbers as examples. That history belongs in issues, pull requests and git.
  Links to decisions and doc sections stay, and so do links that track work,
  such as the roadmap's link to a GitHub milestone.

### How the docs are structured

Agents read the docs in every plan, implement and review round, so the docs
are built to be read in parts: small files, one topic each, found from an
index. The same split keeps two pull requests from editing the same lines.

- **One home per topic.** Each topic lives in one file, and
  [docs/README.md](README.md) lists every file with what it holds. Other docs
  link to that home and do not copy it. A new file gets its row in
  `docs/README.md` in the same change.
- **One home per kind of content.** A rule and its reason live in `docs/`. How
  to apply a rule lives in a skill under `.claude/skills/`, which links to the
  rule and does not restate it. A value that a tool or a setting owns lives
  only there, and the doc or skill says where to look it up. History lives in
  issues, pull requests and git. A skill does not describe an external tool's
  behaviour that the tool's own docs, at the version the repository pins,
  already cover, because the next release of the tool makes that copy stale.
- **One decision per file.** Each decision is a file
  `docs/decisions/d<n>-<slug>.md`. It starts with `# D<n>. <title>` and a
  `Status: **Agreed**` or `Status: **Proposed**` line, and the slug is the
  title's anchor. [decisions.md](decisions.md) is the index: one row per
  decision, in number order, with the same title and status as its file. A new
  decision takes the next free number and adds its row at the end. A status
  change edits the file and its row together.
- **Small files.** A doc under `docs/` stays at or under 1,000 lines. A
  change that takes a doc past that moves whole `##` sections, unchanged, to a
  new file in the same folder, named after its topic, and links them. A new
  topic that needs more than a few sections starts in a file of its own. A
  table that many changes add rows to, such as the
  [diagnostic codes](reference/diagnostic-codes.md), lives in a file of its
  own.
- **Short agent instructions.** [AGENTS.md](../AGENTS.md) holds only where to
  look, the working rules and the commands, in at most 100 lines.
  `CLAUDE.md` only imports it. Anything longer goes to the doc it belongs to,
  such as [development.md](development.md), and AGENTS.md links to it. A
  skill's `SKILL.md` stays at or under 250 lines and holds only how to apply
  the rules it links.
- **Stable links.** Link to a file, or to a heading in it, and never to text
  that changes with status. A link to a decision links its file. Renaming or
  moving a heading changes its anchor, so the same change fixes every link to
  it.
- **Checked on every change.** `scripts/lint.sh` runs two checks.
  [`scripts/check-links.mjs`](../scripts/check-links.mjs) checks every
  relative link and anchor in the repository.
  [`scripts/check-docs.mjs`](../scripts/check-docs.mjs) checks that
  `docs/README.md` lists every doc, that the decisions index matches the
  decision files, that the [ASVS](reference/asvs.md#requirements) requirements table has a valid
  row for each listed id, and the size limits: 1,000 lines for a doc, 100 for
  AGENTS.md and 250 for a `SKILL.md` under `.claude/skills/`. A doc fact that
  code can check, such as the diagnostic code table, has a unit test that
  compares the two and finds the table under `docs/` by its header, wherever
  it moves.

## Writing on GitHub

These rules apply to issues, plans, pull request descriptions and reviews, and to the titles of
issues, pull requests and sub-issues.

- Lead with the point. Use short sentences and one idea per bullet.
- Say what changes in behaviour, and why, before naming classes or files.
- Put decisions and risks the owner must judge in their own short section.
- Keep implementation detail (signatures, SQL, file-by-file steps) out of the
  main text, or put it in a collapsed `<details>` block.
- Name the role that acts, in the terms of [concepts.md](domain/concepts.md), never a development
  user. This covers doc text that describes behaviour, and titles too. User names appear only in test
  code, test scenarios, the realm file and the [development users table](reference/security.md#development-users).
- Name a branch or a tag by what it holds. NexKit names its own branches
  `nexkit/issue-<number>`, and a Claude session in the cloud names its own,
  such as `claude/<words>`: nobody names a branch like those by hand. A
  hand-made pull request comes from `docs/<topic>` for a docs change, or from
  `ops/<topic>` for a change in a path NexKit may not change, such as a NexKit
  upgrade. A design review's prototype is a local
  `design/<yyyy-mm-dd>-<topic>` branch, and its pushed tag is
  `design-review/<yyyy-mm-dd>-<topic>`, with the same date and topic.
  `<topic>` is a few lower-case English words joined by hyphens, such as
  `widget-review`. Release tags, once Axis has them, start with `v`, so no
  other tag does.
- Keep branches from piling up. GitHub's automatic deletion of head branches
  is on, but it deletes neither the branches of the pull requests NexKit
  merges nor those of cloud sessions that push nothing. When the owner asks,
  the local session lists every `nexkit/` branch whose pull request is merged
  and every `claude/` branch with no pull request and no commit that `main`
  lacks, and the owner deletes them. A branch with an open pull request, or a
  closed one that was not merged, stays.

[nexkit-how]: https://github.com/phuongnse/nexkit/blob/main/docs/how-it-works.md
[nexkit-config]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md
[nexkit-troubleshooting]: https://github.com/phuongnse/nexkit/blob/main/docs/troubleshooting.md
[nexkit-request]: https://github.com/phuongnse/nexkit/blob/main/skills/request/SKILL.md
[nexkit-drive]: https://github.com/phuongnse/nexkit/blob/main/skills/drive/SKILL.md
[nexkit-profiles]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#profiles
[nexkit-auto-merge]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#automatic-merge
[nexkit-caches]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#caches
[nexkit-autopilot]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#autopilot
[nexkit-evidence]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#evidence-from-ci-runs
