// Refreshes the shard durations from the JUnit files of a CI run on main, and opens or updates one
// `process` issue when they drift or a split passes the budget.
//
// Usage: node scripts/shard-durations.mjs <results dir> <run id>
//
// The results folder holds the run's test-results-* artifacts, one sub-folder each, as
// actions/download-artifact writes them. Each split's own --durations command rewrites its durations file
// from the JUnit files found there, and its --count command then prints its `Over budget:` lines. A file
// drifts when its entries that moved by at least `minimumMove` seconds add up to more than `driftShare` of
// its old total. The script commits nothing: the issue holds the new files, and NexKit's round commits them.
// CI's durations job runs it with only `contents: read` and `issues: write`, and no model, see
// docs/development.md#shards. GH names the gh command, so the tests can replace it.
import { spawnSync } from 'node:child_process'
import { readFileSync, readdirSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { main as e2e } from './lib/e2e-shard.mjs'
import { main as integration } from './lib/integration-shard.mjs'
import { main as smoke } from './lib/smoke-shard.mjs'
import { main as test } from './lib/test-shard.mjs'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repository = 'https://github.com/phuongnse/axis'

/** The share of a file's old total that its moved entries must pass for the file to drift. */
export const driftShare = 0.05

/** The fewest seconds an entry must move to count, so run-to-run noise on short tests is left out. */
export const minimumMove = 10

/** The marker that ends the issue's body, so a later run finds the open issue. */
export const marker = '<!-- shard-durations -->'

/** The title of the issue. */
export const title = 'The shard durations match the runs on main'

/** The most characters GitHub keeps in an issue's body, less a margin. */
export const maxBody = 65_000

/** Each split: its durations file, its main, and the JUnit file names its --durations command reads. */
export const splits = [
  { kind: 'test', file: 'web/vitest-durations.json', main: test, junit: /^web(-\d+)?\.xml$/ },
  { kind: 'e2e', file: 'tests/e2e/spec-durations.json', main: e2e, junit: /^playwright(-\d+)?\.xml$/ },
  {
    kind: 'integration',
    file: 'tests/Axis.Integration.Tests/class-durations.json',
    main: integration,
    junit: /^Axis(\.ControlPlane)?\.Integration\.Tests\.xml$/,
  },
  { kind: 'smoke', file: 'scripts/smoke-durations.json', main: smoke, junit: /^(dev-script|compose|images|onprem)\.xml$/ },
]

/** The share of the old total that the entries moved by at least `minimumMove` add up to. An added or removed entry counts in full. */
export function drift(before, after) {
  const total = Object.values(before).reduce((sum, seconds) => sum + seconds, 0)
  let moved = 0
  for (const name of new Set([...Object.keys(before), ...Object.keys(after)])) {
    const move = Math.abs((after[name] ?? 0) - (before[name] ?? 0))
    if (move >= minimumMove) moved += move
  }
  if (total === 0) return moved > 0 ? 1 : 0
  return moved / total
}

/** The name an `Over budget:` line gives, such as a spec path or a class name. */
const overBudgetName = (line) => /^Over budget: (.*?) takes about /.exec(line)?.[1] ?? line

const percent = (share) => `${Math.round(share * 1000) / 10}%`
const json = (value) => ['```json', JSON.stringify(value, null, 2), '```']

/** The entries of `after` that differ from `before`, and the names `after` drops. */
function changes(before, after) {
  const changed = Object.fromEntries(Object.entries(after).filter(([name, seconds]) => before[name] !== seconds))
  const removed = Object.keys(before).filter((name) => !(name in after))
  return { changed, removed }
}

function body(firstLine, drifted, overBudget, runId, full) {
  const run = `[${runId}](${repository}/actions/runs/${runId})`
  const sections = drifted.flatMap((file) => {
    const heading = `### \`${file.path}\`, ${percent(file.share)} moved`
    if (full) return ['', heading, '', ...json(file.after)]
    const { changed, removed } = changes(file.before, file.after)
    return [
      '',
      heading,
      '',
      'The file is too long for this issue in full. Set these entries:',
      '',
      ...json(changed),
      ...(removed.length ? ['', `Remove these entries: ${removed.map((name) => `\`${name}\``).join(', ')}.`] : []),
    ]
  })
  return [
    firstLine,
    `CI run ${run} on \`main\` measured the shards, as [the shards section](${repository}/blob/main/docs/development.md#shards) ` +
      'says. The durations files below no longer match that run, or a spec, class, file or suite alone passes the ' +
      'time budget, so the splits weigh the shards wrong.',
    '',
    'Concepts: none from concepts.md; this is how Axis is built.',
    '',
    '## Acceptance criteria',
    '',
    ...drifted.map((file) =>
      full
        ? `- [ ] \`${file.path}\` holds the durations below.`
        : `- [ ] \`${file.path}\` holds the durations below, with the other entries as they are.`,
    ),
    ...overBudget.map((line) => `- [ ] Split ${overBudgetName(line)} so no single one passes the budget: ${line}`),
    '- [ ] `scripts/test.sh` and `scripts/lint.sh` pass.',
    ...(drifted.length ? ['', '## New durations', ...sections] : []),
    '',
    marker,
  ].join('\n')
}

/**
 * What to do with the issue. `files` is [{ path, before, after }], `overBudget` the `Over budget:` lines and
 * `openIssues` [{ number, body }]. Returns { action: 'none' }, { action: 'create', title, labels, body } or
 * { action: 'edit', number, body }. An edit keeps the open issue's first line, whose marker names the run
 * that opened it, since NexKit trusts the issue only for that run.
 */
export function issueFrom(files, overBudget, openIssues, runId) {
  const drifted = files
    .map((file) => ({ ...file, share: drift(file.before, file.after) }))
    .filter((file) => file.share > driftShare)
  if (drifted.length === 0 && overBudget.length === 0) return { action: 'none' }
  const open = openIssues.find((issue) => (issue.body ?? '').includes(marker))
  const firstLine = open ? open.body.split('\n')[0] : `<!-- nexkit:workflow ci.yml ${runId} -->`
  let text = body(firstLine, drifted, overBudget, runId, true)
  if (text.length > maxBody) text = body(firstLine, drifted, overBudget, runId, false)
  if (open) return { action: 'edit', number: open.number, body: text }
  return { action: 'create', title, labels: ['process'], body: text }
}

/** The JUnit files under `dir`, at any depth, by split: { test, e2e, integration, smoke }, each sorted. */
export function junitFiles(dir) {
  const found = Object.fromEntries(splits.map((split) => [split.kind, []]))
  for (const entry of readdirSync(dir, { recursive: true, withFileTypes: true })) {
    if (!entry.isFile()) continue
    const split = splits.find((s) => s.junit.test(entry.name))
    if (split) found[split.kind].push(path.join(entry.parentPath, entry.name))
  }
  for (const list of Object.values(found)) list.sort()
  return found
}

const readJson = (file) => JSON.parse(readFileSync(path.join(root, file), 'utf8'))

/** Rewrites each split's durations file from the JUnit files under `resultsDir`, and collects the `Over budget:` lines of every split. */
export function refresh(resultsDir) {
  const junit = junitFiles(resultsDir)
  const files = []
  for (const split of splits) {
    if (junit[split.kind].length === 0) continue
    const before = readJson(split.file)
    console.log(split.main(['--durations', ...junit[split.kind]]))
    files.push({ path: split.file, before, after: readJson(split.file) })
  }
  const overBudget = []
  for (const split of splits) split.main(['--count'], undefined, (line) => overBudget.push(line))
  return { files, overBudget }
}

function run(command, args, input) {
  const result = spawnSync(command, args, { encoding: 'utf8', input, maxBuffer: 64 * 1024 * 1024 })
  if (result.error) throw new Error(`${command}: ${result.error.message}`)
  if (result.status !== 0) throw new Error(`${command} ${args.slice(0, 2).join(' ')} failed:\n${result.stdout}${result.stderr}`.trimEnd())
  return result.stdout
}

function cli([resultsDir, runId]) {
  if (!resultsDir || !runId) {
    console.error('Usage: node scripts/shard-durations.mjs <results dir> <run id>')
    return 2
  }
  const { files, overBudget } = refresh(resultsDir)
  for (const line of overBudget) console.log(line)
  const gh = process.env.GH ?? 'gh'
  const open = JSON.parse(
    run(gh, ['issue', 'list', '--state', 'open', '--label', 'process', '--limit', '200', '--json', 'number,body']),
  )
  const issue = issueFrom(files, overBudget, open, runId)
  if (issue.action === 'none') {
    console.log('The shard durations did not drift.')
  } else if (issue.action === 'edit') {
    run(gh, ['issue', 'edit', String(issue.number), '--body-file', '-'], issue.body)
    console.log(`Updated issue #${issue.number}.`)
  } else {
    const args = ['issue', 'create', '--title', issue.title, '--body-file', '-']
    for (const label of issue.labels) args.push('--label', label)
    process.stdout.write(run(gh, args, issue.body))
  }
  return 0
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    process.exitCode = cli(process.argv.slice(2))
  } catch (error) {
    console.error(error.message)
    process.exitCode = 1
  }
}
