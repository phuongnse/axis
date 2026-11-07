// - Each decision file is docs/decisions/d<n>-<slug>.md, starts with '# D<n>. <title>', where the
//   slug is the title's anchor, and has a 'Status: **<status>**' line whose status starts with
//   Agreed or Proposed.
// - docs/decisions.md has one row per decision, numbered from 1 with no gaps, in order. The row of a
//   decision file has the file's title and status. The row of a superseded decision has no file: it
//   names the live decisions that superseded it and links the last text of its file at a commit on
//   GitHub.
import { existsSync } from 'node:fs'
import path from 'node:path'
import { slugify } from '../../check-links.mjs'
import { lines, markdownFiles, relativePath } from './helpers.mjs'

const decisionFilePattern = /^d(\d+)-([a-z0-9-]+)\.md$/
const decisionTitlePattern = /^# D(\d+)\. (.+)$/
const statusPattern = /^Status: \*\*((?:Agreed|Proposed)\b[^*]*)\*\*$/
export const decisionRowPattern = /^\| \[D(\d+)\]\(decisions\/([^)]+)\) \| (.+?) \| \*\*(.+?)\*\* \|$/
export const supersededRowPattern =
  /^\| D(\d+) \| (.+?) \| Superseded by ((?:\[D\d+\]\(decisions\/[^)]+\)(?:, | and ))*\[D\d+\]\(decisions\/[^)]+\)), \[last text\]\((https:\/\/github\.com\/[\w.-]+\/[\w.-]+\/blob\/[0-9a-f]{40}\/docs\/decisions\/d(\d+)-[a-z0-9-]+\.md)\) \|$/
export const successorPattern = /\[D(\d+)\]\(decisions\/([^)]+)\)/g

/** Checks the decision files against the decisions index, docs/decisions.md. */
export function checkDecisions(root, report) {
  const docs = path.join(root, 'docs')
  const decisionsFolder = path.join(docs, 'decisions')
  const index = path.join(docs, 'decisions.md')
  if (!existsSync(decisionsFolder) && !existsSync(index)) return
  const decisions = new Map()
  for (const file of existsSync(decisionsFolder) ? markdownFiles(decisionsFolder).sort() : []) {
    const name = path.basename(file)
    const nameMatch = decisionFilePattern.exec(name)
    if (path.dirname(file) !== decisionsFolder || !nameMatch) {
      report(file, 1, 'a decision file is named docs/decisions/d<n>-<slug>.md')
      continue
    }
    const text = lines(file)
    const title = decisionTitlePattern.exec(text[0])
    if (!title) {
      report(file, 1, "the first line is '# D<n>. <title>'")
      continue
    }
    if (title[1] !== nameMatch[1]) report(file, 1, `the title says D${title[1]}, the file name d${nameMatch[1]}`)
    if (slugify(title[2]) !== nameMatch[2]) report(file, 1, `the file name should end in d${nameMatch[1]}-${slugify(title[2])}.md`)
    const statusLine = text.findIndex((line) => line.startsWith('Status:'))
    const status = statusLine < 0 ? undefined : statusPattern.exec(text[statusLine])?.[1]
    if (!status) report(file, statusLine < 0 ? 1 : statusLine + 1, "needs a 'Status: **Agreed**' or 'Status: **Proposed**' line")
    const number = Number(nameMatch[1])
    if (decisions.has(number)) report(file, 1, `D${number} has more than one file`)
    decisions.set(number, { file, name, title: title[2], status })
  }

  if (!existsSync(index)) {
    report(index, 1, 'missing: docs/decisions.md lists every decision')
    return
  }
  const rows = []
  lines(index).forEach((line, i) => {
    const row = decisionRowPattern.exec(line)
    if (row) rows.push({ line: i + 1, number: Number(row[1]), name: row[2], title: row[3], status: row[4] })
    const superseded = supersededRowPattern.exec(line)
    if (superseded) {
      rows.push({
        line: i + 1,
        number: Number(superseded[1]),
        title: superseded[2],
        by: [...superseded[3].matchAll(successorPattern)].map((match) => ({ number: Number(match[1]), name: match[2] })),
        lastTextNumber: Number(superseded[5]),
      })
    }
  })
  const rowByNumber = new Map(rows.map((row) => [row.number, row]))
  rows.forEach((row, i) => {
    if (row.number !== i + 1) report(index, row.line, `row D${row.number} should be D${i + 1}: decisions are numbered in order with no gaps`)
    const decision = decisions.get(row.number)
    if (row.by !== undefined) {
      if (decision) report(index, row.line, `D${row.number} is superseded, so delete ${relativePath(root, decision.file)}`)
      if (row.lastTextNumber !== row.number) report(index, row.line, `D${row.number}'s last text links the file of D${row.lastTextNumber}`)
      for (const by of row.by) {
        const successor = decisions.get(by.number)
        if (!successor || rowByNumber.get(by.number)?.by !== undefined) {
          report(index, row.line, `D${row.number} is superseded by D${by.number}, which is not a decision in force`)
        } else if (by.name !== successor.name) {
          report(index, row.line, `D${row.number} links ${by.name} for D${by.number}, the file is ${successor.name}`)
        }
      }
      return
    }
    if (!decision) {
      report(index, row.line, `D${row.number} has no file in docs/decisions/`)
      return
    }
    if (row.name !== decision.name) report(index, row.line, `D${row.number} links ${row.name}, the file is ${decision.name}`)
    if (row.title !== decision.title) report(index, row.line, `D${row.number} title '${row.title}' differs from the file's '${decision.title}'`)
    if (decision.status && row.status !== decision.status) {
      report(index, row.line, `D${row.number} status '${row.status}' differs from the file's '${decision.status}'`)
    }
  })
  const listed = new Set(rows.map((row) => row.number))
  for (const [number, decision] of decisions) {
    if (!listed.has(number)) report(decision.file, 1, `D${number} has no row in docs/decisions.md`)
  }
}
