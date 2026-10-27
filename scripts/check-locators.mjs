// Checks that every Playwright locator under tests/e2e matches a string name or text exactly.
//
// Usage: node scripts/check-locators.mjs [--root DIR]
//
// getByRole, getByLabel, getByText and getByTitle must pass `exact: true` with a string name or
// text, because a partial match can pick the wrong element. A regular expression literal,
// new RegExp(...) or fieldLabel(...) states on purpose how much it matches and is allowed. The check reads the
// source as text, so a name it cannot see, such as one inside an options variable, is reported.
// A spec under tests/e2e/specs/ must not look up a dialog with getByRole('dialog') itself, because a click
// during the dialog's opening is lost. It reaches the dialog through waitForDialog or confirmDialog in dialogs.ts.
// Prints one line per problem on stderr as file:line and exits 1 when any call is loose.
import { readdirSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const scriptRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

const skippedDirectories = new Set(['node_modules', 'playwright-report', 'test-results'])
const locatorPattern = /(?<![\w$])(getByRole|getByLabel|getByText|getByTitle)\s*\(/g
const regexPrecedingCharacters = new Set(['', '(', ',', '=', ':', '[', '!', '&', '|', '?', '{', '}', ';', '>'])
const openers = '([{'
const closers = ')]}'

function blank(text, fill) {
  return text.replace(/[^\n]/g, fill)
}

function lineEnd(source, index) {
  const newline = source.indexOf('\n', index)
  return newline < 0 ? source.length : newline
}

function quoteEnd(source, start) {
  const quote = source[start]
  let index = start + 1
  while (index < source.length && source[index] !== '\n') {
    if (source[index] === '\\') index += 2
    else if (source[index] === quote) return index + 1
    else index++
  }
  return Math.min(index, source.length)
}

function regexEnd(source, start) {
  let index = start + 1
  let inClass = false
  while (index < source.length && source[index] !== '\n') {
    const char = source[index]
    if (char === '\\') index += 2
    else if (inClass) {
      if (char === ']') inClass = false
      index++
    } else if (char === '[') {
      inClass = true
      index++
    } else if (char === '/') {
      index++
      while (/[a-z]/i.test(source[index] ?? '')) index++
      return index
    } else index++
  }
  return Math.min(index, source.length)
}

function templateEnd(source, start) {
  let index = start + 1
  while (index < source.length) {
    const char = source[index]
    if (char === '\\') index += 2
    else if (char === '`') return index + 1
    else if (char === '$' && source[index + 1] === '{') index = maskCode(source, index + 2, true).end
    else index++
  }
  return source.length
}

/**
 * Copies the code from start and blanks comments, strings, templates and regular expressions, so
 * that the masked text keeps the position of every character. Inside a template expression it
 * stops after the closing brace and returns that end.
 */
function maskCode(source, start, inTemplateExpression = false) {
  let masked = ''
  let depth = 0
  let previous = ''
  let index = start
  while (index < source.length) {
    const char = source[index]
    const next = source[index + 1]
    let end
    let fill
    if (char === '/' && next === '/') {
      end = lineEnd(source, index)
      fill = ' '
    } else if (char === '/' && next === '*') {
      const close = source.indexOf('*/', index + 2)
      end = close < 0 ? source.length : close + 2
      fill = ' '
    } else if (char === '"' || char === "'") {
      end = quoteEnd(source, index)
      fill = '_'
    } else if (char === '`') {
      end = templateEnd(source, index)
      fill = '_'
    } else if (char === '/' && regexPrecedingCharacters.has(previous)) {
      end = regexEnd(source, index)
      fill = '_'
    } else {
      if (inTemplateExpression && char === '{') depth++
      if (inTemplateExpression && char === '}') {
        if (depth === 0) return { masked, end: index + 1 }
        depth--
      }
      masked += char
      if (!/\s/.test(char)) previous = char
      index++
      continue
    }
    masked += blank(source.slice(index, end), fill)
    if (fill === '_') previous = '_'
    index = end
  }
  return { masked, end: index }
}

function maskSource(source) {
  return maskCode(source, 0).masked
}

function matchingClose(masked, open) {
  let depth = 0
  for (let index = open; index < masked.length; index++) {
    if (openers.includes(masked[index])) depth++
    else if (closers.includes(masked[index])) {
      depth--
      if (depth === 0) return index
    }
  }
  return -1
}

function trimmedRange(masked, [start, end]) {
  let trimmedStart = start
  let trimmedEnd = end
  while (trimmedStart < trimmedEnd && /\s/.test(masked[trimmedStart])) trimmedStart++
  while (trimmedEnd > trimmedStart && /\s/.test(masked[trimmedEnd - 1])) trimmedEnd--
  return [trimmedStart, trimmedEnd]
}

/** Splits the masked text between start and end at commas outside brackets, dropping a trailing empty part. */
function splitTopLevel(masked, start, end) {
  const ranges = []
  let depth = 0
  let rangeStart = start
  for (let index = start; index < end; index++) {
    const char = masked[index]
    if (openers.includes(char)) depth++
    else if (closers.includes(char)) depth--
    else if (char === ',' && depth === 0) {
      ranges.push(trimmedRange(masked, [rangeStart, index]))
      rangeStart = index + 1
    }
  }
  ranges.push(trimmedRange(masked, [rangeStart, end]))
  const last = ranges[ranges.length - 1]
  if (last[0] === last[1]) ranges.pop()
  return ranges
}

function isObjectLiteral(masked, [start, end]) {
  return end > start && masked[start] === '{' && matchingClose(masked, start) === end - 1
}

function keyText(raw) {
  return raw.replace(/^(['"])(.*)\1$/, '$2')
}

/** Returns the properties of an object literal range as { key, value } with the raw value text. */
function propertiesOf(source, masked, [start, end]) {
  return splitTopLevel(masked, start + 1, end - 1).map(([propertyStart, propertyEnd]) => {
    const colon = masked.slice(propertyStart, propertyEnd).indexOf(':')
    if (colon < 0) {
      const raw = source.slice(propertyStart, propertyEnd).trim()
      return { key: raw, value: raw }
    }
    return {
      key: keyText(source.slice(propertyStart, propertyStart + colon).trim()),
      value: source.slice(propertyStart + colon + 1, propertyEnd).trim(),
    }
  })
}

function isPattern(raw) {
  return raw.startsWith('/') || /^new\s+RegExp\s*\(/.test(raw) || /^fieldLabel\s*\(/.test(raw)
}

function looseReason(method, source, masked, args) {
  const rawOf = ([start, end]) => source.slice(start, end)

  if (method === 'getByRole') {
    if (args.length < 2) return undefined
    if (!isObjectLiteral(masked, args[1])) return 'options must be an object literal'
    const properties = propertiesOf(source, masked, args[1])
    const name = properties.findLast((property) => property.key === 'name')
    if (!name || isPattern(name.value)) return undefined
    const exact = properties.findLast((property) => property.key === 'exact')
    return exact?.value === 'true' ? undefined : 'name without exact: true'
  }

  if (args.length === 0 || isPattern(rawOf(args[0]))) return undefined
  if (args[1] && isObjectLiteral(masked, args[1])) {
    const exact = propertiesOf(source, masked, args[1]).findLast((property) => property.key === 'exact')
    if (exact?.value === 'true') return undefined
  }
  return 'text without exact: true'
}

function lineOf(source, index) {
  return source.slice(0, index).split('\n').length
}

function isDialogLookup(method, source, args) {
  return method === 'getByRole' && args.length > 0 && /^(['"`])dialog\1$/.test(source.slice(...args[0]))
}

/** Returns every locator call with its line, its loose reason if any, and whether it looks up a dialog. */
function locatorCalls(source) {
  const masked = maskSource(source)
  const calls = []
  for (const match of masked.matchAll(locatorPattern)) {
    const method = match[1]
    const line = lineOf(source, match.index)
    const open = match.index + match[0].length - 1
    const close = matchingClose(masked, open)
    if (close < 0) {
      calls.push({ line, method, reason: 'call is not closed', dialog: false })
      continue
    }
    const args = splitTopLevel(masked, open + 1, close)
    calls.push({
      line,
      method,
      reason: looseReason(method, source, masked, args),
      dialog: isDialogLookup(method, source, args),
    })
  }
  return calls
}

/** Returns the locator calls in the source that match a string name or text without exact: true. */
export function findLooseLocators(source) {
  return locatorCalls(source)
    .filter((call) => call.reason)
    .map(({ line, method, reason }) => ({ line, method, reason }))
}

/** Returns the lines of the getByRole('dialog') calls in the source. */
export function findDialogLookups(source) {
  return locatorCalls(source)
    .filter((call) => call.dialog)
    .map(({ line }) => line)
}

function findTypeScriptFiles(directory, files = []) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name)
    if (entry.isDirectory()) {
      if (!skippedDirectories.has(entry.name)) findTypeScriptFiles(fullPath, files)
    } else if (entry.isFile() && entry.name.endsWith('.ts')) {
      files.push(fullPath)
    }
  }
  return files
}

/** Checks every TypeScript file under root/tests/e2e and returns the loose calls and the counts checked. */
export function checkLocators(root) {
  const files = findTypeScriptFiles(path.join(root, 'tests', 'e2e')).sort()
  const problems = []
  let callCount = 0
  for (const file of files) {
    const relativeFile = path.relative(root, file).split(path.sep).join('/')
    const calls = locatorCalls(readFileSync(file, 'utf8'))
    callCount += calls.length
    const isSpec = relativeFile.startsWith('tests/e2e/specs/')
    for (const { line, method, reason, dialog } of calls) {
      if (reason) problems.push({ file: relativeFile, line, method, reason })
      if (dialog && isSpec) {
        problems.push({ file: relativeFile, line, method, reason: 'dialog without waitForDialog or confirmDialog' })
      }
    }
  }
  return { problems, callCount, fileCount: files.length }
}

function main(args) {
  const rootIndex = args.indexOf('--root')
  const root = rootIndex >= 0 ? path.resolve(args[rootIndex + 1]) : scriptRoot
  const { problems, callCount, fileCount } = checkLocators(root)
  for (const { file, line, method, reason } of problems) {
    process.stderr.write(`${file}:${line}: ${method} ${reason}\n`)
  }
  if (problems.length > 0) return 1
  console.log(`Checked ${callCount} locator calls in ${fileCount} files.`)
  return 0
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2))
}
