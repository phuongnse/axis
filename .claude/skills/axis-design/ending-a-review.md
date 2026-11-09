# Ending a design review

1. Audit the prototype for one concern with two mechanisms: have an agent read
   `web/src` and the server for copies, forks and second ways to the same
   result. Add what it finds to the design issue's body as build notes, and set
   the body with `gh issue edit <n> --body-file <file>`.
2. Tag the prototype's last commit and push the tag, then delete the branch:
   `git tag -a design-review/<yyyy-mm-dd>-<topic> -m "<what it shows>"`,
   `git push origin design-review/<yyyy-mm-dd>-<topic>`, then
   `git push origin --delete design/<yyyy-mm-dd>-<topic>` and the local branch.
   Check with `gh run list` that the tag started no run. Keep the tag's
   comparison link, `compare/<base>...design-review/<yyyy-mm-dd>-<topic>`, where
   `<base>` is `git merge-base design/<yyyy-mm-dd>-<topic> main`, taken before
   the branch is deleted.
3. If no issue waits for the design issue, write one build issue blocked by it,
   as [Writing an issue](../axis-nexkit/SKILL.md#writing-an-issue) says. Comment
   the tag and its comparison link on each issue that waits for the design
   issue, with `gh issue comment <n> --body-file <file>`. Then remove the hold:
   `gh issue edit <design-issue> --remove-label on-hold`.
4. NexKit plans the design issue and writes its agreed rules where they live, as
   [Design changes](../../../docs/delivery.md#design-changes) says. Its pull
   request closes the design issue, which starts the waiting issues.
5. After the build is merged, publish the artifact as [A sync](SKILL.md#a-sync) says.
   The sync keeps the card order and names, which `scripts/design-system.sh`
   writes as the bundle's first line, `/* @ds-bundle: {...} */`.

Rules: [Design changes](../../../docs/delivery.md#design-changes).
