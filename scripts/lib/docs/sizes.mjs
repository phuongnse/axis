// - A doc under docs/ has at most 1,000 lines, and a change that grows a doc past 900 lines fails.
//   The base for "grows" is NEXKIT_BASE_SHA, else the merge base with origin/main. With neither,
//   only the 1,000 limit applies. A SKILL.md under .claude/skills/ holds at most 1,000 words, counted as AGENTS.md's are, so
//   every run reads little. See docs/writing-docs.md#structure.
import { existsSync } from 'node:fs'
import path from 'node:path'
import { countWords, lines, markdownFiles, relativePath } from './helpers.mjs'

export const maxDocLines = 1000
export const maxGrownDocLines = 900
export const maxSkillWords = 1000

/** Checks the size of the docs and the skills under root. A change that grows a doc is checked against its base. */
export function checkSizes(root, report, { baseLineCount } = {}) {
  const docs = path.join(root, 'docs')
  for (const file of markdownFiles(docs).sort()) {
    const count = lines(file).length
    if (count > maxDocLines) {
      report(file, maxDocLines + 1, `${count} lines, over the limit of ${maxDocLines}: move whole ## sections to a new file`)
    } else if (baseLineCount && count > maxGrownDocLines) {
      const before = baseLineCount(relativePath(root, file))
      if (count > before) {
        report(
          file,
          maxGrownDocLines + 1,
          `${count} lines, up from ${before}, over ${maxGrownDocLines} for a change that grows a doc: move whole ## sections to a new file`,
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
