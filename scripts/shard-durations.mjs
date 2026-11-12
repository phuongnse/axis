// Refreshes the shard durations from the JUnit files of the latest CI runs on main. It opens or updates one
// `process` issue with the durations files whose split runs a shard past the budget, and opens one `process` issue
// for each spec, class, file or suite that alone passes the budget.
//
// Usage: node scripts/shard-durations.mjs <results dir> <run id>
//
// The results folder holds the run's test-results-* artifacts, one sub-folder each, as
// actions/download-artifact writes them. The script downloads the same artifacts of the latest successful CI
// runs dispatched on main before it, so that `runsMeasured` runs count in all and no run of a pull request whose
// branch is also called main does. For each split, its own --durations command measures each run from the
// durations file as it was, and each entry becomes the median of its measures, so one slow or fast run moves no
// entry. Its --count command then gives its `Over budget:` lines. A file goes into the issue when the split that
// its current file gives, weighed with the new durations, runs a shard past the budget, and the new file's split
// has a shorter longest shard. The script commits nothing: the issue holds the new files, and NexKit's round
// commits them. CI's durations job runs it with only `contents: read`, `actions: read` and `issues: write`, and no
// model, see docs/development.md#shards.
// GH names the gh command, so the tests can replace it.
import { spawnSync } from 'node:child_process'
import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { longest as longestE2e, main as e2e } from './lib/e2e-shard.mjs'
import { longest as longestIntegration, main as integration } from './lib/integration-shard.mjs'
import { longest as longestSmoke, main as smoke } from './lib/smoke-shard.mjs'
import { budgetSeconds, median } from './lib/shards.mjs'
import { longest as longestTest, main as test } from './lib/test-shard.mjs'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repository = 'https://github.com/phuongnse/axis'

/** The CI runs on main whose median makes the durations: the run itself and the latest successful ones before it. */
export const runsMeasured = 5

/** The most successful CI runs dispatched on main that the script looks through for earlier results, since one may have none. */
export const runsSearched = 20

/** The marker that ends the durations issue's body, so a later run finds it while it is open. */
export const marker = '<!-- shard-durations -->'

/** The title of the durations issue. */
export const title = 'The shard durations match the runs on main'

/** The most items GitHub's issues API returns per page. */
export const perPage = 100

/** The most characters GitHub keeps in an issue's body, less a margin. */
export const maxBody = 65_000

/**
 * Each split: its durations file, the folder or file its main takes, its main, its longest shard for `weighed`, and
 * the JUnit file names its --durations command reads. Paths are relative to the repository root.
 */
export const splits = [
  {
    kind: 'test',
    file: 'web/vitest-durations.json',
    at: 'web',
    main: test,
    longest: longestTest,
    junit: /^web(-\d+)?\.xml$/,
  },
  {
    kind: 'e2e',
    file: 'tests/e2e/spec-durations.json',
    at: 'tests/e2e',
    main: e2e,
    longest: longestE2e,
    junit: /^playwright(-\d+)?\.xml$/,
  },
  {
    kind: 'integration',
    file: 'tests/Axis.Integration.Tests/class-durations.json',
    at: 'tests/Axis.Integration.Tests',
    main: integration,
    longest: longestIntegration,
    junit: /^Axis(\.ControlPlane)?\.Integration\.Tests\.xml$/,
  },
  {
    kind: 'smoke',
    file: 'scripts/smoke-durations.json',
    at: 'scripts/smoke-durations.json',
    main: smoke,
    longest: longestSmoke,
    junit: /^(dev-script|compose|images|onprem)\.xml$/,
  },
]

/**
 * A file with its two longest shards, estimated as the split's --count estimates them. `held` is the longest shard
 * of the split that the `before` file gives at its own count, weighed with the `after` durations. `fresh` is the
 * longest shard of the split that the `after` file gives.
 */
export function weighed(split, root, before, after) {
  const at = path.join(root, split.at)
  return {
    path: split.file,
    before,
    after,
    held: split.longest(before, after, at),
    fresh: split.longest(after, after, at),
  }
}

/** The name an `Over budget:` line gives, such as a spec path or a class name. */
export const overBudgetName = (line) => /^Over budget: (.*?) takes about /.exec(line)?.[1] ?? line

// A run of hyphens is shortened so that a name cannot close the HTML comment of its marker.
const overBudgetMarker = (kind, name) => `<!-- shard-over-budget ${kind} ${name.replace(/-{2,}/g, '-')} -->`

const json = (value) => ['```json', JSON.stringify(value, null, 2), '```']
const runLink = (runId) => `[${runId}](${repository}/actions/runs/${runId})`
const measuredBy = (runIds) =>
  runIds.length === 1 ? `CI run ${runLink(runIds[0])}` : `CI runs ${runIds.map(runLink).join(', ')}`
const shards = `[the shards section](${repository}/blob/main/docs/development.md#shards)`
const overBudgetRule = `[the rule for a test over the shard budget](${repository}/blob/main/docs/testing.md#rules-for-tests)`

