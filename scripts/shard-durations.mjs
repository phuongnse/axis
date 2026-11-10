// Refreshes the shard durations from the JUnit files of a CI run on main. It opens or updates one `process`
// issue when they drift, and opens one `process` issue for each spec, class, file or suite that alone passes
// the budget.
//
// Usage: node scripts/shard-durations.mjs <results dir> <run id>
//
// The results folder holds the run's test-results-* artifacts, one sub-folder each, as
// actions/download-artifact writes them. Each split's own --durations command rewrites its durations file
// from the JUnit files found there, and its --count command then gives its `Over budget:` lines. A file
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

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repository = 'https://github.com/phuongnse/axis'

/** The share of a file's old total that its moved entries must pass for the file to drift. */
export const driftShare = 0.05

/** The fewest seconds an entry must move to count, so run-to-run noise on short tests is left out. */
export const minimumMove = 10

/** The marker that ends the durations issue's body, so a later run finds it while it is open. */
export const marker = '<!-- shard-durations -->'

/** The title of the durations issue. */
export const title = 'The shard durations match the runs on main'

/** The most items GitHub's issues API returns per page. */
export const perPage = 100

/** The most characters GitHub keeps in an issue's body, less a margin. */
export const maxBody = 65_000

/**
 * Each split: its durations file, the folder or file its main takes, its main, and the JUnit file names its
 * --durations command reads. Paths are relative to the repository root.
 */
export const splits = [
  { kind: 'test', file: 'web/vitest-durations.json', at: 'web', main: test, junit: /^web(-\d+)?\.xml$/ },
  {
    kind: 'e2e',
    file: 'tests/e2e/spec-durations.json',
    at: 'tests/e2e',
    main: e2e,
    junit: /^playwright(-\d+)?\.xml$/,
  },
  {
    kind: 'integration',
    file: 'tests/Axis.Integration.Tests/class-durations.json',
    at: 'tests/Axis.Integration.Tests',
    main: integration,
    junit: /^Axis(\.ControlPlane)?\.Integration\.Tests\.xml$/,
  },
  {
    kind: 'smoke',
    file: 'scripts/smoke-durations.json',
    at: 'scripts/smoke-durations.json',
    main: smoke,
    junit: /^(dev-script|compose|images|onprem)\.xml$/,
  },
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
export const overBudgetName = (line) => /^Over budget: (.*?) takes about /.exec(line)?.[1] ?? line

// A run of hyphens is shortened so that a name cannot close the HTML comment of its marker.
const overBudgetMarker = (kind, name) => `<!-- shard-over-budget ${kind} ${name.replace(/-{2,}/g, '-')} -->`

const percent = (share) => `${Math.round(share * 1000) / 10}%`
const json = (value) => ['```json', JSON.stringify(value, null, 2), '```']
const runLink = (runId) => `[${runId}](${repository}/actions/runs/${runId})`
const shards = `[the shards section](${repository}/blob/main/docs/development.md#shards)`

/** The entries of `after` that differ from `before`, and the names `after` drops. */
function changes(before, after) {
  const changed = Object.fromEntries(Object.entries(after).filter(([name, seconds]) => before[name] !== seconds))
  const removed = Object.keys(before).filter((name) => !(name in after))
  return { changed, removed }
}

function durationsBody(firstLine, drifted, runId, full) {
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
    `CI run ${runLink(runId)} on \`main\` measured the shards, as ${shards} says. The durations files below no ` +
      'longer match that run, so the splits weigh the shards wrong.',
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
    '- [ ] `scripts/test.sh` and `scripts/lint.sh` pass.',
    '',
    '## New durations',
    ...sections,
    '',
    marker,
  ].join('\n')
}

