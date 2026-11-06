---
name: axis-docs-audit
description: The weekly "Axis docs audit" routine, which reads a share of Axis's docs against the code and opens a NexKit issue for each statement the code contradicts and each agreed rule the code does not follow. Use only in that routine, or when the owner asks for an audit of named pages. Not for the hourly driver, which uses axis-nexkit-routine, and not for agents inside the NexKit pipeline.
---

# Axis's weekly docs audit

The rules are in [how the docs stay correct](../../../docs/writing-docs.md#how-the-docs-stay-correct)
and [D134](../../../docs/decisions/d134-docs-stay-true-while-work-merges.md). This skill says how to
apply them in one run. The audit changes no file: everything it finds becomes a NexKit issue.

## Start of a run

1. Work on an up-to-date checkout of `main`.
2. Read [writing the docs](../../../docs/writing-docs.md) and
   [writing an issue](../axis-nexkit/SKILL.md#writing-an-issue).
3. Pick this run's pages. List every `*.md` under `docs/` with `git ls-files docs | grep '\.md$' | sort`,
   number them from 0, and take those whose number modulo 4 equals the ISO week number modulo 4
   (`date -u +%V`). Every page is read once in four weeks.

## Reading a page

Split the pages into groups of about eight and give each group to a subagent, with this section as its
brief. For each page, it compares every statement that the repository can confirm with the code: the
server under `src/`, the SPA under `web/src/`, `scripts/`, `deploy/`, `.github/`, `.nexkit/`, the
schemas and the tests. It reads the code, not other docs, as the evidence.

It reports each finding with the page and line, the statement, the evidence (file and line), and one
of these kinds:

| Kind | What it is | What it becomes |
| --- | --- | --- |
| Wrong | The page says what the code does not do, and nothing marks it planned | A docs issue |
| Built | A rule is marked planned, and the code already does it | A docs issue |
| Missing | The code does something a user, operator or author meets, and no page says it | A docs issue |
| Unbuilt | An agreed rule, not marked planned, that the code does not follow | A code issue |
| Untested list | A list of what the code defines with no test that compares it with the code | A test issue |
| Standard | Progress, history, a rule stated twice, a marker in a sentence | A docs issue |

When a page and the code disagree and neither an agreed decision nor the page says which is meant, the
issue names both readings and recommends one, with the reason. NexKit's plan asks the question, and
autopilot answers with the recommendation unless the owner changes it, as
[Decisions](../../../docs/delivery.md#decisions) says.

A subagent reports only findings with evidence. "Could not confirm" is not a finding.

## Opening the issues

1. Drop findings that an open issue or pull request already covers: search with
   `gh issue list --state open --search "<file name>"` and `gh pr list --state open --search "<file name>"`.
2. Group the rest: one docs issue per page, with every docs finding of that page as an acceptance
   criterion; one code issue per behaviour; one test issue per list.
3. Write each issue as [writing an issue](../axis-nexkit/SKILL.md#writing-an-issue) says, with these
   choices:
   - It is in no milestone, as [Decisions](../../../docs/delivery.md#decisions) says for audit issues.
   - A docs issue gets the `documentation` label; a code issue gets `bug` when the code breaks an agreed
     rule; a test issue gets `enhancement`. Add every other label whose description fits.
   - The body links the page section and quotes the code it rests on, with a link to the file at
     `main`. It names the kind of each finding.
   - A code issue whose behaviour belongs to a milestone that is not Done goes in that milestone, and
     the milestone's docs sweep is marked *blocked by* it.
4. Open at most ten issues in a run. Note the rest in the run's report for the next run.

## End of a run

The run's report, in English, lists the pages read, the issues opened, the findings dropped as
already covered, and the findings left for the next run. It mentions nobody: the issues carry
everything that needs a person.
