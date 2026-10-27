// Checks that no file under web/ writes a value into the page as HTML (ASVS V1.2.1, V3.2.2).
//
// Usage: node scripts/check-raw-html.mjs [--root DIR]
//
// The SPA shows every value as text. This check refuses each way to write raw HTML: React's
// dangerouslySetInnerHTML, the innerHTML and outerHTML properties, insertAdjacentHTML, insertHTML,
// createContextualFragment, setHTMLUnsafe, parseHTMLUnsafe, DOMParser, an iframe srcdoc and
// document.write. It reads the source as text, strings and comments included, so a comment that
// names one of them is reported too and must be reworded. It cannot see HTML that a library writes.
// Prints one line per problem on stderr as file:line and exits 1 when any file writes raw HTML.
import { readdirSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const scriptRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

const skippedDirectories = new Set(['node_modules', 'dist', 'coverage'])
const extensions = ['.ts', '.tsx', '.js', '.jsx', '.mjs', '.cjs', '.html']
const rawHtmlPattern =
  /\b(dangerouslySetInnerHTML|innerHTML|outerHTML|insertAdjacentHTML|insertHTML|createContextualFragment|setHTMLUnsafe|parseHTMLUnsafe|DOMParser|srcdoc)\b|\bdocument\s*\.\s*write(ln)?\b/gi

// Every exception is named here with its reason, as { file: 'web/…', reason: '…' }. A listed file
// is not checked at all, so add one only when the SPA must write HTML there.
export const exceptions = []

/** Returns the raw HTML sinks in the source as { line, name }. */
export function findRawHtml(source) {
  const found = []
  source.split('\n').forEach((text, index) => {
    for (const match of text.matchAll(rawHtmlPattern)) {
      found.push({ line: index + 1, name: match[0].replace(/\s+/g, '') })
    }
  })
  return found
}

function findSourceFiles(directory, files = []) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name)
    if (entry.isDirectory()) {
      if (!skippedDirectories.has(entry.name)) findSourceFiles(fullPath, files)
    } else if (entry.isFile() && extensions.includes(path.extname(entry.name))) {
      files.push(fullPath)
    }
  }
  return files
}

/** Checks every source file under root/web, except the allowed ones, and returns the sinks and the file count. */
export function checkRawHtml(root, allowed = exceptions) {
  const skipped = new Set(allowed.map((exception) => exception.file))
  const webRoot = path.join(root, 'web')
  const files = findSourceFiles(webRoot).sort()
  const problems = []
  let fileCount = 0
  for (const file of files) {
    const relativeFile = path.relative(root, file).split(path.sep).join('/')
    if (skipped.has(relativeFile)) continue
    fileCount++
    for (const { line, name } of findRawHtml(readFileSync(file, 'utf8'))) {
      problems.push({ file: relativeFile, line, name })
    }
  }
  return { problems, fileCount }
}

function main(args) {
  const rootIndex = args.indexOf('--root')
  const root = rootIndex >= 0 ? path.resolve(args[rootIndex + 1]) : scriptRoot
  const { problems, fileCount } = checkRawHtml(root)
  for (const { file, line, name } of problems) {
    process.stderr.write(`${file}:${line}: ${name} writes raw HTML. Render the value as text.\n`)
  }
  if (problems.length > 0) return 1
  console.log(`Checked ${fileCount} files.`)
  return 0
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2))
}
