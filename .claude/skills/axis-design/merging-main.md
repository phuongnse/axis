# Bringing `main` into the prototype

Use this when `main` has changed code the prototype touches. The rule is in
[Design changes](../../../docs/delivery.md#design-changes).

1. `git fetch origin`, then `git merge origin/main`. Never rebase a pushed
   prototype.
2. Resolve each conflict on `main`'s API, data shape, texts and logic, and put
   the prototype's look back on them. Never change `main`'s logic to suit the
   prototype.
3. Check it: `npm run lint --prefix web` and `scripts/design-system.sh`. Then
   push the branch.
