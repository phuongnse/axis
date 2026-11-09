# Delivery workflow

Axis is built through NexKit. NexKit runs one issue per agent session, and its autopilot moves issues
on by itself, from plan to merge. How NexKit works, what each setting does, and what a person does
where NexKit stops are in NexKit's own docs and skills, at the version that
`.github/workflows/nexkit.yml` pins: [how it works][nexkit-how], [configuration][nexkit-config],
[troubleshooting][nexkit-troubleshooting], and the [`request`][nexkit-request] and
[`drive`][nexkit-drive] skills. This page holds only what is Axis's own: who decides what, the
settings Axis chooses and why, its rules for issues and milestones, design changes, and writing on
GitHub. The reasons for the workflow are in [D13](decisions/d13-delivery-workflow.md). Values that
NexKit or the repository settings own, such as protected paths, required checks and the profiles'
models, are not copied here: look them up where they live.

## Who does what

Four actors take part in the work, and each has its own part.

- **The owner** decides **Proposed** design points, questions that nothing in the plan, the issue or
  the docs settles, whether NexKit merges its own pull requests, and when a milestone closes. The
  owner merges hand-made pull requests, see [hand-made pull requests](#hand-made-pull-requests), and
  vouches with [`/nexkit trust`][nexkit-trust] for an issue that autopilot leaves alone, after reading
  it. NexKit mentions the owner when a run needs a person, through its `notify` setting.
- **The local agent session** works for the owner when the owner asks. It writes issues and their
  order, checks whether a milestone is ready and breaks it down, proposes changes to the NexKit
  settings as hand-made pull requests, runs design reviews and publishes the Design System artifact in
  Claude from the repository, as [design changes](#design-changes) describes. Its runbooks are the
  [`axis-nexkit` skill](../.claude/skills/axis-nexkit/SKILL.md) and the
  [`axis-design` skill](../.claude/skills/axis-design/SKILL.md). It does not review plans or merged
  pull requests, because NexKit's review stage does.
- **The hourly driver** is a Claude Code routine that follows NexKit's [`drive` skill][nexkit-drive]
  with the [`axis-nexkit-routine` skill](../.claude/skills/axis-nexkit-routine/SKILL.md). It acts for
  the owner where NexKit stops for a person, and may take every option that NexKit, the plan, the
  issue or the docs recommend or support, because the owner has accepted those in advance. It merges
  by hand the conflicts in paths NexKit may not change, makes the hand-made changes that a plan puts
  in those paths, and opens their pull requests. It never approves or merges, and it hands the owner only
  what the owner alone can decide.
- **The docs audit** is the `docs-audit.yml` workflow, which follows the
  [`axis-docs-audit` skill](../.claude/skills/axis-docs-audit/SKILL.md). It reads a quarter of the docs,
  the skills and AGENTS.md against the code every few merges and opens a NexKit issue for each group
  of findings that no open issue covers, code and test fixes first and at most ten a run, as
  [D145](decisions/d145-docs-stay-true-and-an-audit-reads-them-every-few-merges.md) agrees and the
  skill says. It changes no file itself.
- **NexKit's agents** plan, implement, fix and review inside the NexKit pipeline. They read
  [AGENTS.md](../AGENTS.md), and they neither read nor write Claude Design.

## Decisions

These are the settings Axis chooses in `.nexkit/config.json` and on GitHub, each with its reason.
What each setting does is in NexKit's [configuration][nexkit-config], and its value is in the file
that holds it.

### How issues run

- **Autopilot takes every open issue,** as
  [D68](decisions/d68-issues-of-several-milestones-run-at-once.md) agrees. NexKit's
  [`autopilot`][nexkit-autopilot] setting is on with its scope set to every open issue, so no person
  posts `/nexkit plan` or `/nexkit go`. Milestones group the work and do not order it: the issues of
  several milestones run at once, and only *blocked by* marks make one issue wait for another, in the
  same milestone or across milestones. A `bug` issue for CI on `main`, an issue for a Dependabot
  advisory and an issue of the docs audit are in no milestone.
- **Bugs go first, then the work on how Axis is built.** Autopilot's [`priority`][nexkit-labels]
  setting lists the labels whose issues start before the others, in this order: `nexkit:ci` for a
  failure of CI on `main`, `nexkit:dependabot` for an advisory, `bug`, `nexkit:flaky` for a flaky
  test, `process`, and `refactor`. NexKit labels the issues it opens. A `bug` issue that a person
  opens goes first too, with or without a milestone, so a broken `main` never waits behind a
  milestone's features. A `process` issue improves how Axis is built: the docs standard, the checks,
  CI, the delivery workflow or the agent instructions. A `refactor` issue moves one concern to one
  shared mechanism and keeps its behaviour. Both go before the milestones' features, because every
  later issue is built with what they change, and a feature built first would add one more copy to
  move.
- **A held issue waits for the owner.** An issue with the `on-hold` label, which the `labels` setting
  makes a hold, gets no plan, no round and no merge until the label is removed. The owner holds an
  issue to stop work on it without closing it. A [design issue](#design-changes) is held until its
  review ends.
- **Autopilot takes only trusted issues.** An issue is trusted when a person with write access opens
  it, or when NexKit's App opens it: for a failure of CI on `main`, a flaky test, a Dependabot advisory
  or a part of a split. The docs audit's issues are trusted too, because autopilot's
  [`trusted_workflows`][nexkit-workflow-issues] setting lists `docs-audit.yml`. NexKit checks that the
  issue came from a run of that workflow on `main` that NexKit or a writer started, never a pull
  request. Any other issue waits until the owner reads it and comments
  [`/nexkit trust`][nexkit-trust] on it.
- **Autopilot opens and takes an issue for each Dependabot advisory** of a high or critical alert,
  as [D107](decisions/d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md) agrees.
  Its [`dependabot`][nexkit-dependabot] setting holds the lowest severity it takes.
  NexKit's App reads the alerts with its *Dependabot alerts: read* permission.
- **Autopilot answers with the recommended option,** with its `answer` setting, even when the
  recommendation rests on a **Proposed** decision, so a question never stops an issue. The owner sees
  the answers on the issue and can change one with a comment before `/nexkit go`, or with
  `/nexkit fix` on the pull request.
- **Issues run in parallel unless one needs another.** Marks follow the rules of NexKit's
  [`request` skill][nexkit-request]. A milestone ends in two waves, which the `labels` setting gives to
  two labels, the journey's first: its journey carries `journey`, so it starts once every other issue
  of the milestone is closed, and its docs sweep carries `docs-sweep`, so it starts once the journey
  is closed too. Issues opened later and the parts of a split count without any *blocked by*
  mark. Autopilot's `max_parallel` setting caps how many issues run at once, so that their agent
  sessions fit in the Claude usage limits and their rounds of checks in the runner jobs. The owner
  tunes it, as [D144](decisions/d144-branches-need-not-be-up-to-date.md) agrees.
- **A second Claude subscription takes over at the usage limit,** as the fallback agent in
  `agents`, so NexKit pauses only when both are at their limit.
- **A parent issue closes when its last part does,** with `close_parent_issues` on.

### How pull requests merge

- **NexKit merges a pull request once it passes,** with [`merge.auto`][nexkit-auto-merge] on, because
  the AI review and the checks are the gate Axis relies on. The owner turns the setting off to decide
  each merge again, and then only the owner merges. The switch is in `.nexkit/`, so changing it is a
  hand-made pull request.
- **NexKit resolves conflicts with `main` by itself,** with `conflicts.auto_resolve` on. The rules on
  `main` do not want a branch to be up to date, as
  [D144](decisions/d144-branches-need-not-be-up-to-date.md) agrees, so a
  pull request merges once its checks pass on its own branch, and a merge does not send the waiting
  pull requests through another round. Two pull requests that pass alone can break `main` together,
  and CI on `main` finds it. `conflicts.max_rounds` is raised above NexKit's default, so a pull
  request that conflicts with several merges in a row still merges by itself.
- **The rules on `main` let NexKit merge.** They require NexKit's own checks, no approval from a
  person and no branch that is up to date. They do not require the CI workflow, which runs on `main`
  alone, because NexKit's checks run the same scripts on every pull request.
- **The owner guards the security checks.** A hand-made `.github/CODEOWNERS` and a rule the owner sets
  make a pull request that touches a security check, its exceptions or the authentication default
  need the owner's review, as
  [D137](decisions/d137-authentication-by-default-and-the-owner-guards-the-security-checks-alone.md)
  agrees. NexKit cannot merge it until the owner approves it, and then NexKit merges it. The owner checks
  one thing: whether the change accepts a risk, adds an exception or loosens a check, and why.
- **A change adds no exception to a check and loosens none:** no new entry in an accepted list or
  rise of a baseline count, and no rule removed, narrowed or skipped in a check script under
  `scripts/` or its test. The review checklist refuses such a change, and NexKit then mentions the
  owner, as [D137](decisions/d137-authentication-by-default-and-the-owner-guards-the-security-checks-alone.md)
  agrees.
- **Every plan and review answers Axis's checklist.** NexKit's [`review.checklist`][nexkit-checklist]
  setting holds it. Each item names one doc section that holds rules a review can check in the diff,
  summarizes it in a sentence and asks the review to check the diff against each of its rules. The
  rules stay in their section, so a new rule adds no item. A rule that a script or a test can check is
  checked there, not by the review. An item not met requests changes like a blocking finding, so a pull
  request that leaves a doc false or loosens a check does not merge.
- **NexKit acts as the owner's [GitHub App][nexkit-app],** which its `github_app` setting names. The owner keeps one
  App for every repository that uses NexKit and installs it on this one, and its id and private key
  are repository secrets. Its pushes and merges count like a person's: they start workflows, and
  NexKit merges changes to `.github/workflows/` itself. Its agents still may not change `.github/` or
  `.nexkit/`, and the jobs that run an agent or Axis's code hold neither the key nor a token. The
  hourly driver merges by hand only a conflict in those paths.

### CI on main

- **NexKit starts CI, CodeQL, the performance workflow and the docs audit on `main` every few
  merges,** with its `merge.after_workflows` setting, which holds how many merges each waits, as
  [D146](decisions/d146-ci-runs-on-main-every-few-merges-and-nexkit-checks-every-branch.md) and
  [D145](decisions/d145-docs-stay-true-and-an-audit-reads-them-every-few-merges.md) agree. CI waits
  the fewest, then CodeQL and the performance workflow. The docs audit waits the most, because each
  run costs a model session and its issues add to NexKit's queue.
  None of them runs on a push to `main` or has a schedule of its own for this, so a merge does not
  start them too, and without the setting nothing would test `main`. CI also runs on demand, and on a
  pull request that changes its own workflow file. No push to another branch starts it, because
  NexKit's checks run on every pull request.
- **A failure on `main` is noticed when its run ends.** The `workflow_run` trigger of NexKit's
  workflow names each of these workflows, so the end of a run on `main` starts a NexKit sweep.
- **A CI run on `main` always finishes.** A newer run replaces only one that is still waiting to
  start. On any other branch a newer push stops the older run.
- **CI's jobs come from [NexKit's checks](#nexkits-checks):** each check, or `group` of checks, in
  `.nexkit/config.json` is one parallel job that runs the check's commands with its timeout and
  uploads its `results` folders as the artifact `test-results-<name>`. A run takes as long as its
  slowest job, and adding, removing or resharding a check changes CI with it.

### NexKit's checks

- **The checks run in parallel jobs,** with `parallel_checks` on: the build and lint share one job,
  the group `build-lint`, and the unit tests, the smoke tests and each integration and E2E shard get
  one each. A check round then takes about as long as its slowest job, instead of all of them in a
  row. A check gets a job of its own when sharing one made that job the slowest, and the integration
  and E2E suites are split into shards because one job for either came close to the check's
  `timeout_minutes`. [CI on main](#ci-on-main) runs the same checks as jobs of the same names, so
  NexKit matches a failed check with the CI job that failed on `main`.
- **The slow checks are marked `slow`, and the checks name their `results`,** so NexKit keeps what a
  failure needs: the JUnit files, and for E2E the logs and the Playwright traces.
- **NexKit reads CI's evidence.** Its [`evidence`][nexkit-evidence] setting names the test result
  artifacts of the CI and performance workflows, which hold the Playwright traces and
  `error-context.md` files, so a fix sees why `main` failed.
- **A check skips the suites that a change cannot affect,** so a round after a small change is short.
  A suite runs unless no file changed since the check last passed can affect it, and any doubt runs
  it. In the automatic review that brings a branch up to date, NexKit goes further: the smoke,
  integration and E2E checks name the paths they never read in NexKit's
  [`unaffected_by`][nexkit-unaffected] setting, and a check whose paths did not change since it passed
  gets no job at all. CI on `main` sets no base and runs every suite, so a slowdown shows at its next
  run. Which suite skips when, and the commit it compares with, are in
  [test runs and reports](development.md#skipped-suites).
- **The performance tests run on `main` only,** in the performance workflow, and neither in NexKit's
  checks nor in CI's integration jobs, because they depend on the runner's load, as
  [D97](decisions/d97-reliable-test-runs.md) says.
- **NexKit restores the dependency caches that CI on `main` saves:** NuGet, npm and the Playwright
  browsers. The entries are in NexKit's [`cache`][nexkit-caches] setting, and each workflow that runs
  setup restores them with NexKit's cache action, which reads the same entries and saves them on
  `main`. A change to what setup downloads changes the entries in a hand-made pull request.

### Hand-made pull requests

- **Everything goes through NexKit, except changes in paths NexKit may not change.** NexKit's
  `protected_paths` setting names those paths. Such changes are hand-made pull requests on any branch
  name, for example a NexKit version bump. Hand-made pull requests are only for those paths. A design
  change and the docs change that ends a design review are NexKit issues, see
  [design changes](#design-changes). Work that waits for one follows [follow-ups](#the-life-of-an-issue).
  The owner or the hourly driver opens them, and the owner merges them.
- **NexKit checks and reviews them on request.** Whoever pushes to one comments
  [`/nexkit review`][nexkit-person-review] on it after the push. NexKit runs its checks and its review
  on the head and sets the two statuses that the rules on `main` require, and it pushes nothing and
  merges nothing there. The owner merges once both are green, with no admin bypass. The bypass is left
  for a pull request that needs the owner's code-owner review and that the owner opened, since GitHub
  does not let an author approve their own pull request.

## Profiles

Each issue runs on one NexKit profile, which triage chooses when `/nexkit plan` starts. The profiles,
their `when` texts and their models are in NexKit's [`profiles`][nexkit-profiles] setting, and how to
write profiles is in NexKit's configuration doc. These are Axis's choices.

- **Two profiles, `standard` and `hard`, by risk and novelty.** The more a mistake would cost, or the
  less the platform has done such work before, the stronger the profile. `hard` is the default
  profile, so an issue whose triage fails runs on it. The cost is that more issues run on the
  stronger, dearer models.
- **`standard` names only work that repeats what the platform already does,** and `hard` is the rest.
  So the concepts and kinds of work that a new milestone brings land on `hard` with no change to the
  `when` texts. A `when` text may name concepts from [concepts.md](domain/concepts.md) as examples.
- **Plan and review are at least as strong as implement,** and review runs on a different model from
  implement in every profile. Triage runs on a strong model, because it is one call that reads only
  the issue, so it costs little, and a wrong choice does not fail: the issue just runs on the wrong
  profile. When unsure, triage chooses the stronger profile.
- **Profile changes are hand-made pull requests,** because `.nexkit/` is a path NexKit may not change.

## The life of an issue

An issue follows NexKit's life of an issue: plan, questions and splits, implement, pull request, fix
and merge, as NexKit's [how it works][nexkit-how] describes. Changing a plan after it is posted
follows NexKit's rules too. Axis adds these rules for writing it.

- **One behaviour.** One issue is one behaviour, aiming for 2–5 acceptance criteria that a test or
  command can check, as [D13](decisions/d13-delivery-workflow.md) says. More than five is a signal to
  check whether the issue holds more than one behaviour, and if it does, it is split. If every
  criterion checks the same behaviour, more are fine. A plan is not revised only to cut the number of
  criteria.
- **Criteria the review can verify.** The review verifies each acceptance criterion from what it
  sees: the diff, and the output of the checks that ran on the head. A criterion about a measure,
  such as a time, a size or a count, is met by a check that fails past the limit, and the
  criterion names that check. For example, "the test check fails when Axis.Server.Tests runs
  longer than 90 s" can be verified, while "Axis.Server.Tests finishes under 90 s" cannot, because
  no check reports the time against the limit.
- **Docs and concepts.** The issue links to the doc sections it implements, is in the milestone that
  delivers it, and names the concepts it touches in its own text, in the terms of
  [concepts.md](domain/concepts.md), so triage can choose the [profile](#profiles). Its plan and its
  pull request then carry the `Docs:` line of
  [how the docs stay correct](writing-docs.md#how-the-docs-stay-correct).
- **Labels.** It gets at least one label, and every repository label whose description on GitHub fits
  its content. There is no fixed list, because the owner adds and removes labels.
- **Who opens it.** The owner or the local agent opens it. Which issues autopilot takes is in
  [how issues run](#how-issues-run). The issue marks what it must wait for with *blocked by*, by the
  rules under [Decisions](#decisions).
- **Design.** It may link a Design artifact as a picture, while the docs stay the source of
  behaviour.
- **Follow-ups.** Work that must come after other work is opened as an issue when the work it follows
  is agreed, and it is marked *blocked by* that work's issue. A pull request, a comment or a session's
  notes never holds a follow-up. A change in a path NexKit may not change is an issue like any other:
  its plan lists the change under *Needs a hand-made change*, and the hourly driver makes it as a
  [hand-made pull request](#hand-made-pull-requests). A hand-made pull request that other work waits
  for closes an issue of its own with the `on-hold` label, which [how issues run](#how-issues-run)
  makes a hold, and the follow-ups are *blocked by* that issue.

## The life of a milestone

A milestone goes from a readiness check to its close in five steps.

1. **Check it is ready.** When the owner asks whether a milestone can be broken down, the local agent
   runs the readiness check of [D68](decisions/d68-issues-of-several-milestones-run-at-once.md). It
   checks that every item of the milestone's "Done when" text has **Agreed** decisions and doc
   sections that issues can link to; whether each thing the milestone needs is in the code, built by
   an open issue, or built by nobody; and which concepts, docs and code it shares with the milestones
   that have open issues. It answers *ready*, *ready with waits*, naming which issues wait for which,
   or *not ready*, naming what has to exist first. It gives this verdict for that milestone and the
   other planned ones of the current phase of the [roadmap](roadmap.md#milestones), because the
   roadmap lets them change order. A milestone of a later phase is checked only when the owner asks.
   A milestone that is not ready first gets the decisions and doc sections it lacks, as NexKit docs
   issues, which run while other milestones run.
2. **Break it down.** On a *ready* verdict, the local agent breaks the milestone into issues, as the
   [roadmap](roadmap.md#how-work-is-planned) describes, and checks the [profiles](#profiles) against
   the new milestone's concepts and the models available, and sets the milestone's roadmap status to
   In progress, in a NexKit issue. Profile fixes are hand-made pull requests. The issues of
   new screens wait for a design issue, see [design changes](#design-changes).
3. **Deliver the issues.** Each one follows the life of an issue above. Autopilot runs every ready
   issue of every milestone at once.
4. **Docs sweep.** Before the owner closes the milestone, one issue in it checks the docs and fixes
   what the milestone made stale. It carries the `docs-sweep` label, so it runs once every
   other issue of the milestone is closed, the journey included. It leaves what other milestones are still building to their own sweeps. It checks at
   least:
   - the roadmap status of this milestone and the next one;
   - the **From** column of [concepts.md](domain/concepts.md) for every concept listed for this
     milestone, moved to a later milestone when the concept was not delivered;
   - every *(planned for Mx)* marker for this milestone in any doc, removed when the work was built
     and moved to a later milestone when it was not;
   - the doc sections that the milestone's pull requests changed: what is said twice is merged into
     its one home, what a later rule replaced is deleted, and the decisions they link hold only
     choices still in force;
   - the rows of [docs/README.md](README.md), which still say what each file holds;
   - every skill under `.claude/skills/`: its links still resolve and its statements still hold.
5. **Close.** When the last issue of the milestone closes, NexKit mentions the owner once. The owner
   closes the milestone on GitHub once the docs sweep is merged. Nothing waits for the close.

## Design changes

The repository is the source of truth for the Axis design system, and the local agent session
publishes it to Claude as a Design System artifact, as
[D57](decisions/d57-a-design-system-published-from-the-repository.md) agrees. The token file and the
preview pages are in the [frontend reference](reference/frontend.md) and
[widget previews](reference/widget-previews.md). The Design System artifact is at
<https://claude.ai/artifact/N1E4ohLThxhhYtnCCsJgyc>, private to the owner. The steps for a requested
change, a design review and a sync are in the
[`axis-design` skill](../.claude/skills/axis-design/SKILL.md).

- **A design change is a NexKit issue.** It links the artifact and follows the
  [life of an issue](#the-life-of-an-issue). It is never a hand-made pull request, because the token
  file and the widgets are not paths NexKit may not change.
- **A design change starts with the owner.** The owner asks the local session in words, for example
  "make the primary colour darker". The session changes the artifact first, so the owner sees the
  result. Then it writes a NexKit issue that links the artifact and states the change in Axis terms:
  tokens such as `primary` and `spacing-md`, and widgets such as `table`, `form` and `section`. The
  docs stay the source of behaviour.
- **A new screen is designed first,** as
  [D140](decisions/d140-a-new-screen-is-designed-first-on-a-short-prototype-with-fixtures.md) agrees. A
  new screen or a new way of interacting gets a design review before it is built. A screen made of
  agreed patterns needs none. Each group of new screens has one design issue with the `on-hold`
  label, so NexKit does not start it until the review ends, and the issues that build those screens are
  *blocked by* it. The open design issues are the queue of reviews.
- **Several changes are a design review.** When the owner reviews the design in words, one change at
  a time, the session prototypes each change in the real widget code on a
  `design/<yyyy-mm-dd>-<topic>` branch, which holds only what the design needs, and the owner sees it
  in the [widget previews](reference/widget-previews.md), on fixtures. A review covers one screen
  or one group of screens, and what it finds outside them becomes an issue. The branch is pushed, so
  the review can go on from any machine, and is never merged. No push to a branch starts CI, as
  [D146](decisions/d146-ci-runs-on-main-every-few-merges-and-nexkit-checks-every-branch.md)
  agrees. A prototype is never merged, and it never gets `/nexkit review`, because it changes the widgets
  without their tests. When the review ends, the branch's last commit gets a
  `design-review/<yyyy-mm-dd>-<topic>` tag with the same date and topic, which is pushed and never
  deleted, so the links to the prototype never break. A tag starts no CI run. The branch is then
  deleted, locally and on GitHub.
- **A prototype starts from `main` and changes only the SPA.** It never starts from another review's
  tag: a screen that an earlier review's issues have not built yet waits for them. It changes no
  server code. What a screen needs from an API is a fixture in the prototype and a contract in the
  review's rule comments on the design issue, and NexKit builds it. When `main` changes code the
  prototype touches, the session merges `main` into the branch and never rebases it, and `main` stays
  the standard for behaviour and the prototype the standard for its look.
- **The design runs ahead of the repository only by open issues.** While a design issue is open, the
  artifact holds its change and its last change note names the issue. A review does not change the
  artifact: it is published once the review's issues are merged, so nothing else in the artifact
  differs from the repository.
- **A review's agreed rules are comments on its design issue.** The design issue stays short enough to
  read in one pass. Its body holds the review's scope and what the build must do beyond the docs,
  because NexKit plans from the body. One comment holds the review's state: the prototype branch, what
  is done, what waits for the owner, what comes next and each change's parts list. Before it builds a
  change, the session lists each visible part with the shared widget, helper or token that draws it, as
  [which widget](reference/shared-widgets.md#which-widget) shows, and marks **new** each mechanism the
  change adds, so the owner decides only on what is new. The session edits that comment in place and
  never adds a second. The rules the owner agrees about one screen, or in one step of the review, share
  one English comment. Each rule has its own bold heading, the doc page where it lives, any API contract
  the screen needs and the prototype commit, so NexKit finds each rule. A later rule about the same
  screen is added to that comment by an edit. A rule that changes while the issue has `on-hold` is
  edited in its comment. NexKit reads the issue only once the label comes off, and GitHub keeps each
  comment's edit history. There is no docs branch, and no other file holds these rules.
- **A review ends when the design issue's `on-hold` label comes off.** Before that, the session tags the
  prototype and comments the tag on each issue that waits for the design issue. A screen that is already
  built has no such issue, so the session first writes one build issue *blocked by* the design issue.
  Then it removes the label.
- **NexKit writes the docs change once the label is removed.** It plans the design issue like any
  issue and writes each rule where it lives, marked as planned, as
  [writing the docs](writing-docs.md#planned-work) says. The rules
  go in the reference pages, configuration and APIs included. It adds a decision only for a choice
  between real alternatives. Its pull request closes the design issue, which starts the issues that
  wait for it. Those issues build the screens, link the docs and the tag, and remove the planned markers
  they build. NexKit splits an issue if it needs to.
- **A sync follows the repository.** After a merged change to the tokens or a widget, or whenever the
  owner asks, the session renders the previews again with `scripts/design-system.sh`, reads the
  tokens from `web/src/platform/designTokens.ts`, and publishes both to the same artifact. It keeps
  the changes of open design issues, and the card groups and their order. Before it removes anything,
  it lists every other difference it overwrote and every token the code does not define. A review's
  issue, once merged, is followed by a sync.
- **Only the owner and Claude edit in Claude Design.** NexKit's agents neither read nor write it. New
  screens may be drawn as Design artifacts, and an issue may link one as a picture, while the docs
  stay the source of behaviour.

## The docs

The docs follow [writing the docs](writing-docs.md): what goes in which doc, how to write it, and how
the docs stay correct while the work goes on.

## Writing on GitHub

These rules apply to issues, plans, pull request descriptions and reviews, and to the titles of
issues, pull requests and sub-issues.

- Lead with the point. Use short sentences and one idea per bullet.
- Say what changes in behaviour, and why, before naming classes or files.
- Put decisions and risks the owner must judge in their own short section.
- Keep implementation detail (signatures, SQL, file-by-file steps) out of the main text, or put it in
  a collapsed `<details>` block.
- Name the role that acts, in the terms of [concepts.md](domain/concepts.md), never a development
  user. This covers doc text that describes behaviour, and titles too. User names appear only in test
  code, test scenarios, the realm file and the
  [development users table](reference/security.md#development-users).
- Write a closing keyword before an issue number only when the pull request closes that issue.
  GitHub reads `close #12`, `fixes #12` or `resolves #12` as closing it even in a negation such as
  "does not close #12", and the merge then closes the issue with its work undone. To say that a
  pull request leaves an issue open, write "#12 stays open".
- Name a branch or a tag by what it holds:

  | Branch or tag                         | What it holds                                              | Who names it   |
  | ------------------------------------- | ---------------------------------------------------------- | -------------- |
  | `nexkit/issue-<number>`               | A NexKit issue's work                                      | NexKit         |
  | `claude/<words>`                      | A Claude session in the cloud                              | The session    |
  | `ops/<topic>`                         | A change in a path NexKit may not change, such as a NexKit upgrade | A person |
  | `design/<yyyy-mm-dd>-<topic>`         | A design review's prototype                                | The local session |
  | `design-review/<yyyy-mm-dd>-<topic>`  | The tag on a prototype's last commit, same date and topic  | The local session |
  | `v<version>`                          | A release tag, once Axis has releases                      |                |

  Nobody names a branch like NexKit's or a cloud session's by hand. `<topic>` is a few lower-case
  English words joined by hyphens, such as `widget-review`. Only release tags start with `v`.
- Keep branches from piling up. GitHub's automatic deletion of head branches is on, and with it NexKit
  deletes the branch of each pull request it merges or closes itself, and each sweep deletes the
  `nexkit/issue-<n>` branches of closed work, as *Branches of closed work* in NexKit's
  [stages][nexkit-branches] says. Neither deletes the branches of cloud sessions that push nothing.
  When the owner asks, the local session lists every `claude/` branch with no pull request and no
  commit that `main` lacks, and every `design/` branch whose `design-review/` tag exists, and the
  owner deletes them. A branch with an open pull request, or any other closed one that was not merged,
  stays.

[nexkit-how]: https://github.com/phuongnse/nexkit/blob/main/docs/how-it-works.md
[nexkit-config]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md
[nexkit-troubleshooting]: https://github.com/phuongnse/nexkit/blob/main/docs/troubleshooting.md
[nexkit-request]: https://github.com/phuongnse/nexkit/blob/main/skills/request/SKILL.md
[nexkit-drive]: https://github.com/phuongnse/nexkit/blob/main/skills/drive/SKILL.md
[nexkit-profiles]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#profiles
[nexkit-auto-merge]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#automatic-merge
[nexkit-caches]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#caches
[nexkit-unaffected]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#checks-that-a-change-cannot-affect
[nexkit-autopilot]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#autopilot
[nexkit-evidence]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#evidence-from-ci-runs
[nexkit-workflow-issues]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#issues-from-a-workflow
[nexkit-checklist]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#review-checklist
[nexkit-app]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#the-github-app
[nexkit-trust]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#vouching-for-an-issue
[nexkit-dependabot]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#dependabot-advisories
[nexkit-person-review]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#reviewing-your-own-pull-requests
[nexkit-branches]: https://github.com/phuongnse/nexkit/blob/main/docs/how-it-works.md#stages
[nexkit-labels]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#autopilot
