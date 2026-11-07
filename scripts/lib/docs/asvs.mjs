// - docs/reference/asvs.md, when it exists, has one row for each requirement of the committed ASVS list
//   (docs/reference/asvs-5.0.0.json), with no unknown or repeated id, a status from the list and some
//   evidence. A Met row names only test classes that exist. A Gap row links the GitHub issue that
//   closes it.
import { existsSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { cellSeparator, issueLinkPattern, lines, testClassLookup, testClassPattern } from './helpers.mjs'

export const asvsListFile = 'docs/reference/asvs-5.0.0.json'
export const asvsStatuses = ['Met', 'Gap', 'Delegated', 'Not applicable']
const asvsFile = 'docs/reference/asvs.md'

/** Checks the requirements table of the ASVS page against the committed list of ASVS ids. */
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

  const text = lines(asvs)
  const heading = text.findIndex((line) => line === '## Requirements') + 1
  const seen = new Map()

  for (let i = Math.max(heading, 0); i < text.length; i++) {
    const cells = text[i].split(cellSeparator)
    if (cells.length < 6 || cells[0].trim() !== '') continue
    const [id, , status, evidence] = cells.slice(1, 5).map((cell) => cell.trim().replace(/^`(.*)`$/, '$1'))
    if (id === 'Id' || /^-+$/.test(id)) continue
    const line = i + 1
    if (!known.has(id)) report(asvs, line, `${id} is not a Level 1 or Level 2 id in ${asvsListFile}`)
    else if (seen.has(id)) report(asvs, line, `${id} has a row already, on line ${seen.get(id)}`)
    else seen.set(id, line)
    const statusName = /^\*\*(.+)\*\*$/.exec(status)?.[1]
    if (!asvsStatuses.includes(statusName)) {
      report(asvs, line, `${id} status '${status}' is not one of ${asvsStatuses.map((name) => `**${name}**`).join(', ')}`)
    }
    const rawEvidence = text[i].split(cellSeparator)[4]
    if (evidence === '') report(asvs, line, `${id} has no evidence`)
    else if (statusName === 'Met') {
      for (const match of rawEvidence.matchAll(testClassPattern)) {
        if (!testClasses().has(match[1])) report(asvs, line, `${id} names the test class ${match[1]}, which no .cs file under tests/, controlplane/tests/ or samples/ declares`)
      }
    } else if (statusName === 'Gap' && !issueLinkPattern.test(rawEvidence)) {
      report(asvs, line, `${id} is a Gap and links no issue: link the GitHub issue that closes it`)
    }
  }

  for (const { id, level } of list) {
    if (!seen.has(id)) report(asvs, Math.max(heading, 1), `${id} (Level ${level}) has no row in the requirements table`)
  }
}
