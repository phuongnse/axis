// Checks the repository layout tree of docs/repository-layout.md against the folders on disk and the projects of Axis.slnx.
// This is the test that docs/writing-docs.md rule 7 asks for a list of what the code defines. A tree entry is a line in the
// first text block under a Repository layout heading, whose first word ends in a slash. An entry's folder is the path of the
// entries above it, and a segment written as <Name> matches any folder name. The tree lists the children of each folder that an
// entry lies under, so a leaf entry such as src/Axis.Server/ is not looked into. Hidden folders and folders that .gitignore
// lists are skipped.
import { existsSync, readdirSync } from 'node:fs'
import path from 'node:path'
import { lines, markdownFiles, relativePath } from './helpers.mjs'

const headingPattern = /^#+ Repository layout\s*$/
const fenceStart = /^```text\s*$/
const entryPattern = /^(?:[\w.<>-]+\/)+$/
const placeholder = /<[^>]+>/
const placeholderPart = /^<[^>]+>$/
const projectPattern = /<Project Path="([^"]+)"/
const layoutDoc = path.join('docs', 'repository-layout.md')

/** Checks the repository layout tree against the folders under root and the projects of Axis.slnx. */
export function checkRepositoryLayout(root, report) {
  const solution = path.join(root, 'Axis.slnx')
  if (!existsSync(solution)) return

  const tree = layoutTree(root)
  if (!tree) {
    report(path.join(root, layoutDoc), 1, 'needs a text block under a Repository layout heading that names the top-level folders and the projects in them')
    return
  }
  const doc = relativePath(root, tree.file)
  const { entries } = tree
  const lineOf = (folder) => {
    if (folder.length === 0) return tree.fence
    const entry = entries.find((candidate) => matches(candidate.segments, folder)) ?? entries.find((candidate) => isUnder(candidate.segments, folder))
    return entry.line
  }

  const ignored = ignoredFolders(root)
  const folders = []
  const queue = [[]]
  while (queue.length > 0) {
    const parent = queue.shift()
    if (!isExpanded(parent, entries)) continue
    for (const name of childFolders(root, parent, ignored)) {
      const folder = [...parent, name]
      folders.push(folder)
      if (!isExpanded(folder, entries) && !entries.some((entry) => matches(entry.segments, folder))) {
        report(tree.file, lineOf(parent), `${folder.join('/')}/ is a folder but is not in the repository layout tree of ${doc}`)
      }
      queue.push(folder)
    }
  }

  for (const entry of entries) {
    if (!folders.some((folder) => matches(entry.segments, folder))) {
      report(tree.file, entry.line, `${entry.path}/ is in the repository layout tree but is not a folder`)
    }
  }

  lines(solution).forEach((text, index) => {
    const match = projectPattern.exec(text)
    if (!match) return
    const folder = path.posix.dirname(match[1].replace(/\\/g, '/')).split('/')
    if (!entries.some((entry) => matches(entry.segments, folder))) {
      report(solution, index + 1, `${folder.join('/')}/ holds a project of Axis.slnx but is not in the repository layout tree of ${doc}`)
    }
  })
}

/** Returns the first text block under a Repository layout heading, with its file, its fence line and its entries, or undefined. */
function layoutTree(root) {
  const docs = path.join(root, 'docs')
  if (!existsSync(docs)) return undefined
  for (const file of markdownFiles(docs).sort()) {
    const text = lines(file)
    const heading = text.findIndex((line) => headingPattern.test(line))
    if (heading < 0) continue
    const open = text.findIndex((line, index) => index > heading && fenceStart.test(line))
    if (open < 0) return undefined
    const close = text.findIndex((line, index) => index > open && line === '```')
    const stop = close < 0 ? text.length : close

    const entries = []
    const stack = []
    for (let index = open + 1; index < stop; index++) {
      const match = /^( *)(\S+)/.exec(text[index])
      if (!match || !entryPattern.test(match[2])) continue
      const indent = match[1].length
      while (stack.length > 0 && stack.at(-1).indent >= indent) stack.pop()
      const segments = [...(stack.at(-1)?.segments ?? []), ...match[2].slice(0, -1).split('/')]
      entries.push({ path: segments.join('/'), segments, line: index + 1 })
      stack.push({ indent, segments })
    }
    return { file, fence: open + 1, entries }
  }
  return undefined
}

/** Returns the names of the folders that .gitignore lists: a name anywhere, and a path from the root. */
function ignoredFolders(root) {
  const names = new Set()
  const paths = new Set()
  const gitignore = path.join(root, '.gitignore')
  if (!existsSync(gitignore)) return { names, paths }
  for (const line of lines(gitignore)) {
    const pattern = line.trim()
    if (!pattern || pattern.startsWith('#') || pattern.startsWith('!') || pattern.includes('*') || !pattern.endsWith('/')) continue
    const folder = pattern.slice(0, -1).replace(/^\//, '')
    if (folder.includes('/')) paths.add(folder)
    else names.add(folder)
  }
  return { names, paths }
}

/** Returns the names of the folders under parent that are not hidden and not ignored, sorted. */
function childFolders(root, parent, ignored) {
  return readdirSync(path.join(root, ...parent), { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    .filter((name) => !name.startsWith('.') && !ignored.names.has(name) && !ignored.paths.has([...parent, name].join('/')))
    .sort()
}

/** Returns whether a folder has children that the tree must list: the root, or a folder that an entry lies under, which the tree needs to reach it. */
function isExpanded(folder, entries) {
  return folder.length === 0 || entries.some((entry) => isUnder(entry.segments, folder))
}

/** Returns whether an entry lies under the folder, strictly, through its segments. */
function isUnder(segments, folder) {
  return segments.length > folder.length && matches(segments.slice(0, folder.length), folder)
}

/** Returns whether an entry's segments name the folder's segments, a <Name> segment naming any single folder name. */
function matches(segments, folder) {
  return segments.length === folder.length && segments.every((segment, index) => segmentMatches(segment, folder[index]))
}

function segmentMatches(segment, name) {
  if (!placeholder.test(segment)) return segment === name
  const source = segment
    .split(/(<[^>]+>)/)
    .map((part) => (placeholderPart.test(part) ? '[^/]+' : part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')))
    .join('')
  return new RegExp(`^${source}$`).test(name)
}
