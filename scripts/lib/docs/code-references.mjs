// Checks the references that the code and the docs make, so that they still point at something that holds.
//
// - A D<n> in the code or in the prose of docs/ is a decision in force in docs/decisions.md. A decision
//   file may also cite the decisions its successor's row says it superseded.
// - A docs/<path>.md#<anchor> in the code names a file and a section that exist.
// - An M<n> in the code names a milestone, which is progress and history, so the code says what it means.
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { collectHeadings, forEachProseLine } from '../../check-links.mjs'
import { decisionRowPattern, successorPattern, supersededRowPattern } from './decisions.mjs'
import { lines, markdownFiles, relativePath } from './helpers.mjs'

const codeFolders = [
  'src',
  path.join('web', 'src'),
  'tests',
  'scripts',
  'controlplane',
  'samples',
  'deploy',
  'modules',
  '.github',
]
const skippedDirectories = new Set([
  'bin',
  'obj',
  'node_modules',
  'dist',
  'artifacts',
  'wwwroot',
  'playwright-report',
  'test-results',
])
const citationPattern = /(?<![\w:"'])D(\d+)\b/g
const docsReferencePattern = /(?<![\w.-])docs\/([\w./-]+\.md)#([\w-]+)/g
const milestonePattern = /\bM(\d+)\b/g
const decisionFileNumber = /^d(\d+)-/
const citeAdvice = 'cite the decision in force, or link the reference section that holds the rule'

function codeFiles(directory, files = []) {
  if (!existsSync(directory)) return files
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name)
    if (entry.isDirectory()) {
      if (!skippedDirectories.has(entry.name)) codeFiles(fullPath, files)
    } else if (entry.isFile()) {
      files.push(fullPath)
    }
  }
  return files
}

function isChecksOwnTest(rel) {
  return rel.startsWith('scripts/fixtures/') || (rel.startsWith('scripts/') && rel.endsWith('.test.mjs'))
}

function decisionsIndex(root) {
  const inForce = new Set()
  const supersededBy = new Map()
  const index = path.join(root, 'docs', 'decisions.md')
  if (!existsSync(index)) return { inForce, supersededBy }
  for (const line of lines(index)) {
    const row = decisionRowPattern.exec(line)
    if (row) inForce.add(Number(row[1]))
    const superseded = supersededRowPattern.exec(line)
    if (!superseded) continue
    for (const successor of superseded[3].matchAll(successorPattern)) {
      const number = Number(successor[1])
      supersededBy.set(number, [...(supersededBy.get(number) ?? []), Number(superseded[1])])
    }
  }
  return { inForce, supersededBy }
}

/** Checks the D<n>, docs/<path>#<anchor> and M<n> references of the code and the docs under root. */
export function checkCodeReferences(root, report) {
  const { inForce, supersededBy } = decisionsIndex(root)
  const docsFolder = path.join(root, 'docs')
  const decisionsFolder = path.join(docsFolder, 'decisions')
  const indexFile = path.join(docsFolder, 'decisions.md')
  const sectionCache = new Map()
  const sectionsOf = (file) => {
    if (!sectionCache.has(file)) sectionCache.set(file, new Set(collectHeadings(readFileSync(file, 'utf8'))))
    return sectionCache.get(file)
  }

  const checkCitations = (file, lineNumber, text, allowed) => {
    for (const match of text.matchAll(citationPattern)) {
      const number = Number(match[1])
      if (inForce.has(number) || allowed.has(number)) continue
      report(file, lineNumber, `D${number} is not a decision in force in docs/decisions.md: ${citeAdvice}`)
    }
  }

  const checkDocsReferences = (file, lineNumber, text) => {
    for (const match of text.matchAll(docsReferencePattern)) {
      const [, docPath, anchor] = match
      const target = path.join(docsFolder, docPath)
      if (!existsSync(target)) {
        report(file, lineNumber, `docs/${docPath} does not exist`)
      } else if (!sectionsOf(target).has(anchor.toLowerCase())) {
        report(file, lineNumber, `docs/${docPath}#${anchor}: no such section`)
      }
    }
  }

  for (const folder of codeFolders) {
    for (const file of codeFiles(path.join(root, folder))) {
      if (isChecksOwnTest(relativePath(root, file))) continue
      const buffer = readFileSync(file)
      if (buffer.includes(0)) continue
      buffer.toString('utf8').split('\n').forEach((text, index) => {
        checkCitations(file, index + 1, text, new Set())
        checkDocsReferences(file, index + 1, text)
        for (const match of text.matchAll(milestonePattern)) {
          report(file, index + 1, `M${match[1]} names a milestone: say what the code means instead`)
        }
      })
    }
  }

  for (const file of existsSync(docsFolder) ? markdownFiles(docsFolder).sort() : []) {
    if (file === indexFile) continue
    const inDecisions = path.dirname(file) === decisionsFolder
    const ownNumber = inDecisions ? Number(decisionFileNumber.exec(path.basename(file))?.[1]) : undefined
    const allowed = new Set(supersededBy.get(ownNumber) ?? [])
    forEachProseLine(readFileSync(file, 'utf8'), (text, lineNumber) => checkCitations(file, lineNumber, text, allowed))
  }
}
