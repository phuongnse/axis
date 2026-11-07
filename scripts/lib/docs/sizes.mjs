// - A doc under docs/ has at most 1,000 lines, and a change that grows a doc past 900 lines fails.
//   The base for "grows" is NEXKIT_BASE_SHA, else the merge base with origin/main. With neither,
//   only the 1,000 limit applies. A SKILL.md under .claude/skills/ has at most 250 lines, so agents can
//   read what they need without reading whole books.
import { existsSync } from 'node:fs'
import path from 'node:path'
import { lines, markdownFiles, relativePath } from './helpers.mjs'

export const maxDocLines = 1000
export const maxGrownDocLines = 900
export const maxSkillLines = 250

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
      const count = lines(file).length
      if (count > maxSkillLines) {
        report(file, maxSkillLines + 1, `${count} lines, over the limit of ${maxSkillLines}: link the rules in docs/ instead of restating them`)
      }
    }
  }
}
