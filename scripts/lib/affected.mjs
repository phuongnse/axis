// Decides whether a smoke test can be skipped because a change cannot affect it.
//
// Usage: node scripts/lib/affected.mjs NAME
//
// NAME is a suite in smokeSuites. Reads the base commit from $NEXKIT_BASE_SHA and prints
// "skip" when it is set, the changed paths could be listed, and none of them is on the
// suite's list. Prints "run" in every other case, so a doubt never hides a test. A skip
// also writes one line to stderr. Always exits 0.
import { execFileSync } from 'node:child_process'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..')

// A matcher is a directory prefix (ends in "/"), an exact path, or a RegExp.
export const smokeSuites = {
  'dev-script': [
    'scripts/dev.sh',
    'scripts/dev.test.mjs',
    'scripts/lib/',
    'scripts/integration.sh',
    'compose.yaml',
    'src/Axis.Server/appsettings.Development.json',
    'src/Axis.Server/Properties/',
    'src/Axis.Worker/appsettings.Development.json',
    'src/Axis.Worker/Properties/',
    'web/vite.config.ts',
    'web/package.json',
    'web/package-lock.json',
  ],
  compose: [
    'Dockerfile',
    '.dockerignore',
    'compose.yaml',
    'scripts/compose.test.mjs',
    'scripts/lib/',
    'scripts/e2e.sh',
    'global.json',
    'Directory.Build.props',
    'Directory.Packages.props',
    'Axis.slnx',
    /\.csproj$/,
    /^src\/(.+\/)?appsettings\.json$/,
    'web/package.json',
    'web/package-lock.json',
  ],
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

/** Runs unless base is set, the changed paths could be listed and none of them matches. */
export function decide({ base, matchers, listChanged }) {
  if (!base) return { run: true, reason: 'NEXKIT_BASE_SHA is not set.' }
  let paths
  try {
    paths = listChanged(base)
  } catch (error) {
    return { run: true, reason: `The changed paths could not be listed: ${error.message}` }
  }
  const hit = paths.find((filePath) => matches(filePath, matchers))
  if (hit) return { run: true, reason: `${hit} changed.` }
  return { run: false, reason: `No path it covers changed since ${base}.` }
}

/** Runs the command line and returns the text for stdout. */
export function main(args, env = process.env, stderr = process.stderr) {
  const [name] = args
  const matchers = Object.hasOwn(smokeSuites, name) ? smokeSuites[name] : undefined
  if (!matchers) {
    stderr.write(`Unknown suite ${name}. Running it.\n`)
    return 'run'
  }
  const decision = decide({ base: env.NEXKIT_BASE_SHA, matchers, listChanged: changedPaths })
  if (decision.run) return 'run'
  stderr.write(`Skipping ${name}: no path it covers changed since ${env.NEXKIT_BASE_SHA}.\n`)
  return 'skip'
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    console.log(main(process.argv.slice(2)))
  } catch (error) {
    console.error(error)
    console.log('run')
  }
}
