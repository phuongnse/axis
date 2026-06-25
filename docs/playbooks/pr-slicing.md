# PR slicing — isolated, mergeable increments

> **Navigation**: [<- docs/README.md](../README.md) . [<- agent checklist](./agent-checklist.md) . [<- AGENTS.md](../../AGENTS.md)

Slice large work into branches that can merge independently.

## What "isolated" actually means (two-sided test)

The slice is useful without later slices, and later slices can build on `main` after it merges.

## Rules (P0 for agents)

- Branch each slice from `main`.
- Do not stack slice B on slice A's unmerged branch.
- Do not claim readiness without triggered verification.
- Assign one owner per shared seam.

## Shared seams

Shared DTOs, contracts, primitives, migrations, and generated files belong in the slice that first needs them and can prove them.

## Verification Gate Honesty

Use `$axis-ready-review` per slice. Report skipped checks as not triggered or blocked, never green.

## Merge order & rebase cadence

Merge the foundation slice first, then rebase remaining branches onto updated `main`.

## Decision tree

If a change modifies a contract used by multiple slices, make it its own slice or put it in the first independently useful slice.

## Failure modes → the rule that prevents them

Stacked branches hide failures; shared seams without owners create churn; broad slices make verification expensive.

## Agent workflow

Plan slices, implement one, verify one, publish one. Repeat after merge/rebase.

## Checklist before review (each slice)

Design Gate, AC map, docs/status, triggered checks, and retrospective review are complete for that slice only.
