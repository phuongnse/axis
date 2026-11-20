// - Every ASVS table under docs/, found by its header line, such as the ones in docs/reference/asvs-*.md, has
//   one row for each requirement of the committed ASVS list (docs/reference/asvs-5.0.0.json) across all of them,
//   with no unknown or repeated id, a status from the list and some evidence. A Met row names only test classes
//   that exist. A Gap row is refused: the check runs offline, so it cannot tell whether the issue a Gap links is still open.
import { existsSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { lines, markdownFiles, relativePath, tableRows, testClassLookup, testClassPattern } from './helpers.mjs'

export const asvsListFile = 'docs/reference/asvs-5.0.0.json'
export const asvsStatuses = ['Met', 'Delegated', 'Not applicable']
const asvsFile = 'docs/reference/asvs.md'
const asvsHeader = '| Id | Requirement | Status | Evidence |'

/** Checks the requirements tables of the ASVS pages under docs/ against the committed list of ASVS ids. */
export function checkAsvs(root, report) {
  const asvs = path.join(root, asvsFile)
  if (!existsSync(asvs)) return
  const listPath = path.join(root, asvsListFile)
  if (!existsSync(listPath)) {
    report(asvs, 1, `missing ${asvsListFile}: the requirements table is checked against it`)
    return
  }
  const list = JSON.parse(readFileSync(listPath, 'utf8')).requirements
  const known = new Set(list.map((requirement) => requirement.id))
  const testClasses = testClassLookup(root)
  const seen = new Map()

  for (const file of markdownFiles(path.join(root, 'docs')).sort()) {
    for (const { index, raw } of tableRows(lines(file), asvsHeader)) {
      if (raw.length < 6) continue
      const [id, , status, evidence] = raw.slice(1, 5).map((cell) => cell.trim().replace(/^`(.*)`$/, '$1'))
      const line = index + 1
      if (!known.has(id)) report(file, line, `${id} is not a Level 1 or Level 2 id in ${asvsListFile}`)
      else if (!seen.has(id)) seen.set(id, { file, line })
      else {
        const first = seen.get(id)
        if (first.file === file) report(file, line, `${id} has a row already, on line ${first.line}`)
        else report(file, line, `${id} has a row already, in ${relativePath(root, first.file)} on line ${first.line}`)
      }
      const statusName = /^\*\*(.+)\*\*$/.exec(status)?.[1]
      if (statusName === 'Gap') report(file, line, `${id} is a Gap: meet it in this change, or wait for the issue that closes it`)
      else if (!asvsStatuses.includes(statusName)) {
        report(file, line, `${id} status '${status}' is not one of ${asvsStatuses.map((name) => `**${name}**`).join(', ')}`)
      }
      if (evidence === '') report(file, line, `${id} has no evidence`)
      else if (statusName === 'Met') {
        for (const match of raw[4].matchAll(testClassPattern)) {
          if (!testClasses().has(match[1])) report(file, line, `${id} names the test class ${match[1]}, which no .cs file under tests/, controlplane/tests/ or samples/ declares`)
        }
      }
    }
  }

  const heading = Math.max(lines(asvs).findIndex((line) => line === '## Requirements') + 1, 1)
  for (const { id, level } of list) {
    if (!seen.has(id)) report(asvs, heading, `${id} (Level ${level}) has no row in any ASVS table`)
  }
}
