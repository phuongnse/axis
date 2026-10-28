// Decides whether a suite can be skipped because a change cannot affect it.
//
// Usage: node scripts/lib/affected.mjs NAME
//
// NAME is a suite in safeSuites. Reads the base commit from $NEXKIT_BASE_SHA and prints "skip"
// when it is set, the changed paths could be listed and every one of them is on the suite's safe
// list. Prints "run" in every other case, so a doubt never hides a test. A skip also writes one
// line to stderr. Always exits 0.
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
  if (!base) return { reason: 'NEXKIT_BASE_SHA is not set.' }
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
  const base = env.NEXKIT_BASE_SHA
  if (Object.hasOwn(safeSuites, name)) {
    const decision = decideSafe({ base, matchers: safeSuites[name], listChanged: changedPaths })
    if (decision.run) return 'run'
    stderr.write(`Skipping ${name}: every path changed since ${base} is on its safe list.\n`)
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
