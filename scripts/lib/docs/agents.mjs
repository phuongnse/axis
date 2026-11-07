// - AGENTS.md has at most 800 words, and no rule in it takes more than 60, so every session reads a short
//   file. A rule is a bullet with its continuation lines. Words are counted, not lines, because a long
//   line costs an agent as much as a short one, see docs/writing-docs.md#structure.
// - Each script that the Commands table names exists under scripts/.
import { existsSync } from 'node:fs'
import path from 'node:path'
import { countWords, lines } from './helpers.mjs'

export const maxAgentsWords = 800
export const maxAgentsRuleWords = 60

/** Checks the word limits of AGENTS.md under root, and that each script its Commands table names exists. */
export function checkAgents(root, report) {
  const agents = path.join(root, 'AGENTS.md')
  if (!existsSync(agents)) return
  const text = lines(agents)
  const words = countWords(text.join('\n'))
  if (words > maxAgentsWords) {
    report(agents, 1, `${words} words, over the limit of ${maxAgentsWords}: keep what almost every session needs and link the doc that holds the detail`)
  }
  checkRules(agents, text, report)
  checkCommands(root, agents, text, report)
}

function checkRules(agents, text, report) {
  let start = -1
  const close = (end) => {
    if (start < 0) return
    const words = countWords(text.slice(start, end).join('\n'))
    if (words > maxAgentsRuleWords) {
      report(agents, start + 1, `a rule of ${words} words, over the limit of ${maxAgentsRuleWords}: keep the rule short and link the doc that holds the detail`)
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

function checkCommands(root, agents, text, report) {
  const heading = text.indexOf('## Commands')
  if (heading < 0) return
  for (let index = heading + 1; index < text.length && !text[index].startsWith('## '); index++) {
    if (!text[index].startsWith('|')) continue
    for (const [, script] of text[index].matchAll(/`(scripts\/[^`\s]+)`/g)) {
      if (!existsSync(path.join(root, script))) report(agents, index + 1, `names ${script}, which does not exist`)
    }
  }
}
