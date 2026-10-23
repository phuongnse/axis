# Delivery workflow

Axis is built through NexKit. NexKit runs one issue per agent session. Its
autopilot moves each issue of the current milestone on by itself, from plan to
merge. People can still post the four comment commands at any time:
`/nexkit plan` and `/nexkit go` on an issue, and `/nexkit fix` and
`/nexkit review` on its pull request.

Three roles take part:

- **The owner** decides what only the owner can: **Proposed** design points,
  questions that have no recommended answer, whether NexKit merges its own
  pull requests, and when a milestone closes. NexKit mentions the owner when a
  run needs a person.
- **The local agent session** works for the owner. It writes issues and their
  order, watches the work, reviews plans and merged pull requests, and steps
  in where autopilot stops and a person is needed. It also publishes the
  Design System artifact in Claude from the repository, as
  [Design changes](#design-changes) describes. How it does each of these,
  with the commands and checklists, is in its runbook, the
  [`axis-nexkit` skill](../.claude/skills/axis-nexkit/SKILL.md).
- **NexKit's agents** plan, implement, fix and review inside the NexKit
  pipeline. They read [AGENTS.md](../AGENTS.md). They do not read or write
  Claude Design.

The rules here put [D13](decisions/d13-delivery-workflow.md) into
practice. Values that NexKit or the repository settings own, such as protected
paths, required checks, the automatic fix limit and the profiles with their
models, are not copied here. Look them up where they live.

## Decisions

- **Autopilot moves the issues of the current milestone on.** With NexKit's
  [`autopilot` setting][nexkit-autopilot] on, NexKit plans each ready issue of
  the earliest open milestone, oldest first, and starts `/nexkit go` on a plan
  that has no questions and is not too large. It splits a plan that is too
  large into sub-issues and re-plans an issue that collaborators commented on
  after its latest plan. It never starts the next milestone by itself: when
  every issue of the milestone is closed, it waits until the owner closes the
  milestone. Issues outside that milestone, or opened by someone without write
  access, are never touched.
- **Issues run in parallel unless one blocks another.** An issue is ready when
  no issue that blocks it, by GitHub's *blocked by* relationship, is open.
  Autopilot reads no text such as "Depends on #12", and the order of
  sub-issues does not count. So the creator of an issue that must wait for
  another marks it *blocked by* that issue, for example each step of a journey
  by the step before it, and the milestone's docs sweep by every other issue
  of the milestone. There is no limit on how many issues run at once:
  autopilot's `max_parallel` is left unset.
- **Autopilot answers a question that has a recommended option.** Plans, and
  rounds that stop as `blocked`, give each question its options and the one
  NexKit recommends. With `answer` set to `recommended`, autopilot answers when
  every question has a recommendation, even one that rests on a **Proposed**
  decision, and nobody is mentioned. The owner sees the answers on the issue
  and can change one with a comment, before `/nexkit go`, or with
  `/nexkit fix` on the pull request. Autopilot answers only once in a row:
  when the plan or round its answers started asks again, and no person has
  commented since, the issue waits for a person.
- **People stay in charge.** A person's command, a plan's question without a
  recommendation, a question asked again after autopilot answered it, a failed
  run, a second time-out and a refused merge all stop autopilot on that issue
  until a person acts. Other issues go on, and an issue that waits for a person
  does not hold up the others. A run that stops on a transient error, such as
  an overloaded API or a lost connection, before it published anything, runs
  once more by itself; a second such stop waits for a person.
- **NexKit merges a pull request once it passes, while `merge.auto` is on.**
  With NexKit's [`merge.auto` setting][nexkit-auto-merge] on, NexKit
  squash-merges a pull request as soon as every check passes and the AI review
  approves. Then it closes the issue the pull request implements. The review
  marks each acceptance criterion met, unmet or unverifiable. An unmet
  criterion counts as changes requested. An approved pull request with an
  unverifiable criterion, one that the diff, its tests and the check results
  show neither way, never merges by itself: the owner confirms the criteria
  that the review comment lists, then merges. A merge that GitHub refuses
  because `main` moved meanwhile is tried again, and one that waits for
  required checks happens on a later sweep once they pass. The owner
  turns the setting off to decide each merge again, and then only the owner
  merges. The switch is in `.nexkit/`, so changing it is a hand-made pull
  request.
- **NexKit mentions the owner when a person is needed.** The owner's login is
  in NexKit's [`notify` setting][nexkit-config]. NexKit mentions it when a run
  ends in a state that needs a person: a plan with a question that has no
  recommended option, a blocked round with such a question, a question asked
  again after autopilot answered it, a failed run, a second time-out, no
  automatic fix rounds left, a refused automatic merge, an approved pull
  request with an unverifiable criterion, a CI failure on `main`, or the last
  issue of the milestone closed. It mentions the owner once for each issue
  that waits. Every other
  run needs nobody, and a plan that autopilot answers or splits mentions
  nobody either.
- **NexKit resolves conflicts with `main` by itself.** With
  `conflicts.auto_resolve` on, each time `main` moves NexKit starts a fix round
  for every open NexKit pull request that now conflicts with it. A conflict
  that needs a choice ends the round as `blocked`, with a question.
- **NexKit pauses at the Claude usage limit and resumes after it resets.** A
  Claude stage first moves to the second Claude subscription. NexKit pauses
  only when both are at their limit. The
  run's comment shows the reset time. No person repeats the command.
- **A parent issue closes when its last part does.** With
  `close_parent_issues` on, NexKit closes a parent issue once every sub-issue
  is closed and at least one was completed.
- **The rules on `main` let NexKit merge.** They require NexKit's own checks
  and no approval from a person. They do not require the CI workflow, which
  runs on pushes to `main` and to hand-made branches but not on NexKit's
  branches. NexKit's checks run the same scripts there.
- **CI runs on `main` after every merge.** A merge by NexKit starts no `push`
  workflow, so NexKit starts the CI and CodeQL workflows on `main` itself,
  through its `merge.after_workflows` setting. Two pull requests that pass on their own
  can still break `main` together. When one of those workflows fails on `main`,
  autopilot runs its failed jobs once more. When they fail again, it opens a
  `bug` issue in the milestone with the failed jobs and the run link, mentions
  the owner, and plans it before every other issue. A run that passes only when
  its failed jobs run again, and an older failed run that a newer run overtook,
  are recorded too, planned right after those, so a failure that passed on a
  second attempt is not lost. Each failure gets a `bug` issue of its own, named
  by the failing test from the job's error annotation, or by the job and its
  first failed step; the same failure later adds a comment to its open issue.
- **Every merge to `main` gets a CI result.** A run on `main` always finishes.
  A newer merge replaces only a run that is still waiting to start, so the next
  run tests both merges. On any other branch a newer push stops the older run.
  CI runs the lint and unit tests, the integration tests and the E2E tests as
  three parallel jobs, so a run takes as long as its slowest job.
- **Each check has room to grow within its time limit.** The limits are in
  NexKit's [`checks` setting][nexkit-config]. When a check takes more than half
  its limit, a hand-made pull request raises the limit or makes the suite
  faster, before rounds start to stop on it.
- **The slow checks run in full only in NexKit's checks.** The integration and
  E2E checks are marked `slow`, so an agent runs only the tests in them that
  cover its change or that failed, and leaves the full suites to the checks
  after its round. The test, integration and E2E checks name their `results`:
  `artifacts/test-results`, and for E2E also `artifacts/logs` and the
  Playwright traces in `tests/e2e/test-results`. When such a check fails, the
  next fix round gets those files.
- **NexKit's checks skip suites that a change cannot affect.** A check round
  takes about 25 minutes, of which the integration tests take about 7 and
  Playwright about 10. So under NexKit's `NEXKIT_BASE_SHA`, a suite that no
  changed file can affect is skipped, and any doubt runs it. The integration
  performance tests, whose class names end in `PerformanceTests`, never run
  there, because they depend on the runner's load. CI on `main` sets no base
  and runs every suite, so a slowdown shows after the merge and autopilot opens
  a bug issue for it. Which suite skips when is in
  [test runs and reports](development.md#test-runs-and-reports).

- **NexKit restores the dependency caches that CI on `main` saves.** Before
  `scripts/setup.sh`, NexKit's implement, fix and check runs restore the
  NuGet, npm and Playwright browser caches, so setup installs only what
  changed. NexKit never saves a cache, because those runs execute code the
  agent wrote. The CI workflow saves them on `main`, under the keys NexKit
  looks for. The entries are in NexKit's [`cache` setting][nexkit-caches],
  and the CI workflow has one `actions/cache` step for each, with the same
  paths and key files in the same order. A change to what setup downloads
  updates both in the same hand-made pull request. A missed cache never fails
  a run; setup then installs from scratch.
- **Everything goes through NexKit, except changes in paths NexKit may not
  change.** Those paths are set by NexKit's
  [`protected_paths` setting][nexkit-config].
  Such changes are hand-made pull requests on any branch name, for example a
  NexKit version bump. Hand-made pull requests are only for those paths, so a
  design change is never one; see [Design changes](#design-changes). The owner
  opens them from their own account. NexKit's
  checks do not run on them, so the owner merges them with an admin bypass of
  the rules on `main`, after the CI workflow passes.

## Profiles

Each issue runs on one NexKit profile. The profile sets the models for the
plan, implement, fix and review rounds of that issue. When `/nexkit plan`
starts, NexKit's triage step reads the issue and chooses the profile. The
profiles, their `when` texts and their models are in NexKit's
[`profiles` setting][nexkit-profiles]. This section holds the rules behind
them.

- **Profiles go by risk and novelty: `standard` and `hard`.** The more a
  mistake would cost, or the less the platform has done such work before, the
  stronger the profile. `hard` is the default, so an issue whose triage fails
  runs on it.
- **`standard` is a narrow description and `hard` is the rest.** `standard`
  names only work that repeats what the platform already does. Anything it
  does not name goes to `hard`, so the concepts and kinds of work that a new
  milestone brings land on the stronger profile with no change to the `when`
  texts. The cost is that more issues run on the stronger, dearer models.
- **Plan and review are at least as strong as implement.** The plan is the
  contract and the review is the last check before the owner. In every
  profile, review runs on a different model from implement.
- **Triage runs on a strong model.** It is one call that reads only the
  issue, so it costs little, and a wrong choice does not fail: the issue just
  runs on the wrong profile.
- **A `when` text says what a mistake would cost, not where the code is.** It
  may name concepts from [concepts.md](domain/concepts.md) as examples, but
  never files, classes, doc sections or milestones, which go stale. When
  unsure, triage chooses the stronger profile.
- **Triage sees only the issue and its discussion.** It does not open links.
  So an issue names the concepts it touches in its own text, even when it
  links to the docs that describe them.
- **A comment changes the profile.** Comment
  `/nexkit plan Use the <profile> profile`. A request written in the issue
  text does not count. A re-plan keeps the profile unless someone asks for
  another one.
- **Profile changes are hand-made pull requests,** because `.nexkit/` is a
  path NexKit may not change.

## The life of an issue

1. **Write the issue.** One issue is one behaviour, aiming for 2–5 acceptance
   criteria that a test or command can check. More than five is a signal to
   check whether the issue holds more than one behaviour. If it does, split it.
   If every criterion checks the same behaviour, more are fine. A plan is not
   revised only to cut the number of criteria. The issue links to the doc
   sections it implements, is in the current milestone, and names the concepts
   it touches in its own text, so triage can choose the [profile](#profiles).
   It gets at least one label, and every repository label whose description
   on GitHub fits its content. There is no fixed list, because the owner adds
   and removes labels. The owner or the local agent opens it, because
   autopilot takes only issues opened by someone with write access, and marks
   it *blocked by* every issue it must wait for. It may link a Design artifact
   as a picture, while the docs stay the source of behaviour.
2. **Plan.** Autopilot starts `/nexkit plan` once the issue is ready. NexKit's
   plan agent posts a plan as a comment. A person may comment `/nexkit plan`
   to start it sooner or with a note.
3. **Questions and splits.** When every question of the plan has a
   recommended option, autopilot answers with those options and plans again.
   Otherwise NexKit mentions the owner, who answers in a comment on the
   issue, and the next sweep plans again. When the plan reports `too_large`,
   autopilot creates the parts the plan suggests as sub-issues, with the
   issue's milestone and labels, marks each part *blocked by* the earlier
   parts it needs, and closes an open NexKit pull request of the issue as
   superseded, keeping its branch. NexKit closes the parent when all parts are
   done. A part is written like any issue: its title names the behaviour that
   part delivers and does not reuse the parent's title, whole or as a
   `<parent>: <part>` prefix, because the sub-issue link already shows the
   parent.
4. **Review the plan.** The local agent reads each plan as it is posted and
   checks it against the docs, [decisions.md](decisions.md) and the profiles.
   Every change agreed after a plan, from the review or from the owner,
   however small, is commented on the issue, and the next sweep plans again
   before it starts `/nexkit go`. Once `/nexkit go` has started, the change
   goes to the pull request as `/nexkit fix <change>`. No follow-up issues.
   The reason: the plan is the contract. It holds the acceptance criteria the
   pull request is checked against, and it is the record a later reader looks
   at. A change that lives only in a comment is missing from both.
5. **Implement.** Autopilot starts `/nexkit go` on the latest plan once it has
   no open question, is not too large and no comment came after it. NexKit
   implements that plan and opens a pull request. When an implement or fix
   round runs out of time, autopilot plans once more with a note to split the
   issue, and the plan says why the round did not finish. A cause that was not
   found makes the first part a diagnosis. A part inherits its parent's
   time-outs, so a second time-out anywhere below waits for a person, and parts
   go at most three levels deep.
6. **Pull request.** Every round runs NexKit's checks and the AI review on the
   new commit. Merging needs both to pass.
7. **Fix.** NexKit starts fix rounds by itself for a conflict with `main`,
   which it merges and never rebases, and for failing checks or a review that
   requests changes, up to the automatic fix limit in its configuration. A
   conflict that needs a choice stops the round as `blocked`, with a
   question. A fix round reads the result files of the checks that failed and
   what earlier fix rounds tried. When a check fails the same way before and
   after a fix round, the next automatic round is a diagnosis: it names the
   cause with evidence before it changes code, or stops as `blocked` with what
   it found. Beyond that, a person comments `/nexkit fix <instructions>`.
8. **Merge.** NexKit squash-merges, starts CI on `main` and closes the issue.
   With `merge.auto` off, or when the review left a criterion unverifiable,
   the owner squash-merges instead, and GitHub closes the issue.
9. **Exceptions.** Changes whose purpose is to edit paths NexKit may not change
   are hand-made pull requests, as described under Decisions. A conflict in
   those paths, or a merge that brings workflow changes, is one that NexKit
   cannot push: the local agent merges `main` into the NexKit branch by hand,
   and asks the owner before it pushes.

## The life of a milestone

1. **Break it down.** When the current milestone is nearly done, the local
   agent breaks the next one into issues, as the
   [roadmap](roadmap.md#how-work-is-planned) describes, and checks the
   [profiles](#profiles) against the new milestone's concepts and the models
   now available. Profile fixes are hand-made pull requests. Autopilot does not
   touch the next milestone's issues while an earlier milestone is open.
2. **Deliver the issues.** Each one follows the life of an issue above.
   Autopilot runs every ready issue of the milestone at once.
3. **Docs sweep.** Before the owner closes the milestone, one issue in it
   checks the docs and fixes what the milestone made stale. It is marked
   *blocked by* every other issue of the milestone, so it runs last. It checks
   at least:
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
4. **Close.** When the last issue of the milestone closes, NexKit mentions
   the owner once. The owner closes the milestone on GitHub once the docs
   sweep is merged, and the next sweep takes the next milestone.

## Design changes

The repository is the source of truth for the Axis design system, and the
local agent session publishes it to Claude as a Design System artifact, as
[D57](decisions/d57-a-design-system-published-from-the-repository.md) agrees.
The token file and the preview pages are in the
[frontend reference](reference/frontend.md).

- **A design change is a NexKit issue.** It links the artifact and follows the
  [life of an issue](#the-life-of-an-issue). It is never a hand-made pull
  request, because the token file and the widgets are not paths NexKit may
  not change.
- **The published design follows the repository.** After a merged change to
  the tokens or a widget, the local session publishes the design system again.
- **Only the owner and Claude edit in Claude Design.** NexKit's agents neither
  read nor write it. New screens may be drawn as Design artifacts, and an
  issue may link one as a picture, while the docs stay the source of
  behaviour.

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
  decision files, and the size limits: 1,000 lines for a doc, 100 for
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

[nexkit-config]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md
[nexkit-profiles]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#profiles
[nexkit-auto-merge]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#automatic-merge
[nexkit-caches]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#caches
[nexkit-autopilot]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md#autopilot
