# Delivery workflow

Axis is built through NexKit. NexKit runs one issue per agent session. People
drive it with four comment commands: `/nexkit plan` and `/nexkit go` on an
issue, and `/nexkit fix` and `/nexkit review` on its pull request.

Three roles take part:

- **The owner** decides. The owner approves plans, pull requests and waiting
  CI runs, and merges.
- **The local agent session** drives NexKit for the owner. It posts commands,
  reads plans and triages pull requests.
- **NexKit's agents** plan, implement, fix and review inside the NexKit
  pipeline. They read [AGENTS.md](../AGENTS.md).

The rules here put [D13](decisions.md#d13-delivery-workflow--agreed) into
practice. Values that NexKit or the repository settings own, such as protected
paths, required checks and the automatic fix limit, are not copied here. Look
them up where they live.

## Decisions

- **The local agent may post `/nexkit plan`, `/nexkit fix` and
  `/nexkit review` itself.** It posts `/nexkit go` only when the owner says so
  explicitly.
- **Only the owner approves pull requests, approves CI runs that wait for
  approval, and merges.** The local agent reminds the owner when a CI run
  needs approval.
- **Everything goes through NexKit, except changes in paths NexKit may not
  change.** Those paths are set by NexKit's
  [`protected_paths` setting][nexkit-config].
  Such changes are hand-made pull requests on any branch name, for example a
  NexKit version bump. The owner opens them from their own account, and
  GitHub does not let an author approve their own pull request. So the owner
  merges them with an admin bypass of the rules on `main`, after the required
  checks pass.

## The life of an issue

1. **Write the issue.** One issue is one behaviour, with 2–5 acceptance
   criteria that a test or command can check. It links to the doc sections it
   implements and is in the current milestone.
2. **Plan.** Comment `/nexkit plan` on the issue. NexKit's plan agent posts a
   plan as a comment.
3. **Review the plan.** Check it against [decisions.md](decisions.md), and
   never treat a **Proposed** decision as **Agreed**. Any change agreed after
   the plan is posted, whether the review finds a gap or the owner decides
   something while approving, however small, is commented in the same issue
   and `/nexkit plan` is run again before `/nexkit go`. `/nexkit go` only runs
   on the latest plan, and that plan holds every agreed change. The reason:
   the plan is the contract. It holds the acceptance criteria the pull request
   is checked against, and it is the record a later reader looks at. A change
   that lives only in a comment is missing from both. Re-planning also lets
   the plan agent find other places the change affects, such as another doc
   that must say the same. Do not open follow-up issues. When the plan reports
   `too_large`, split the issue into parts. Add each part as a GitHub
   sub-issue of the parent, in order and in the parent's milestone. The parent
   closes when all parts are done.
4. **Implement.** The owner says go on the latest plan, and `/nexkit go` is
   posted. NexKit implements that plan and opens a pull request.
5. **Pull request.** If the CI run waits as `action_required`, the owner
   approves it. Merging needs the required checks on `main` to be green, plus
   the owner's approval.
6. **Fix.** Comment `/nexkit fix <instructions>` for another round. This also
   handles merge conflicts: NexKit merges `main` into the branch, it does not
   rebase. When a conflict needs a choice, the round stops as `blocked` with a
   question. Answer it with `/nexkit fix <decision>`. After a manual push,
   comment `/nexkit review`. If the review job itself fails with an error,
   `/nexkit review` retries it. If the review requests changes, NexKit starts
   automatic fix rounds up to the limit in its configuration. After that, a
   person decides on `/nexkit fix`.
7. **Merge.** The owner squash-merges.
8. **Exceptions.** Changes whose purpose is to edit paths NexKit may not change
   are hand-made pull requests, as described under Decisions. A conflict in
   those paths, or a merge that brings workflow changes, is different: merge
   `origin/main` into the NexKit pull request branch by hand, in a separate
   worktree. Run build, lint and tests, ask the owner before pushing, then
   comment `/nexkit review`.

## The life of a milestone

1. **Break it down.** When the current milestone is nearly done, break the
   next one into issues, as the [roadmap](roadmap.md#how-work-is-planned)
   describes.
2. **Deliver the issues.** Each one follows the life of an issue above.
3. **Docs sweep.** Before the owner closes the milestone, one issue in it
   checks the docs and fixes what the milestone made stale. It checks at
   least:
   - the roadmap status of this milestone and the next one;
   - the **From** column of [concepts.md](domain/concepts.md) for every
     concept listed for this milestone, moved to a later milestone when the
     concept was not delivered;
   - every *(planned for Mx)* marker for this milestone in any doc, removed
     when the work was built and moved to a later milestone when it was not.
4. **Close.** The owner closes the milestone on GitHub once the docs sweep
   is merged.

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

## Writing on GitHub

These rules apply to issues, plans, pull request descriptions and reviews.

- Lead with the point. Use short sentences and one idea per bullet.
- Say what changes in behaviour, and why, before naming classes or files.
- Put decisions and risks the owner must judge in their own short section.
- Keep implementation detail (signatures, SQL, file-by-file steps) out of the
  main text, or put it in a collapsed `<details>` block.

[nexkit-config]: https://github.com/phuongnse/nexkit/blob/main/docs/configuration.md
