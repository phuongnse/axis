// - Every threat table under docs/, found by its header line, such as the ones in
//   docs/reference/threat-model-*.md, has a unique id across all of them, a status and a rating from
//   the lists, and evidence. A Mitigated row names only test classes declared under tests/, controlplane/tests/
//   or samples/, a Gap row links the GitHub issue that closes it, and an Accepted row gives a date.
import path from 'node:path'
import {
  cellSeparator,
  issueLinkPattern,
  lines,
  markdownFiles,
  relativePath,
  testClassLookup,
  testClassPattern,
} from './helpers.mjs'

export const threatStatuses = ['Mitigated', 'Gap', 'Delegated', 'Accepted']
export const threatRatings = ['Low', 'Medium', 'High', 'Critical']
const threatHeader = '| Id | Threat | Boundary | Category | Rating | Status | Evidence |'
const isoDatePattern = /\b\d{4}-\d{2}-\d{2}\b/

/** Checks every threat table under docs/. An id must be unique across all of them. */
export function checkThreatTables(root, report) {
  const testClasses = testClassLookup(root)
  const seen = new Map()
  for (const file of markdownFiles(path.join(root, 'docs')).sort()) {
    if (lines(file).some((line) => line.trim() === threatHeader)) checkThreatTable(root, report, testClasses, seen, file)
  }
}

function checkThreatTable(root, report, testClasses, seen, file) {
  const text = lines(file)
  for (let header = 0; header < text.length; header++) {
    if (text[header].trim() !== threatHeader) continue
    for (let i = header + 1; i < text.length && text[i].startsWith('|'); i++) {
      const raw = text[i].split(cellSeparator)
      if (/^:?-+:?$/.test(raw[1].trim())) continue
      const cells = raw.slice(1, 8).map((cell) => cell.trim())
      const id = cells[0].replace(/^`(.*)`$/, '$1')
      const [rating, status, evidence] = cells.slice(4)
      const rawEvidence = raw[7] ?? ''
      const line = i + 1
      const first = seen.get(id)
      if (!first) seen.set(id, { file, line })
      else if (first.file === file) report(file, line, `${id} has a row already, on line ${first.line}`)
      else report(file, line, `${id} has a row already, in ${relativePath(root, first.file)} on line ${first.line}`)
      const statusName = /^\*\*(.+)\*\*$/.exec(status)?.[1]
      if (!threatStatuses.includes(statusName)) {
        report(file, line, `${id} status '${status}' is not one of ${threatStatuses.map((name) => `**${name}**`).join(', ')}`)
      }
      if (rating === '') report(file, line, `${id} has no rating: one of ${threatRatings.join(', ')}`)
      else if (!threatRatings.includes(rating)) report(file, line, `${id} rating '${rating}' is not one of ${threatRatings.join(', ')}`)
      if (evidence === '') report(file, line, `${id} has no evidence`)
      else if (statusName === 'Mitigated') {
        for (const match of rawEvidence.matchAll(testClassPattern)) {
          if (!testClasses().has(match[1])) report(file, line, `${id} names the test class ${match[1]}, which no .cs file under tests/, controlplane/tests/ or samples/ declares`)
        }
      } else if (statusName === 'Gap' && !issueLinkPattern.test(rawEvidence)) {
        report(file, line, `${id} is a Gap and links no issue: link the GitHub issue that closes it`)
      } else if (statusName === 'Accepted' && !isoDatePattern.test(rawEvidence)) {
        report(file, line, `${id} is Accepted and gives no date: give the date the owner accepted it, as YYYY-MM-DD`)
      }
    }
  }
}
