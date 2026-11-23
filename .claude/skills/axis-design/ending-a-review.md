# Ending a design review

1. Audit the prototype for one concern with two mechanisms: have an agent read
   `web/src` and the server for copies, forks and second ways to the same
   result. The audit runs over the whole prototype, after the per-step audits
   that [SKILL.md](SKILL.md) describes. Add what it finds to the design issue's
   body as build notes, and set the body with `gh issue edit <n> --body-file <file>`. If the issue
   has a plan, comment the change on it in the same step, as
   [the life of an issue](../../../docs/delivery.md#the-life-of-an-issue) says.
2. Tag the prototype's last commit and push the tag, then delete the branch:
   `git tag -a design-review/<yyyy-mm-dd>-<topic> -m "<what it shows>"`,
   `git push origin design-review/<yyyy-mm-dd>-<topic>`, then
   `git push origin --delete design/<yyyy-mm-dd>-<topic>` and the local branch.
   Check with `gh run list` that the tag started no run. Keep the tag's
   comparison link, `compare/<base>...design-review/<yyyy-mm-dd>-<topic>`, where
   `<base>` is `git merge-base design/<yyyy-mm-dd>-<topic> main`, taken before
   the branch is deleted.
3. If no issue waits for the design issue, write one build issue blocked by it,
   as [Writing an issue](../axis-nexkit/SKILL.md#writing-an-issue) says. Give each build issue its
   home as [how issues run](../../../docs/delivery.md#how-issues-run) says: `improvement` and no
   milestone for screens already built, or the milestone that brings new screens. Check each issue
   already waiting the same way, and move it if it is not in its home.
4. Map each rule to its build issue, as [Design changes](../../../docs/delivery.md#design-changes) says.
   Edit the state comment with the PATCH command of [Start step 4](SKILL.md#start). Add a table with one
   row per rule heading, a link to the rule's comment and the issue that builds it. A rule with no issue
   gets one first, as step 3 says. An older issue that takes a later step's rules is edited with
   `gh issue edit <n> --body-file <file>`.
5. Comment the tag and its comparison link on each issue that waits for the design
   issue, with `gh issue comment <n> --body-file <file>`. Then remove the hold:
   `gh issue edit <design-issue> --remove-label on-hold`.
6. NexKit plans the design issue and writes its agreed rules where they live, as
   [Design changes](../../../docs/delivery.md#design-changes) says. Its pull
   request closes the design issue, which starts the waiting issues.
7. After the build is merged, publish the artifact as [A sync](SKILL.md#a-sync) says.
   The sync keeps the card order and names, which `scripts/design-system.sh`
   writes as the bundle's first line, `/* @ds-bundle: {...} */`.

Rules: [Design changes](../../../docs/delivery.md#design-changes).
