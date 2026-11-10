# What waits for the owner

Follow these steps when the owner asks, in any language, whether anything
waits for them or needs them.

1. Use the clone from
   [Where the values live](SKILL.md#where-the-values-live). Follow NexKit's
   `status` skill there, at the pinned ref, and take its `needs_person` items.
2. Check each part of the owner in
   [who does what](../../../docs/delivery.md#who-does-what) that
   `needs_person` does not cover:
   - **Proposed** decisions that an open issue waits for: search
     `docs/decisions/` for **Proposed**, and `gh issue list --state open` for
     issues that link them.
   - Hand-made pull requests ready for the owner's merge: open pull requests
     not on a `nexkit/issue-<n>` branch whose `nexkit/checks` and
     `nexkit/review` are green.
   - Milestones with no open issue:
     `gh api repos/{owner}/{repo}/milestones --jq '.[] | select(.open_issues == 0)'`.
   - Links of open issues that the docs audit could not fix: the `Not relinked:` lines in the log of
     its latest run, from `gh run list --workflow docs-audit.yml --branch main --limit 1` and
     `gh run view <id> --log`. The row links that run.
3. Check the labels and milestones of the open issues:
   - **No label:** `gh issue list --state open --search 'no:label' --limit 500 --json number,title,url`.
     The row suggests every label whose description fits, as
     [the life of an issue](../../../docs/delivery.md#the-life-of-an-issue) says.
   - **No milestone, though one is due:**
     `gh issue list --state open --search 'no:milestone' --limit 500 --json number,title,url,labels,body`.
     Leave out an issue of a kind that
     [how issues run](../../../docs/delivery.md#how-issues-run) puts in no milestone. Recognise those
     kinds by their labels, such as `nexkit:flaky` for a flaky test issue, by the hidden `docs-audit`
     marker in the body of a docs audit issue, and by a link to the Design System artifact or a design
     review in the body of a design issue. The row suggests the milestone that delivers the issue, or
     the label of its kind when that label is missing.
4. Answer with one Markdown table, one row per item, with columns for the
   item, its link and the suggested action. When nothing waits, say so in one
   sentence.
5. Change nothing until the owner says yes.