function overBudgetIssue({ kind, line }, runId) {
  const name = overBudgetName(line)
  return {
    action: 'create',
    title: `${name} fits within the shard budget`,
    labels: ['process'],
    body: [
      `<!-- nexkit:workflow ci.yml ${runId} -->`,
      `CI run ${runLink(runId)} on \`main\` measured the shards, as ${shards} says, and \`${name}\` alone takes ` +
        'longer than one shard may. No number of shards makes it fit, so its shard is the slowest.',
      '',
      `> ${line}`,
      '',
      'Concepts: none from concepts.md; this is how Axis is built.',
      '',
      '## Acceptance criteria',
      '',
      `- [ ] \`${name}\` is split, or made faster, so that \`node scripts/lib/${kind}-shard.mjs --count\` prints no ` +
        '`Over budget:` line for it or for any part of it.',
      '- [ ] Every test it held still runs, in the check that ran it, and passes.',
      '- [ ] `scripts/test.sh` and `scripts/lint.sh` pass.',
      '',
      overBudgetMarker(kind, name),
    ].join('\n'),
  }
}

/**
 * The issues to open or update. `files` is [{ path, before, after }], `overBudget` [{ kind, line }] with the
 * split's kind and its `Over budget:` line, and `openIssues` [{ number, body }]. Returns a list of
 * { action: 'create', title, labels, body } and { action: 'edit', number, body }.
 *
 * The drifted files make one issue, or update the open one. An edit keeps the open issue's first line, whose
 * marker names the run that opened it, since NexKit trusts the issue only for that run. Each `Over budget:`
 * line makes an issue of its own, unless one for the same name is open.
 */
export function issuesFrom(files, overBudget, openIssues, runId) {
  const bodies = openIssues.map((issue) => issue.body ?? '')
  const issues = []
  const drifted = files
    .map((file) => ({ ...file, share: drift(file.before, file.after) }))
    .filter((file) => file.share > driftShare)
  if (drifted.length) {
    const open = openIssues.find((issue) => (issue.body ?? '').includes(marker))
    const firstLine = open ? open.body.split('\n')[0] : `<!-- nexkit:workflow ci.yml ${runId} -->`
    let body = durationsBody(firstLine, drifted, runId, true)
    if (body.length > maxBody) body = durationsBody(firstLine, drifted, runId, false)
    issues.push(open ? { action: 'edit', number: open.number, body } : { action: 'create', title, labels: ['process'], body })
  }
  const seen = new Set()
  for (const entry of overBudget) {
    const key = overBudgetMarker(entry.kind, overBudgetName(entry.line))
    if (seen.has(key) || bodies.some((body) => body.includes(key))) continue
    seen.add(key)
    issues.push(overBudgetIssue(entry, runId))
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
 * Rewrites each split's durations file under `root` from the JUnit files under `resultsDir`, and collects
 * the `Over budget:` lines of every split. Returns { files, overBudget }.
 */
export function refresh(resultsDir, root = repositoryRoot, log = (line) => console.log(line)) {
  const junit = junitFiles(resultsDir)
  const readJson = (file) => JSON.parse(readFileSync(path.join(root, file), 'utf8'))
  const files = []
  for (const split of splits) {
    if (junit[split.kind].length === 0) continue
    const before = readJson(split.file)
    log(split.main(['--durations', ...junit[split.kind]], path.join(root, split.at)))
    files.push({ path: split.file, before, after: readJson(split.file) })
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

/** Runs the command line: refreshes the durations under `root` and opens or updates the issues with gh. */
export function cli([resultsDir, runId], root = repositoryRoot, log = (line) => console.log(line)) {
  if (!resultsDir || !runId) {
    console.error('Usage: node scripts/shard-durations.mjs <results dir> <run id>')
    return 2
  }
  const { files, overBudget } = refresh(resultsDir, root, log)
  for (const { line } of overBudget) log(line)
  const gh = process.env.GH ?? 'gh'
  const issues = issuesFrom(files, overBudget, openProcessIssues(gh), runId)
  if (issues.length === 0) log('The shard durations did not drift, and nothing passes the budget.')
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
