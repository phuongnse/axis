// Checks that web/ uses the platform's shared widgets and does not draw its own (AGENTS.md: one concern has one mechanism).
//
// Usage: node scripts/check-shared-widgets.mjs [--root DIR] [--write-baseline]
//
// It refuses three ways round a shared widget, read from the source as text:
//   button  An Ant Design `<Button` outside AxisButton.tsx. Use `AxisButton` with a role.
//   table   An import of `Table` from `antd` outside AxisTable.tsx. Use `AxisTable`. The baseline cannot allow it.
//   tag     A `<Tag` whose colour is an Ant Design preset: a colour name such as `blue` or `blue-inverse`, or a status
//           preset such as `success`. It counts as a string or inside a `color={…}` expression, even on a later line.
//           Use the colour tokens.
// The code that broke a rule before the check existed is counted in scripts/shared-widgets-baseline.json, by file and
// rule, for the rules that have a baseline. A file may not use such a rule more often than its baseline, and a file
// that uses one less must lower its baseline, so the count only goes down. A strict rule has no baseline: any use is a
// problem. `--write-baseline` writes the baseline from the code as it is now. Prints one line per problem on stderr
// and exits 1 when there is any.
import { readdirSync, readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const scriptRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

const skippedDirectories = new Set(['node_modules', 'dist', 'coverage'])
const extensions = ['.tsx', '.jsx']

const presetColours =
  'magenta|red|volcano|orange|gold|lime|green|cyan|blue|geekblue|purple|pink|yellow'
const statusPresets = 'success|processing|error|warning|default'
const preset = `(?:(?:${presetColours})(?:-inverse)?|${statusPresets})`

// button does not match across a line break, so a tag whose name ends its line is not counted yet.
/**
 * Each rule: its name, the pattern, the files that are the shared widget itself, what to say and, for a strict rule,
 * that the baseline cannot allow a use.
 */
export const rules = [
  {
    name: 'button',
    pattern: /<Button(?=[^\S\n]|[>/])/g,
    owner: 'web/src/platform/AxisButton.tsx',
    advice: 'Use AxisButton with a role.',
  },
  {
    name: 'table',
    // The `Table` specifier of an import from antd, also when the import spans lines. The line is the specifier's.
    pattern: /(?<=\bimport\s*\{[^}]*)\bTable\b(?=[^}]*\}\s*from\s*['"]antd['"])/g,
    owner: 'web/src/platform/AxisTable.tsx',
    strict: true,
    advice: 'Import Table from antd only in AxisTable.tsx. Use AxisTable.',
  },
  {
    name: 'tag',
    pattern: new RegExp(`<Tag\\b[^>]*?\\bcolor=(?:["']${preset}["']|\\{[^{}]*["']${preset}["'][^{}]*\\})`, 'g'),
    owner: null,
    advice: 'Colour a tag with the colour tokens, not a preset name.',
  },
]

export const baselineFile = 'scripts/shared-widgets-baseline.json'

/** Returns the uses in the source as { line, rule }, for the rules that apply to `file`, in line order. */
export function findUses(source, file) {
  const found = []
  rules.forEach((rule, order) => {
    if (rule.owner === file) return
    for (const match of source.matchAll(rule.pattern)) {
      found.push({ line: source.slice(0, match.index).split('\n').length, rule: rule.name, order })
    }
  })
  return found
    .sort((a, b) => a.line - b.line || a.order - b.order)
    .map(({ line, rule }) => ({ line, rule }))
}

function findSourceFiles(directory, files = []) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name)
    if (entry.isDirectory()) {
      if (!skippedDirectories.has(entry.name)) findSourceFiles(fullPath, files)
    } else if (entry.isFile() && extensions.includes(path.extname(entry.name)) && !/\.test\.tsx$/.test(entry.name)) {
      files.push(fullPath)
    }
  }
  return files
}

/** The count of each rule in each file under root/web, as { file: { rule: count } }, without the tests. */
export function countUses(root) {
  const counts = {}
  for (const file of findSourceFiles(path.join(root, 'web/src')).sort()) {
    const relativeFile = path.relative(root, file).split(path.sep).join('/')
    for (const { rule } of findUses(readFileSync(file, 'utf8'), relativeFile)) {
      counts[relativeFile] ??= {}
      counts[relativeFile][rule] = (counts[relativeFile][rule] ?? 0) + 1
    }
  }
  return counts
}

/** Compares the counts with the baseline and returns the problems as { file, rule, message }. */
export function compare(counts, baseline) {
  const problems = []
  const advice = Object.fromEntries(rules.map((rule) => [rule.name, rule.advice]))
  const strict = new Set(rules.filter((rule) => rule.strict).map((rule) => rule.name))
  const files = new Set([...Object.keys(counts), ...Object.keys(baseline)])
  for (const file of [...files].sort()) {
    for (const rule of rules.map((entry) => entry.name)) {
      const now = counts[file]?.[rule] ?? 0
      const allowed = strict.has(rule) ? 0 : (baseline[file]?.[rule] ?? 0)
      if (now > allowed) {
        problems.push({ file, rule, message: `${now} use(s) of the ${rule} rule, ${allowed} allowed. ${advice[rule]}` })
      } else if (now < allowed) {
        problems.push({
          file,
          rule,
          message: `${now} use(s) of the ${rule} rule, ${allowed} in the baseline. Lower it in ${baselineFile}.`,
        })
      }
    }
  }
  return problems
}

function main(args) {
  const rootIndex = args.indexOf('--root')
  const root = rootIndex >= 0 ? path.resolve(args[rootIndex + 1]) : scriptRoot
  const counts = countUses(root)
  if (args.includes('--write-baseline')) {
    const strict = new Set(rules.filter((rule) => rule.strict).map((rule) => rule.name))
    const allowed = {}
    for (const [file, fileCounts] of Object.entries(counts)) {
      const entries = Object.entries(fileCounts).filter(([rule]) => !strict.has(rule))
      if (entries.length > 0) allowed[file] = Object.fromEntries(entries)
    }
    writeFileSync(path.join(root, baselineFile), `${JSON.stringify(allowed, null, 2)}\n`)
    console.log(`Wrote ${baselineFile}.`)
    return 0
  }
  let baseline = {}
  try {
    baseline = JSON.parse(readFileSync(path.join(root, baselineFile), 'utf8'))
  } catch {
    // No baseline: every use is a problem.
  }
  const problems = compare(counts, baseline)
  for (const { file, message } of problems) process.stderr.write(`${file}: ${message}\n`)
  if (problems.length > 0) return 1
  console.log(`Checked ${Object.keys(counts).length} files with a baseline use.`)
  return 0
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2))
}
