# Hand merges and hand-made changes

Follow these steps only when the owner asks. Ask the owner before each push.
The rules for hand-made pull requests are in
[hand-made pull requests](../../../docs/delivery.md#hand-made-pull-requests).
Do not restate them here.

## Hand merges

When NexKit cannot resolve a conflict because it is in a path NexKit may not
change:

1. Merge `main` into the pull request's branch in a separate worktree.
2. Run `scripts/build.sh`, `scripts/lint.sh` and `scripts/test.sh`.
3. Ask the owner before you push.
4. Push to that branch and comment `/nexkit review`.

## Hand-made changes

When the latest plan says the change is in paths NexKit may not change, under
*Needs a hand-made change*, or a `/nexkit go` or fix round stopped as blocked
because its change needs those paths:

1. Make the change on an `ops/<topic>` branch, exactly as the plan says.
2. Run at least the plan's check commands, `node scripts/check-links.mjs` and
   `node scripts/check-docs.mjs`.
3. Ask the owner before you push.
4. Push, and open a pull request that closes the issue, with the plan's points
   for the owner in their own short section. It carries the issue's labels and
   milestone, as [hand-made pull requests](../../../docs/delivery.md#hand-made-pull-requests)
   requires. Read them with `gh issue view`, and pass them to `gh pr create`:

   ```sh
   labels=$(gh issue view <n> --json labels --jq '[.labels[].name] | join(",")')
   milestone=$(gh issue view <n> --json milestone --jq '.milestone.title // empty')
   gh pr create --title '<title>' --body-file <body> \
     ${labels:+--label "$labels"} ${milestone:+--milestone "$milestone"}
   ```

5. Comment `/nexkit review` on it after each push. When CI, `nexkit/checks` or
   `nexkit/review` fails, fix the cause, ask the owner, push and comment again.
6. When all are green, tell the owner that it is ready to merge. Never approve
   or merge it.
