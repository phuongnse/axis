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
   - Links of open issues that point nowhere: each link that
     `node scripts/check-issue-links.mjs` prints on an up-to-date `main`.
3. Answer with one Markdown table, one row per item, with columns for the
   item, its link and the suggested action. When nothing waits, say so in one
   sentence.
4. Change nothing until the owner says yes.

For a broken link, after the yes, find where the file or heading went
(`git log --follow`, or the decisions index for a superseded decision). Edit the
issue to link the new place with `gh issue edit --body-file`, and change
nothing else in it.
