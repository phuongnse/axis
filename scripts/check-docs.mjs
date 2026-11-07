// Checks the structure of the docs, so that they stay small, indexed and free of conflicts.
//
// Usage: node scripts/check-docs.mjs [--root DIR]
//
// The rules live in scripts/lib/docs/, one module for each, with a test of its own.
//
// Prints one line per problem on stderr as file:line and exits 1 when there is any.
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { checkAgents } from './lib/docs/agents.mjs'
import { checkAsvs } from './lib/docs/asvs.mjs'
import { checkDecisions } from './lib/docs/decisions.mjs'
import { checkCodeReferences } from './lib/docs/code-references.mjs'
import { checkDevelopmentUsers } from './lib/docs/development-users.mjs'
import { checkDocsMap } from './lib/docs/docs-map.mjs'
import { gitBaseLineCount, relativePath } from './lib/docs/helpers.mjs'
import { checkPlannedMarkers } from './lib/docs/planned-markers.mjs'
import { checkSizes } from './lib/docs/sizes.mjs'
import { checkThreatTables } from './lib/docs/threat-tables.mjs'

const scriptRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

export const rules = [
  checkSizes,
  checkAgents,
  checkDocsMap,
  checkAsvs,
  checkThreatTables,
  checkDevelopmentUsers,
  checkPlannedMarkers,
  checkDecisions,
  checkCodeReferences,
]

/** Checks the docs under root and returns the problems as { file, line, message }. */
export function checkDocs(root, options = {}) {
  const problems = []
  const report = (file, line, message) => problems.push({ file: relativePath(root, file), line, message })
  for (const rule of rules) rule(root, report, options)
  return problems
}

function main(args) {
  const rootIndex = args.indexOf('--root')
  const root = rootIndex >= 0 ? path.resolve(args[rootIndex + 1]) : scriptRoot
  const baseLineCount = gitBaseLineCount(root)
  const problems = checkDocs(root, { baseLineCount }).sort((a, b) =>
    a.file === b.file ? a.line - b.line : a.file < b.file ? -1 : 1,
  )
  for (const { file, line, message } of problems) process.stderr.write(`${file}:${line}: ${message}\n`)
  if (problems.length > 0) return 1
  console.log('The docs map, the decisions index, the planned markers and where they sit, the ASVS table, the threat model tables, the development users, the decisions, sections and milestones the code cites and the doc sizes, the skill word limit, the AGENTS.md word limits and the scripts it names are in order.')
  return 0
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2))
}
