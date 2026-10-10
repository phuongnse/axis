// Reports on the JUnit XML files written by the test scripts.
//
// Usage: node scripts/test-report.mjs [--results-dir DIR] [--root DIR] [--skipped NAME]... NAME:CWD...
//
// Each suite NAME has its results in DIR/NAME.xml. CWD is the directory, relative to
// the root, that the suite's relative file paths start from. The Markdown summary is
// appended to $GITHUB_STEP_SUMMARY when it is set and printed otherwise. On GitHub
// Actions every failed test also gets an ::error annotation. A suite named by --skipped did
// not run and has no results file. It is reported as skipped. Exits 1 when any other suite
// has failed tests or no results file.
import { appendFileSync, existsSync, readFileSync, statSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const scriptRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

const entities = { lt: '<', gt: '>', amp: '&', quot: '"', apos: "'" }

function unescapeXml(text) {
  return text.replace(/&(#x[0-9a-fA-F]+|#[0-9]+|[a-z]+);/g, (match, entity) => {
    if (entity.startsWith('#x')) return String.fromCodePoint(parseInt(entity.slice(2), 16))
    if (entity.startsWith('#')) return String.fromCodePoint(parseInt(entity.slice(1), 10))
    return entities[entity] ?? match
  })
}

function parseAttributes(text) {
  const attributes = {}
  for (const match of text.matchAll(/([^\s=]+)\s*=\s*(?:"([^"]*)"|'([^']*)')/g)) {
    attributes[match[1]] = unescapeXml(match[2] ?? match[3])
  }
  return attributes
}

const tokenPattern =
  /<!\[CDATA\[([\s\S]*?)\]\]>|<!--[\s\S]*?-->|<[?!][^>]*>|<(\/?)([^\s/>]+)((?:[^>"']|"[^"]*"|'[^']*')*?)(\/?)>|([^<]+)/g

/** Parses a JUnit XML document into its test cases and total duration in seconds. */
export function parseJunit(xml) {
  const tests = []
  let rootTime
  let outerSuiteTime = 0
  let caseTime = 0
  let suiteDepth = 0
  let current
  let inFailure = false

  for (const [, cdata, closing, tag, attributeText, selfClosing, text] of xml.matchAll(tokenPattern)) {
    if (tag === undefined) {
      if (inFailure) current.details += cdata ?? unescapeXml(text ?? '')
      continue
    }
    if (closing) {
      if (tag === 'testsuite') suiteDepth--
      else if (tag === 'failure' || tag === 'error') inFailure = false
      else if (tag === 'testcase' && current) {
        tests.push(current)
        current = undefined
      }
      continue
    }

    const attributes = parseAttributes(attributeText)
    const time = Number(attributes.time) || 0
    if (tag === 'testsuites') {
      if (attributes.time !== undefined) rootTime = time
    } else if (tag === 'testsuite') {
      if (suiteDepth === 0) outerSuiteTime += time
      if (!selfClosing) suiteDepth++
    } else if (tag === 'testcase') {
      caseTime += time
      current = {
        name: attributes.name ?? '',
        classname: attributes.classname,
        file: attributes.file,
        line: attributes.line ? Number(attributes.line) : undefined,
        status: 'passed',
        message: '',
        details: '',
      }
      if (selfClosing) {
        tests.push(current)
        current = undefined
      }
    } else if (current && (tag === 'failure' || tag === 'error')) {
      if (current.status !== 'failed') {
        current.status = 'failed'
        current.message = attributes.message ?? ''
        inFailure = !selfClosing
      }
    } else if (current && tag === 'skipped' && current.status === 'passed') {
      current.status = 'skipped'
    }
  }

  return { tests, time: rootTime ?? (outerSuiteTime || caseTime) }
}

// Stack frames: "in /path/File.cs:line 12" (.NET) and "path/file.ts:12:5" (Node).
const framePattern = / in (\S.*?):line (\d+)|((?:[A-Za-z]:[\\/])?[^\s()'"`<>:]+):(\d+):\d+/g

function resolveFile(candidate, root, cwd) {
  const filePath = candidate.replace(/^file:\/\//, '')
  const options = path.isAbsolute(filePath)
    ? [filePath]
    : [path.resolve(root, cwd, filePath), path.resolve(root, filePath)]
  for (const option of options) {
    const relative = path.relative(root, option)
    if (!relative || relative.startsWith('..') || path.isAbsolute(relative)) continue
    if (relative.split(path.sep).includes('node_modules')) continue
    if (existsSync(option) && statSync(option).isFile()) return relative.split(path.sep).join('/')
  }
  return undefined
}

/** Finds the repository-relative file and line of a failed test, when known. */
export function locateFailure(test, root, cwd) {
  const frames = []
  for (const match of `${test.message}\n${test.details}`.matchAll(framePattern)) {
    const file = resolveFile(match[1] ?? match[3], root, cwd)
    if (file) frames.push({ file, line: Number(match[2] ?? match[4]) })
  }
  if (test.file) {
    const file = resolveFile(test.file, root, cwd)
    if (file) return { file, line: test.line ?? frames.find((frame) => frame.file === file)?.line }
  }
  return frames[0] ?? {}
}

/** Reads the results of each suite. Suites are "NAME:CWD" strings or { name, cwd } objects. */
export function buildReport({ suites, resultsDir, root, skipped = [] }) {
  const results = suites.map((suite) => {
    const [name, cwd = '.'] = typeof suite === 'string' ? suite.split(/:(.*)/s) : [suite.name, suite.cwd]
    if (skipped.includes(name)) {
      return { name, missing: false, unaffected: true, passed: 0, failed: 0, skipped: 0, duration: 0, failures: [] }
    }
    const file = path.join(resultsDir, `${name}.xml`)
    if (!existsSync(file)) return { name, missing: true, passed: 0, failed: 0, skipped: 0, duration: 0, failures: [] }

    const { tests, time } = parseJunit(readFileSync(file, 'utf8'))
    const count = (status) => tests.filter((test) => test.status === status).length
    const failures = tests
      .filter((test) => test.status === 'failed')
      .map((test) => ({
        suite: name,
        name: test.name,
        message: (test.message || test.details).trim(),
        ...locateFailure(test, root, cwd || '.'),
      }))
    return {
      name,
      missing: false,
      passed: count('passed'),
      failed: failures.length,
      skipped: count('skipped'),
      duration: time,
      failures,
    }
  })
  return { suites: results, ok: results.every((suite) => !suite.missing && suite.failed === 0) }
}

export function formatDuration(seconds) {
  if (seconds < 60) return `${seconds.toFixed(1)}s`
  const minutes = Math.floor(seconds / 60)
  return `${minutes}m ${Math.round(seconds - minutes * 60)}s`
}

function codeSpan(text) {
  const longest = Math.max(0, ...(text.match(/`+/g) ?? []).map((run) => run.length))
  const fence = '`'.repeat(longest + 1)
  const padding = longest > 0 ? ' ' : ''
  return `${fence}${padding}${text}${padding}${fence}`
}

function codeBlock(text, indent) {
  const longest = Math.max(2, ...(text.match(/`+/g) ?? []).map((run) => run.length))
  const fence = '`'.repeat(longest + 1)
  return [`${fence}text`, ...text.split(/\r?\n/), fence].map((line) => (line ? indent + line : line)).join('\n')
}

const tableCell = (text) => String(text).replace(/\|/g, '\\|')

/** The Markdown summary: one table row per suite, then each failed test. */
export function formatSummary(report) {
  const lines = [
    '## Test results',
    '',
    '| Suite | Passed | Failed | Skipped | Duration |',
    '| --- | ---: | ---: | ---: | ---: |',
  ]
  for (const suite of report.suites) {
    if (suite.unaffected) {
      lines.push(`| ${tableCell(suite.name)} (skipped: not affected by this change) | - | - | - | - |`)
    } else if (suite.missing) {
      lines.push(`| ${tableCell(suite.name)} (missing: no results file) | - | - | - | - |`)
    } else {
      lines.push(
        `| ${tableCell(suite.name)} | ${suite.passed} | ${suite.failed} | ${suite.skipped} | ${formatDuration(suite.duration)} |`,
      )
    }
  }

  const failures = report.suites.flatMap((suite) => suite.failures)
  if (failures.length > 0) {
    lines.push('', '### Failed tests', '')
    for (const failure of failures) {
      const location = failure.file
        ? ` (${codeSpan(failure.line ? `${failure.file}:${failure.line}` : failure.file)})`
        : ''
      lines.push(`- **${failure.suite}**: ${codeSpan(failure.name)}${location}`, '')
      lines.push(codeBlock(failure.message || '(no message)', '  '), '')
    }
  }
  return `${lines.join('\n').trimEnd()}\n`
}

const escapeData = (text) => text.replace(/%/g, '%25').replace(/\r/g, '%0D').replace(/\n/g, '%0A')
const escapeProperty = (text) => escapeData(text).replace(/:/g, '%3A').replace(/,/g, '%2C')

/** GitHub Actions workflow commands: one ::error per failed test or missing results file. */
export function formatAnnotations(report) {
  const lines = []
  for (const suite of report.suites) {
    if (suite.missing) {
      lines.push(`::error title=${escapeProperty(suite.name)}::${escapeData(`${suite.name} wrote no test results.`)}`)
    }
    for (const failure of suite.failures) {
      const properties = []
      if (failure.file) properties.push(`file=${escapeProperty(failure.file)}`)
      if (failure.file && failure.line) properties.push(`line=${failure.line}`)
      properties.push(`title=${escapeProperty(failure.name)}`)
      lines.push(`::error ${properties.join(',')}::${escapeData(failure.message || 'Test failed.')}`)
    }
  }
  return lines
}

/** Runs the report with command-line arguments and returns the exit code. */
export function main(args, env = process.env, stdout = process.stdout) {
  let resultsDir
  let root = scriptRoot
  const suites = []
  const skipped = []
  for (let i = 0; i < args.length; i++) {
    if (args[i] === '--results-dir') resultsDir = args[++i]
    else if (args[i] === '--root') root = args[++i]
    else if (args[i] === '--skipped') skipped.push(args[++i])
    else suites.push(args[i])
  }
  root = path.resolve(root)
  resultsDir = path.resolve(resultsDir ?? path.join(root, 'artifacts/test-results'))
  const report = buildReport({ suites, resultsDir, root, skipped })

  const summary = formatSummary(report)
  if (env.GITHUB_STEP_SUMMARY) appendFileSync(env.GITHUB_STEP_SUMMARY, `${summary}\n`)
  else stdout.write(summary)
  if (env.GITHUB_ACTIONS === 'true') {
    for (const line of formatAnnotations(report)) stdout.write(`${line}\n`)
  }
  return report.ok ? 0 : 1
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2))
}
