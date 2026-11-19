# A sync

Use this after a merged change to the tokens or a widget, or when the owner
asks. The rule is "A sync follows the repository" in
[Design changes](../../../docs/delivery.md#design-changes).

1. Run `scripts/design-system.sh`, which builds the bundle from source. If
   `web/node_modules` is missing, run `scripts/setup.sh` first.
2. Read the tokens from `web/src/platform/designTokens.ts`.
3. For each issue the change notes name, run
   `gh issue view <number> --json state,title`. Keep the changes of open issues.
4. Compare the artifact with the repository. List each other difference the
   sync would overwrite, and each token the code no longer defines.
5. Show that list to the owner and publish only after they confirm, unless the
   sync ends a review and every difference comes from what merged.
6. Publish the previews and tokens to the same artifact, keeping open issues'
   changes. Add a change note with the commit you synced from
   (`git rev-parse --short HEAD`) and the open issues kept.
