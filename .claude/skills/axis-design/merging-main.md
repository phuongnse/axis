# Bringing `main` into the prototype

Use this at the start of every session and before each change, so the prototype
stands on the current `main`. The rule is in
[Design changes](../../../docs/delivery.md#design-changes).

1. `git fetch origin`, then `git merge origin/main`. Never rebase a pushed
   prototype.
2. Resolve each conflict on `main`'s API, data shape, texts and logic, and put
   the prototype's look back on them. Never change `main`'s logic to suit the
   prototype.
3. If the merge brought in commits, run `npm run lint --prefix web` and
   `scripts/design-system.sh`. Then push the branch.
4. Edit the state comment so it names the `main` commit the prototype stands on,
   from `git rev-parse --short origin/main`. Use the PATCH command in
   [SKILL.md](SKILL.md) Start step 3.
