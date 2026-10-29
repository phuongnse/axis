// Decides whether a suite can be skipped because a change cannot affect it.
//
// Usage: node scripts/lib/affected.mjs NAME
//
// NAME is a suite in safeSuites. Reads the commit to compare with from NexKit's variables (see
// sinceCommit) and prints "skip" when one is set, the changed paths could be listed and every one of
// them is on the suite's safe list. Prints "run" in every other case, so a doubt never hides a test.
// A skip also writes one line to stderr. Always exits 0.
import { execFileSync } from 'node:child_process'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..')

// A matcher is a directory prefix (ends in "/"), an exact path, or a RegExp.
// No smoke suite or Playwright reads docs/ or Markdown files.
const docsOnly = ['docs/', /\.md$/]

// A safe list names only paths that its suite never reads, so a change to them cannot change the result.
export const safeSuites = {
  integration: ['docs/', 'tests/e2e/', /\.md$/, /^web\/(?!package(-lock)?\.json$)/],
  playwright: docsOnly,
  'dev-script': docsOnly,
  compose: docsOnly,
  images: docsOnly,
  onprem: docsOnly,
}

// NexKit's commits, in the order a check prefers them: the head on which this check last passed,
// the head of the last round whose checks all passed, and the merge base with the base branch. None
// is set outside NexKit, so CI and local runs run every suite.
export const sinceVariables = ['NEXKIT_CHECK_VERIFIED_SHA', 'NEXKIT_VERIFIED_SHA', 'NEXKIT_BASE_SHA']

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

/** Whether filePath is covered by any of the matchers. */
export function matches(filePath, matchers) {
  return matchers.some((matcher) => {
    if (matcher instanceof RegExp) return matcher.test(filePath)
    return matcher.endsWith('/') ? filePath.startsWith(matcher) : filePath === matcher
  })
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

/** Runs unless base is set, the changed paths could be listed, and every one of them matches. */
export function decideSafe({ base, matchers, listChanged }) {
  const { paths, reason } = listOrReason(base, listChanged)
  if (!paths) return { run: true, reason }
  if (paths.length === 0) return { run: true, reason: 'No changed path was listed.' }
  const miss = paths.find((filePath) => !matches(filePath, matchers))
  if (miss) return { run: true, reason: `${miss} changed.` }
  return { run: false, reason: 'Every changed path is on its safe list.' }
}

/** Runs the command line and returns the text for stdout. */
export function main(args, env = process.env, stderr = process.stderr) {
  const [name] = args
  const since = sinceCommit(env)
  if (Object.hasOwn(safeSuites, name)) {
    const decision = decideSafe({ base: since?.sha, matchers: safeSuites[name], listChanged: changedPaths })
    if (decision.run) return 'run'
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
