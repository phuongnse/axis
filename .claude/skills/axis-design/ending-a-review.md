# Ending a design review

1. Audit the prototype for one concern with two mechanisms: have an agent read
   `web/src` and the server for copies, forks and second ways to the same
   result, and put what it finds in a comment on the design issue.
2. Read the docs branch through and open it as a hand-made pull request for
   the owner to merge, as
   [Design changes](../../../docs/delivery.md#design-changes) and
   [writing the docs](../../../docs/writing-docs.md) say.
3. Tag the prototype's last commit and push the tag, then delete the branch:
   `git tag -a design-review/<yyyy-mm-dd>-<topic> -m "<what it shows>"`,
   `git push origin design-review/<yyyy-mm-dd>-<topic>`, then
   `git push origin --delete design/<yyyy-mm-dd>-<topic>` and the local branch.
   Check with `gh run list` that the tag started no run. Link the tag's
   comparison, `compare/<base>...design-review/<yyyy-mm-dd>-<topic>`, where
   `<base>` is `git merge-base design/<yyyy-mm-dd>-<topic> main`, taken before
   the branch is deleted.
4. Once the docs pull request is merged, hand over to the build as
   [Design changes](../../../docs/delivery.md#design-changes) says. Write any
   issue as [Writing an issue](../axis-nexkit/SKILL.md#writing-an-issue) says.
   It links the docs sections, the artifact and the tag, and names the tokens,
   widgets and previews it changes. Then close the design issue.
5. After the build is merged, publish the artifact as [A sync](SKILL.md#a-sync) says.
   The sync keeps the card order and names, which `scripts/design-system.sh`
   writes as the bundle's first line, `/* @ds-bundle: {...} */`.

Rules: [Design changes](../../../docs/delivery.md#design-changes).
