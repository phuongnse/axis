// - Every Markdown file under docs/ is linked from docs/README.md, except the decision files,
//   which docs/decisions.md lists.
import { existsSync } from 'node:fs'
import path from 'node:path'
import { lines, markdownFiles, relativePath } from './helpers.mjs'

const linkPattern = /\]\(([^)\s#]+)(?:#[^)\s]*)?\)/g

export function checkDocsMap(root, report) {
  const docs = path.join(root, 'docs')
  const files = markdownFiles(docs).sort()
  const map = path.join(docs, 'README.md')
  const decisionsFolder = path.join(docs, 'decisions')
  if (!existsSync(map)) {
    report(map, 1, 'missing: docs/README.md lists every doc')
    return
  }
  const linked = new Set()
  for (const line of lines(map)) {
    for (const match of line.matchAll(linkPattern)) linked.add(path.resolve(docs, match[1]))
  }
  for (const file of files) {
    if (file === map || path.dirname(file) === decisionsFolder) continue
    if (!linked.has(file)) report(map, 1, `does not list ${relativePath(root, file)}`)
  }
}
