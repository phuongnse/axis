// Checks the structure of the docs, so that they stay small, indexed and free of conflicts.
//
// Usage: node scripts/check-docs.mjs [--root DIR]
//
// - Every Markdown file under docs/ is linked from docs/README.md, except the decision files,
//   which docs/decisions.md lists.
// - Each decision file is docs/decisions/d<n>-<slug>.md, starts with '# D<n>. <title>', where the
//   slug is the title's anchor, and has a 'Status: **<status>**' line whose status starts with
//   Agreed or Proposed.
// - docs/decisions.md has one row per decision file, numbered from 1 with no gaps, in order, with
//   the file's title and status.
// - docs/reference/asvs.md, when it exists, has one row for each requirement of the committed ASVS list
//   (docs/reference/asvs-5.0.0.json) in chapters V1 to V8 for now, with no unknown or repeated id, a
//   status from the list and some evidence. A Met row names only test classes that exist.
// - A doc under docs/ has at most 1,000 lines, AGENTS.md at most 100 and a SKILL.md under
//   .claude/skills/ at most 250, so agents can read what they need without reading whole books.
//
// Prints one line per problem on stderr as file:line and exits 1 when there is any.
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { slugify } from './check-links.mjs'

const scriptRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

export const maxDocLines = 1000
export const maxAgentsLines = 100
export const maxSkillLines = 250
export const asvsListFile = 'docs/reference/asvs-5.0.0.json'
export const asvsStatuses = ['Met', 'Gap', 'Delegated', 'Not applicable']
/** The chapters V1 to V<n> of the ASVS list that need a row. The next issue raises it to 17. */
export const asvsCheckedChapters = 8

const decisionFilePattern = /^d(\d+)-([a-z0-9-]+)\.md$/
const decisionTitlePattern = /^# D(\d+)\. (.+)$/
const statusPattern = /^Status: \*\*((?:Agreed|Proposed)\b[^*]*)\*\*$/
const decisionRowPattern = /^\| \[D(\d+)\]\(decisions\/([^)]+)\) \| (.+?) \| \*\*(.+?)\*\* \|$/
const asvsFile = 'docs/reference/asvs.md'
const asvsChapterPattern = /^V(\d+)\./
const cellSeparator = /(?<!\\)\|/
const testClassPattern = /`([A-Z]\w*Tests)`/g
const classPattern = /\bclass\s+(\w+)/g
const ignoredFolders = new Set(['bin', 'obj', 'node_modules'])
const linkPattern = /\]\(([^)\s#]+)(?:#[^)\s]*)?\)/g

function markdownFiles(directory) {
  const files = []
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name)
    if (entry.isDirectory()) files.push(...markdownFiles(fullPath))
    else if (entry.isFile() && entry.name.endsWith('.md')) files.push(fullPath)
  }
  return files
}

function sourceFiles(directory) {
  if (!existsSync(directory)) return []
  const files = []
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name)
    if (entry.isDirectory()) {
      if (!ignoredFolders.has(entry.name)) files.push(...sourceFiles(fullPath))
    } else if (entry.isFile() && entry.name.endsWith('.cs')) files.push(fullPath)
  }
  return files
}

const lines = (file) => readFileSync(file, 'utf8').replace(/\n$/, '').split('\n')

/** Checks the requirements table of the ASVS page against the committed list of ASVS ids. */
function checkAsvs(root, asvs, report) {
  const listPath = path.join(root, asvsListFile)
  if (!existsSync(listPath)) {
    report(asvs, 1, `missing ${asvsListFile}: the requirements table is checked against it`)
    return
  }
  const list = JSON.parse(readFileSync(listPath, 'utf8')).requirements
  const known = new Set(list.map((requirement) => requirement.id))

  const text = lines(asvs)
  const heading = text.findIndex((line) => line === '## Requirements') + 1
  const seen = new Map()
  let classes
  const testClasses = () => {
    if (!classes) {
      classes = new Set()
      for (const file of [...sourceFiles(path.join(root, 'tests')), ...sourceFiles(path.join(root, 'samples'))]) {
        for (const match of readFileSync(file, 'utf8').matchAll(classPattern)) classes.add(match[1])
      }
    }
    return classes
  }

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
    if (evidence === '') report(asvs, line, `${id} has no evidence`)
    else if (statusName === 'Met') {
      for (const match of text[i].split(cellSeparator)[4].matchAll(testClassPattern)) {
        if (!testClasses().has(match[1])) report(asvs, line, `${id} names the test class ${match[1]}, which no .cs file under tests/ or samples/ declares`)
      }
    }
  }

  for (const { id, level } of list) {
    const chapter = Number(asvsChapterPattern.exec(id)?.[1])
    if (chapter <= asvsCheckedChapters && !seen.has(id)) {
      report(asvs, Math.max(heading, 1), `${id} (Level ${level}) has no row in the requirements table`)
    }
  }
}

/** Checks the docs under root and returns the problems as { file, line, message }. */
export function checkDocs(root) {
  const problems = []
  const docs = path.join(root, 'docs')
  const relative = (file) => path.relative(root, file).split(path.sep).join('/')
  const report = (file, line, message) => problems.push({ file: relative(file), line, message })

  const files = markdownFiles(docs).sort()
  for (const file of files) {
    const count = lines(file).length
    if (count > maxDocLines) {
      report(file, maxDocLines + 1, `${count} lines, over the limit of ${maxDocLines}: move whole sections to a new file`)
    }
  }
  const agents = path.join(root, 'AGENTS.md')
  if (existsSync(agents) && lines(agents).length > maxAgentsLines) {
    report(agents, maxAgentsLines + 1, `${lines(agents).length} lines, over the limit of ${maxAgentsLines}: move detail to docs/`)
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

  // The docs map.
  const map = path.join(docs, 'README.md')
  const decisionsFolder = path.join(docs, 'decisions')
  if (!existsSync(map)) {
    report(map, 1, 'missing: docs/README.md lists every doc')
  } else {
    const linked = new Set()
    for (const line of lines(map)) {
      for (const match of line.matchAll(linkPattern)) linked.add(path.resolve(docs, match[1]))
    }
    for (const file of files) {
      if (file === map || path.dirname(file) === decisionsFolder) continue
      if (!linked.has(file)) report(map, 1, `does not list ${relative(file)}`)
    }
  }

  const asvs = path.join(root, asvsFile)
  if (existsSync(asvs)) checkAsvs(root, asvs, report)

  // The decisions.
  const index = path.join(docs, 'decisions.md')
  if (!existsSync(decisionsFolder) && !existsSync(index)) return problems
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
    return problems
  }
  const rows = []
  lines(index).forEach((line, i) => {
    const row = decisionRowPattern.exec(line)
    if (row) rows.push({ line: i + 1, number: Number(row[1]), name: row[2], title: row[3], status: row[4] })
  })
  rows.forEach((row, i) => {
    if (row.number !== i + 1) report(index, row.line, `row D${row.number} should be D${i + 1}: decisions are numbered in order with no gaps`)
    const decision = decisions.get(row.number)
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

  return problems
}

function main(args) {
  const rootIndex = args.indexOf('--root')
  const root = rootIndex >= 0 ? path.resolve(args[rootIndex + 1]) : scriptRoot
  const problems = checkDocs(root).sort((a, b) => (a.file === b.file ? a.line - b.line : a.file < b.file ? -1 : 1))
  for (const { file, line, message } of problems) process.stderr.write(`${file}:${line}: ${message}\n`)
  if (problems.length > 0) return 1
  console.log('The docs map, the decisions index, the ASVS table and the doc and skill sizes are in order.')
  return 0
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2))
}