/** The entries of `after` that differ from `before`, and the names `after` drops. */
function changes(before, after) {
  const changed = Object.fromEntries(Object.entries(after).filter(([name, seconds]) => before[name] !== seconds))
  const removed = Object.keys(before).filter((name) => !(name in after))
  return { changed, removed }
}

function durationsBody(firstLine, stale, runIds, full) {
  const sections = stale.flatMap((file) => {
    const seconds = `a shard of about ${Math.round(file.held)} s, past the budget of ${budgetSeconds} s`
    const heading = `### \`${file.path}\`, ${seconds}`
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
    `${measuredBy(runIds)} on \`main\` measured the shards, as ${shards} says, and each entry below is the median ` +
      "of its measures. Weighed with these durations, a shard of the current file's split runs past the budget, " +
      "and the new file's split has a shorter longest shard.",
    '',
    'Concepts: none from concepts.md; this is how Axis is built.',
    '',
    '## Acceptance criteria',
    '',
    ...stale.map((file) =>
      full
        ? `- [ ] \`${file.path}\` holds the durations below.`
        : `- [ ] \`${file.path}\` holds the durations below, with the other entries as they are.`,
    ),
    '- [ ] `scripts/test.sh` and `scripts/lint.sh` pass.',
    '',
    '## New durations',
    ...sections,
    '',
    marker,
  ].join('\n')
}

function overBudgetIssue({ kind, line }, runId, runIds) {
  const name = overBudgetName(line)
  return {
    action: 'create',
    title: `${name} fits within the shard budget`,
    labels: ['process'],
    body: [
      `<!-- nexkit:workflow ci.yml ${runId} -->`,
      `${measuredBy(runIds)} on \`main\` measured the shards, as ${shards} says, and \`${name}\` alone takes ` +
        'longer than one shard may. No number of shards makes it fit, so its shard is the slowest.',
      '',
      `> ${line}`,
      '',
      'Concepts: none from concepts.md; this is how Axis is built.',
      '',
      '## Acceptance criteria',
      '',
      `- [ ] \`${name}\` is made faster, or split, so that \`node scripts/lib/${kind}-shard.mjs --count\` prints no ` +
        '`Over budget:` line for it or for any part of it.',
      `- [ ] The change follows ${overBudgetRule}, taking its steps in order.`,
      '- [ ] Every test it held still runs, in the check that ran it, and passes.',
      '- [ ] `scripts/test.sh` and `scripts/lint.sh` pass.',
      '',
      overBudgetMarker(kind, name),
    ].join('\n'),
  }
}

/**
 * The issues to open or update. `files` is [{ path, before, after, held, fresh }], as `weighed` gives them,
 * `overBudget` [{ kind, line }] with the split's kind and its `Over budget:` line, `openIssues` [{ number, body }],
 * `runId` the run that opens an issue and `runIds` the runs measured, that one first. Returns a list of
 * { action: 'create', title, labels, body } and { action: 'edit', number, body }.
 *
 * A file goes into the durations issue when its `held` shard passes the budget and is longer than its `fresh`
 * shard. Those files make one issue, or update the open one. An edit keeps the open issue's first line, whose
 * marker names the run that opened it, since NexKit trusts the issue only for that run. Each `Over budget:` line
 * makes an issue of its own, unless one for the same name is open.
 */
export function issuesFrom(files, overBudget, openIssues, runId, runIds = [runId]) {
  const bodies = openIssues.map((issue) => issue.body ?? '')
  const issues = []
  const stale = files.filter((file) => file.held > budgetSeconds && file.held > file.fresh)
  if (stale.length) {
    const open = openIssues.find((issue) => (issue.body ?? '').includes(marker))
    const firstLine = open ? open.body.split('\n')[0] : `<!-- nexkit:workflow ci.yml ${runId} -->`
    let body = durationsBody(firstLine, stale, runIds, true)
    if (body.length > maxBody) body = durationsBody(firstLine, stale, runIds, false)
    issues.push(open ? { action: 'edit', number: open.number, body } : { action: 'create', title, labels: ['process'], body })
  }
  const seen = new Set()
  for (const entry of overBudget) {
    const key = overBudgetMarker(entry.kind, overBudgetName(entry.line))
    if (seen.has(key) || bodies.some((body) => body.includes(key))) continue
    seen.add(key)
    issues.push(overBudgetIssue(entry, runId, runIds))
  }
  return issues
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

/**
 * Each entry's median over the measures of one durations file, rounded as the measures are: whole seconds, or two
 * decimals for a ratio such as the parallelism. The entries are those of the first measure, the latest run's.
 */
export function medianDurations(measures) {
  return Object.fromEntries(
    Object.keys(measures[0]).map((name) => {
      const values = measures.map((measure) => measure[name]).filter((seconds) => seconds !== undefined)
      const middle = median(values)
      return [name, values.every(Number.isInteger) ? Math.round(middle) : Math.round(middle * 100) / 100]
    }),
  )
}

/**
 * Rewrites each split's durations file under `root` from the JUnit files under `resultsDirs`, one folder per run,
 * the latest first, and collects the `Over budget:` lines of every split. A split that the latest run has no
 * results for keeps its file. Each run is measured from the file as it was, and each entry becomes the median of
 * its measures. Returns { files, overBudget }, with each file as `weighed` gives it.
 */
export function refresh(resultsDirs, root = repositoryRoot, log = (line) => console.log(line)) {
  const runs = [resultsDirs].flat().map(junitFiles)
  const files = []
  for (const split of splits) {
    if (runs[0][split.kind].length === 0) continue
    const file = path.join(root, split.file)
    const text = readFileSync(file, 'utf8')
    const measures = []
    for (const junit of runs.map((found) => found[split.kind]).filter((list) => list.length > 0)) {
      writeFileSync(file, text)
      log(split.main(['--durations', ...junit], path.join(root, split.at)))
      measures.push(JSON.parse(readFileSync(file, 'utf8')))
    }
    const after = medianDurations(measures)
    writeFileSync(file, `${JSON.stringify(after, null, 2)}\n`)
    files.push(weighed(split, root, JSON.parse(text), after))
  }
  const overBudget = []
  for (const split of splits) {
    split.main(['--count'], path.join(root, split.at), (line) => overBudget.push({ kind: split.kind, line }))
  }
  return { files, overBudget }
}

function run(command, args, input) {
  const result = spawnSync(command, args, { encoding: 'utf8', input, maxBuffer: 64 * 1024 * 1024 })
  if (result.error) throw new Error(`${command}: ${result.error.message}`)
  if (result.status !== 0) {
    throw new Error(`${command} ${args.slice(0, 2).join(' ')} failed:\n${result.stdout}${result.stderr}`.trimEnd())
  }
  return result.stdout
}

/** The open `process` issues, pull requests left out, read page by page until a page comes back short. */
export function openProcessIssues(gh) {
  const repo = new URL(repository).pathname.slice(1)
  const open = []
  for (let page = 1; ; page++) {
    const entries = JSON.parse(
      run(gh, ['api', `repos/${repo}/issues?state=open&labels=process&per_page=${perPage}&page=${page}`]),
    )
    open.push(...entries.filter((issue) => !issue.pull_request).map(({ number, body }) => ({ number, body })))
    if (entries.length < perPage) return open
  }
}

/**
 * Downloads into folders under `dir` the test-results-* artifacts of the latest successful CI runs dispatched on main
 * other than `runId`, at most `count` of them, and returns those folders, the latest first. Only dispatched runs
 * count, so a run of a pull request whose branch is also called main does not. A run that has no such artifacts,
 * such as one whose artifacts expired, is skipped.
 */
export function earlierResults(gh, runId, dir, count, log = (line) => console.log(line)) {
  const repo = new URL(repository).pathname.slice(1)
  const list = ['run', 'list', '--repo', repo, '--workflow', 'ci.yml', '--branch', 'main', '--event', 'workflow_dispatch', '--status', 'success']
  const ids = run(gh, [...list, '--limit', String(runsSearched), '--json', 'databaseId', '--jq', '.[].databaseId'])
    .split('\n')
    .map((id) => id.trim())
    .filter((id) => id && id !== String(runId))
  const folders = []
  for (const id of ids) {
    if (folders.length >= count) break
    const folder = path.join(dir, id)
    const download = ['run', 'download', id, '--repo', repo, '--pattern', 'test-results-*', '--dir', folder]
    const result = spawnSync(gh, download, { encoding: 'utf8' })
    if (result.status === 0) folders.push(folder)
    else log(`Run ${id} has no test results to download, so it is left out.`)
  }
  return folders
}

/** Runs the command line: refreshes the durations under `root` and opens or updates the issues with gh. */
export function cli([resultsDir, runId], root = repositoryRoot, log = (line) => console.log(line)) {
  if (!resultsDir || !runId) {
    console.error('Usage: node scripts/shard-durations.mjs <results dir> <run id>')
    return 2
  }
  const gh = process.env.GH ?? 'gh'
  const scratch = mkdtempSync(path.join(tmpdir(), 'shard-durations-'))
  let issues
  try {
    const earlier = earlierResults(gh, runId, scratch, runsMeasured - 1, log)
    const { files, overBudget } = refresh([resultsDir, ...earlier], root, log)
    for (const { line } of overBudget) log(line)
    const runIds = [String(runId), ...earlier.map((folder) => path.basename(folder))]
    issues = issuesFrom(files, overBudget, openProcessIssues(gh), runId, runIds)
  } finally {
    rmSync(scratch, { recursive: true, force: true })
  }
  if (issues.length === 0) log('No durations file needs a new split, and nothing passes the budget.')
  for (const issue of issues) {
    if (issue.action === 'edit') {
      run(gh, ['issue', 'edit', String(issue.number), '--body-file', '-'], issue.body)
      log(`Updated issue #${issue.number}.`)
    } else {
      const args = ['issue', 'create', '--title', issue.title, '--body-file', '-']
      for (const label of issue.labels) args.push('--label', label)
      log(run(gh, args, issue.body).trim())
    }
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
