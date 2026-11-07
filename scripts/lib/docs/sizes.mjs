// - A doc under docs/ has at most 1,000 lines, and a change that grows a doc past 900 lines fails.
//   The base for "grows" is NEXKIT_BASE_SHA, else the merge base with origin/main. With neither,
//   only the 1,000 limit applies. AGENTS.md has at most 100 lines, no rule in it takes more than 5, and a SKILL.md under
//   .claude/skills/ at most 250, so agents can read what they need without reading whole books.
import { existsSync } from 'node:fs'
import path from 'node:path'
import { lines, markdownFiles, relativePath } from './helpers.mjs'

export const maxDocLines = 1000
export const maxGrownDocLines = 900
export const maxAgentsLines = 100
export const maxAgentsRuleLines = 5
export const maxSkillLines = 250

/** Checks the size of the docs, AGENTS.md and the skills under root. A change that grows a doc is checked against its base. */
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
  const agents = path.join(root, 'AGENTS.md')
  if (existsSync(agents) && lines(agents).length > maxAgentsLines) {
    report(agents, maxAgentsLines + 1, `${lines(agents).length} lines, over the limit of ${maxAgentsLines}: move detail to docs/`)
  }
  if (existsSync(agents)) {
    // A rule is a bullet with its continuation lines. One that runs long holds detail that belongs in a doc.
    const text = lines(agents)
    let start = -1
    const close = (end) => {
      if (start >= 0 && end - start > maxAgentsRuleLines) {
        report(agents, start + 1, `a rule of ${end - start} lines, over the limit of ${maxAgentsRuleLines}: keep the rule short and link the doc that holds the detail`)
      }
      start = -1
    }
    text.forEach((line, index) => {
      if (/^- /.test(line)) {
        close(index)
        start = index
      } else if (start >= 0 && !/^ +\S/.test(line)) {
        close(index)
      }
    })
    close(text.length)
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
