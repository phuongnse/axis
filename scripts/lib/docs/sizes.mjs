// - A doc under docs/ has at most 12,000 words, and a change that grows a doc past 10,000 words fails.
//   Docs have no line limit: a long line dodges a line count, so words measure what a reader reads.
//   A split moves one topic's sections and never shrinks text.
//   The base for "grows" is NEXKIT_BASE_SHA, else the merge base with origin/main. With neither,
//   only the 12,000 limit applies. A SKILL.md under .claude/skills/ holds at most 1,000 words, counted as AGENTS.md's are, so
//   every run reads little. See docs/writing-docs.md#structure.
//   The docs audit opens an issue to split each doc past splitIssueWords, see
//   docs/writing-docs.md#how-the-docs-stay-correct.
import { existsSync } from 'node:fs'
import path from 'node:path'
import { countWords, docWordCounts, lines, markdownFiles } from './helpers.mjs'

export const maxDocWords = 12000
export const maxGrownDocWords = 10000
export const maxSkillWords = 1000
export const splitTargetWords = 8000
export const splitIssueWords = 9000

const splitAdvice = `move the whole ## sections of one topic to a new file, leaving each page at or under ${splitTargetWords} words, and do not shorten, rewrap or delete existing text to fit`

/** Checks the size of the docs and the skills under root. A change that grows a doc is checked against its base. */
export function checkSizes(root, report, { baseWordCount } = {}) {
  for (const { file, page, words } of docWordCounts(root)) {
    if (words > maxDocWords) {
      report(file, 1, `${words} words, over the limit of ${maxDocWords}: ${splitAdvice}`)
    } else if (baseWordCount && words > maxGrownDocWords) {
      const before = baseWordCount(page)
      if (words > before) {
        report(
          file,
          1,
          `${words} words, up from ${before}, over ${maxGrownDocWords} for a change that grows a doc: ${splitAdvice}`,
        )
      }
    }
  }
  const skills = path.join(root, '.claude', 'skills')
  if (existsSync(skills)) {
    for (const file of markdownFiles(skills).filter((file) => path.basename(file) === 'SKILL.md').sort()) {
      const words = countWords(lines(file).join('\n'))
      if (words > maxSkillWords) {
        report(
          file,
          1,
          `${words} words, over the limit of ${maxSkillWords}: link the rules in docs/ and move a workflow few runs need to a file of its own`,
        )
      }
    }
  }
}
