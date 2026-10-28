// Checks that no SPA code is open to prototype pollution (ASVS V15.3.6).
//
// Usage: node scripts/check-prototype-pollution.mjs [--root DIR]
//
// A key such as __proto__ that reaches obj[key] = value changes the prototype of every object.
// This check refuses each way the SPA could do that: the __proto__ name, a write into any
// .prototype, setPrototypeOf, Object.assign and a write to a computed key such as obj[key] = value,
// unless the key is a number or string literal. Spread syntax and { [key]: value } literals create own
// properties and pass. Use setOwn from web/src/platform/ownProperty.ts, or a Map, for dynamic keys.
// It reads the source as text, strings and comments included, so a comment that names one of them is
// reported too and must be reworded. It scans web/src and skips test files and web/src/test. It cannot
// see pollution inside a library.
// Prints one line per problem on stderr as file:line and exits 1 when any file is open to it.
import { readdirSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const scriptRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

const skippedDirectories = new Set(['node_modules', 'dist', 'coverage'])
const extensions = ['.ts', '.tsx', '.js', '.jsx', '.mjs', '.cjs']
const testFile = /\.test\.[jt]sx?$/

const patterns = [
  { name: '__proto__', pattern: /\b__proto__\b/g },
  {
    name: 'prototype write',
    pattern: /\.prototype\s*\.\s*[\w$]+\s*(\?\?|\|\|)?=(?![=>])|\.prototype\s*\[[^\]]*\]\s*(\?\?|\|\|)?=(?![=>])/g,
  },
  {
    name: 'defineProperty on a prototype',
    pattern: /\bdefineProperty\s*\(\s*[\w$.]+\.prototype\b/g,
  },
  { name: 'setPrototypeOf', pattern: /\bsetPrototypeOf\b/g },
  { name: 'Object.assign', pattern: /\bObject\s*\.\s*assign\s*\(/g },
]
// An identifier, call or index, then [key] with a key that is not a literal, then an assignment.
const computedWrite = /([\w$)\]])\[((?:[^[\]]|\[[^[\]]*\])+)\]\s*(\?\?|\|\|)?=(?![=>])/g
const literalKey = /^\s*(\d+|'[^']*'|"[^"]*")\s*$/

// Every exception is named here with its reason, as { file: 'web/…', reason: '…' }. A listed file
// is not checked at all, so add one only when the SPA must write there.
export const exceptions = [
  {
    file: 'web/src/designSystem/formSubmission.ts',
    reason:
      'The design-system previews patch HTMLFormElement.prototype.requestSubmit to show a submit, and no user input reaches that code.',
  },
]

/** Returns the code open to prototype pollution in the source as { line, name }. */
export function findPrototypePollution(source) {
  const found = []
  source.split('\n').forEach((text, index) => {
    const line = index + 1
    for (const { name, pattern } of patterns) {
      for (const _ of text.matchAll(pattern)) found.push({ line, name })
    }
    for (const match of text.matchAll(computedWrite)) {
      if (literalKey.test(match[2])) continue
      // A write into a prototype is reported by its own rule.
      if (/\bprototype\s*$/.test(text.slice(0, match.index + 1))) continue
      found.push({ line, name: 'computed key write' })
    }
  })
  return found
}

function findSourceFiles(directory, files = [], testSupport = path.join(directory, 'test')) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name)
    if (entry.isDirectory()) {
      if (!skippedDirectories.has(entry.name) && fullPath !== testSupport) findSourceFiles(fullPath, files, testSupport)
    } else if (entry.isFile() && extensions.includes(path.extname(entry.name)) && !testFile.test(entry.name)) {
      files.push(fullPath)
    }
  }
  return files
}

/** Checks every source file under root/web/src, except the allowed ones, and returns the problems and the file count. */
export function checkPrototypePollution(root, allowed = exceptions) {
  const skipped = new Set(allowed.map((exception) => exception.file))
  const files = findSourceFiles(path.join(root, 'web/src')).sort()
  const problems = []
  let fileCount = 0
  for (const file of files) {
    const relativeFile = path.relative(root, file).split(path.sep).join('/')
    if (skipped.has(relativeFile)) continue
    fileCount++
    for (const { line, name } of findPrototypePollution(readFileSync(file, 'utf8'))) {
      problems.push({ file: relativeFile, line, name })
    }
  }
  return { problems, fileCount }
}

function main(args) {
  const rootIndex = args.indexOf('--root')
  const root = rootIndex >= 0 ? path.resolve(args[rootIndex + 1]) : scriptRoot
  const { problems, fileCount } = checkPrototypePollution(root)
  for (const { file, line, name } of problems) {
    process.stderr.write(`${file}:${line}: ${name} is open to prototype pollution. Use setOwn or a Map.\n`)
  }
  if (problems.length > 0) return 1
  console.log(`Checked ${fileCount} files.`)
  return 0
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2))
}
