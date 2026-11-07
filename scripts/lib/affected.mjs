// Decides whether a suite can be skipped because a change cannot affect it.
//
// Usage: node scripts/lib/affected.mjs NAME
//
// NAME is a suite in suiteScripts. Reads the commit to compare with from NexKit's variables (see
// sinceCommit) and the safe lists from `unaffected_by` in .nexkit/config.json (see safeLists). Prints
// "skip" when a commit is set, the changed paths could be listed and every one of them is on the suite's
// safe list. Prints "run" in every other case, so a doubt never hides a test.
// A skip also writes one line to stderr. Always exits 0.
import { execFileSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..')

// The script that runs each suite. A suite's safe list is what every NexKit check that runs its script
// lists in `unaffected_by` in .nexkit/config.json, so a safe list names only paths its suite never reads.
// The integration tests read no SPA file, except the Keycloak login theme that scripts/integration.sh
// builds from web/keycloak-theme/ with the Vite configs: they read its sign-in page. The config therefore
// takes those paths back out of the integration lists.
export const suiteScripts = {
  integration: 'scripts/integration.sh',
  playwright: 'scripts/e2e.sh',
  'dev-script': 'scripts/smoke.sh',
  compose: 'scripts/smoke.sh',
  images: 'scripts/smoke.sh',
  onprem: 'scripts/smoke.sh',
}

// NexKit's commits, in the order a check prefers them: the head on which this check last passed with
// its current command and setup, then the merge base with the base branch. NEXKIT_VERIFIED_SHA, the head of the
// last round whose checks all passed, is never used: NexKit does not record which checks or commands
// that round ran, so a check added or changed since would skip without ever having passed. None is
// set outside NexKit, so CI and local runs run every suite.
export const sinceVariables = ['NEXKIT_CHECK_VERIFIED_SHA', 'NEXKIT_BASE_SHA']

/** The first of sinceVariables that env sets, as { name, sha }, or undefined. */
export function sinceCommit(env) {
  const name = sinceVariables.find((variable) => env[variable])
  return name ? { name, sha: env[name] } : undefined
}

/** Lists the paths changed since base, including untracked files. Throws when git fails. */
export function changedPaths(base, cwd = root) {
  const git = (...args) => execFileSync('git', args, { cwd, encoding: 'utf8' })
  const output = git('diff', '--name-only', '--no-renames', base) + git('ls-files', '--others', '--exclude-standard')
  return output.split('\n').filter(Boolean)
}

/** Reads NexKit's settings. Throws when they cannot be read. */
export function readConfig(cwd = root) {
  return JSON.parse(readFileSync(path.join(cwd, '.nexkit/config.json'), 'utf8'))
}

/** The `unaffected_by` list of each check that runs the suite's script, or [] for a check without one. */
export function safeLists(name, config) {
  const script = suiteScripts[name]
  return config.checks
    .filter((check) => check.run.split(/\s+/).includes(script))
    .map((check) => check.unaffected_by ?? [])
}

// A NexKit glob: `*` and `?` stay in a segment, and `**`, a whole segment, crosses folders.
export function globRegExp(pattern) {
  const segments = pattern.split('/')
  const source = segments
    .map((segment, index) => {
      const last = index === segments.length - 1
      if (segment === '**') return last ? '.*' : '(?:[^/]+/)*'
      const text = segment
        .replace(/[.+^${}()|\\]/g, '\\$&')
        .replace(/\*/g, '[^/]*')
        .replace(/\?/g, '[^/]')
      return last ? text : `${text}/`
    })
    .join('')
  return new RegExp(`^${source}$`)
}

/** Whether patterns list filePath: the last pattern that matches decides, and a `!` pattern takes it out. */
export function unaffected(filePath, patterns) {
  return patterns.reduce((listed, pattern) => {
    const negated = pattern.startsWith('!')
    return globRegExp(negated ? pattern.slice(1) : pattern).test(filePath) ? !negated : listed
  }, false)
}

/** Whether every list covers filePath. No list covers nothing, so a doubt runs the suite. */
export function isSafe(filePath, lists) {
  return lists.length > 0 && lists.every((patterns) => unaffected(filePath, patterns))
}

/** Lists the changed paths, or says why they cannot be listed. */
function listOrReason(base, listChanged) {
  if (!base) return { reason: `None of ${sinceVariables.join(', ')} is set.` }
  try {
    return { paths: listChanged(base) }
  } catch (error) {
    return { reason: `The changed paths could not be listed: ${error.message}` }
  }
}

/** Runs unless base is set, the changed paths could be listed, and every one of them is safe. */
export function decideSafe({ base, lists, listChanged }) {
  if (lists.length === 0) return { run: true, reason: 'No NexKit check lists the paths it never reads.' }
  const { paths, reason } = listOrReason(base, listChanged)
  if (!paths) return { run: true, reason }
  if (paths.length === 0) return { run: true, reason: 'No changed path was listed.' }
  const miss = paths.find((filePath) => !isSafe(filePath, lists))
  if (miss) return { run: true, reason: `${miss} changed.` }
  return { run: false, reason: 'Every changed path is on its safe list.' }
}

/** Runs the command line and returns the text for stdout. */
export function main(args, env = process.env, stderr = process.stderr) {
  const [name] = args
  const since = sinceCommit(env)
  if (Object.hasOwn(suiteScripts, name)) {
    let decision
    try {
      decision = decideSafe({ base: since?.sha, lists: safeLists(name, readConfig()), listChanged: changedPaths })
    } catch (error) {
      decision = { run: true, reason: `The NexKit settings could not be read: ${error.message}` }
    }
    if (decision.run) {
      // Outside NexKit every suite runs, so only a run under NexKit says why.
      if (since) {
        stderr.write(`Running ${name} in full: ${decision.reason} Compared with ${since.sha} (${since.name}).\n`)
      }
      return 'run'
    }
    stderr.write(`Skipping ${name}: every path changed since ${since.sha} (${since.name}) is on its safe list.\n`)
    return 'skip'
  }
  stderr.write(`Unknown suite ${name}. Running it.\n`)
  return 'run'
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    console.log(main(process.argv.slice(2)))
  } catch (error) {
    console.error(error)
    console.log('run')
  }
}
