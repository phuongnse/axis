// - The Studio spec folders table under docs/, found by its Specs and Folder header cells, lists in backticks in its
//   Specs column every spec under tests/e2e/specs that calls startServer with Studio__Folders__ or Studio__ApplicationsPath,
//   and no other spec. Its Folder column is not read.
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { cellSeparator, lines, markdownFiles, relativePath } from './helpers.mjs'

const specsFolder = path.join('tests', 'e2e', 'specs')
const defaultTableFile = path.join('docs', 'testing.md')
const serverCall = /startServer\(/
const studioSetting = /Studio__(Folders__\d+|ApplicationsPath)\b/

/** Checks the Studio spec folders table against the specs that start their own server over a Studio folder. */
export function checkStudioSpecFolders(root, report) {
  const specsPath = path.join(root, specsFolder)
  if (!existsSync(specsPath)) return

  const specs = new Map()
  for (const entry of readdirSync(specsPath).filter((name) => name.endsWith('.spec.ts')).sort()) {
    const file = path.join(specsPath, entry)
    const text = readFileSync(file, 'utf8')
    if (!serverCall.test(text) || !studioSetting.test(text)) continue
    const line = text.split('\n').findIndex((source) => studioSetting.test(source)) + 1
    specs.set(entry.slice(0, -'.spec.ts'.length), { file, line })
  }

  const table = studioFolderTable(root)
  if (!table) {
    report(path.join(root, defaultTableFile), 1, 'needs a table with Specs and Folder columns that lists the specs that start a server over their own Studio folder')
    return
  }
  const tableDoc = relativePath(root, table.file)
  for (const [name, { file, line }] of specs) {
    if (!table.rows.has(name)) report(file, line, `${name} starts a server over its own Studio folder but is not in the Studio spec folders table of ${tableDoc}`)
  }
  for (const [name, line] of table.rows) {
    if (!specs.has(name)) report(table.file, line, `${name} is in the Studio spec folders table but starts no server over its own Studio folder`)
  }
}

/** Returns the first table under docs/ with Specs and Folder header cells, with the spec names of its Specs column and their lines. */
function studioFolderTable(root) {
  for (const file of markdownFiles(path.join(root, 'docs')).sort()) {
    const text = lines(file)
    const header = text.findIndex((line) => {
      if (!line.startsWith('|')) return false
      const cells = line.split(cellSeparator).map((cell) => cell.trim())
      return cells.includes('Specs') && cells.includes('Folder')
    })
    if (header < 0) continue
    const specsColumn = text[header].split(cellSeparator).map((cell) => cell.trim()).indexOf('Specs')
    const rows = new Map()
    for (let i = header + 1; i < text.length && text[i].startsWith('|'); i++) {
      const cell = text[i].split(cellSeparator)[specsColumn] ?? ''
      for (const match of cell.matchAll(/`([^`]+)`/g)) rows.set(match[1], i + 1)
    }
    return { file, rows }
  }
  return undefined
}
