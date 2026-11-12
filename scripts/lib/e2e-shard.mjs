// Splits the Playwright specs over the E2E shards by how long they run.
//
// Usage: node scripts/lib/e2e-shard.mjs K/N
//        node scripts/lib/e2e-shard.mjs --count
//        node scripts/lib/e2e-shard.mjs --durations JUNIT...
//
// The first prints the specs of shard K of N, one path per line relative to tests/e2e, such as
// specs/form.spec.ts. Every shard computes the same split, so each spec runs in exactly one shard.
// The second prints the fewest shards whose longest estimated shard fits the budget in
// scripts/lib/shards.mjs, at most its cap. A spec that alone takes longer than the budget gets an
// `Over budget:` line on stderr, and the count still prints.
// The third rewrites tests/e2e/spec-durations.json from Playwright JUnit files, such as the
// artifacts/test-results/playwright-*.xml of a full run. A spec missing from them keeps its entry,
// and an entry whose spec no longer exists is dropped. It also measures the shard setup under
// `shard setup`: the wall time of each file minus the estimate of its specs, averaged over the files.
//
// Playwright's own --shard splits by test count, so one shard got both long serial journeys and took
// twice as long as the others. Here each spec weighs its seconds in tests/e2e/spec-durations.json,
// and a spec without an entry weighs the median. The specs go one at a time, the heaviest first, to
// the shard whose estimated run then ends soonest. A shard runs on `workers` workers: its tests share
// them, except that the tests of a serial spec run one after another, starting once the specs before
// it in Playwright's file order have shared the workers. Each shard's estimate adds the shard setup,
// the seconds it pays besides its specs, such as starting the web servers. The split ignores the setup.
import { readdirSync, readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { budgetSeconds, maxShards, median, mergeDurations, parseShard, shardCount } from './shards.mjs'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..')
const e2e = path.join(root, 'tests/e2e')

// Playwright uses two workers on the CI and NexKit runners.
const workers = 2

/** The key in spec-durations.json of the seconds a shard pays besides its specs. */
export const setupKey = 'shard setup'

/** Whether a spec's source runs its tests one after another. */
export const isSerial = (source) => /mode:\s*['"]serial['"]/.test(source)

/** The estimated seconds of a shard that runs these specs, in Playwright's file order. */
export function estimate(specs, workers) {
  let before = 0
  let end = 0
  for (const spec of [...specs].sort((a, b) => (a.path < b.path ? -1 : 1))) {
    if (spec.serial) end = Math.max(end, before / workers + spec.seconds)
    before += spec.seconds
  }
  return Math.max(end, before / workers)
}

/** Splits specs, each { path, seconds, serial }, over `total` shards and returns their paths. */
export function split(specs, total, workers) {
  const shards = Array.from({ length: total }, () => [])
  const heaviestFirst = [...specs].sort((a, b) => b.seconds - a.seconds || (a.path < b.path ? -1 : 1))
  for (const spec of heaviestFirst) {
    let best = 0
    let bestEnd = Infinity
    shards.forEach((shard, index) => {
      const end = estimate([...shard, spec], workers)
      if (end < bestEnd) {
        best = index
        bestEnd = end
      }
    })
    shards[best].push(spec)
  }
  return shards.map((shard) => shard.map((spec) => spec.path).sort())
}

/** The estimated seconds of the longest of `shards`, each a list of spec paths, with `setup` added to each shard. */
export function longestShard(shards, specs, setup, workers) {
  return Math.max(...shards.map((paths) => setup + estimate(specs.filter((s) => paths.includes(s.path)), workers)))
}

/** The fewest shards that fit the budget, and an `Over budget:` line for each spec that alone does not. */
export function count(specs, workers, budget = budgetSeconds, max = maxShards.e2e, setup = 0) {
  const longestOf = (total) => longestShard(split(specs, total, workers), specs, setup, workers)
  const overBudget = []
  for (const spec of specs) {
    const seconds = Math.round(setup + estimate([spec], workers))
    if (seconds > budget) {
      overBudget.push(`Over budget: ${spec.path} takes about ${seconds} s, longer than the budget of ${budget} s.`)
    }
  }
  return { total: shardCount(longestOf, budget, max), overBudget }
}

/** The seconds a shard pays besides its specs, or 0 when the durations do not measure it. */
export function readSetup(dir = e2e) {
  return JSON.parse(readFileSync(path.join(dir, 'spec-durations.json'), 'utf8'))[setupKey] ?? 0
}

/** Reads the specs of tests/e2e/specs with their weights from `durations`, by default spec-durations.json. */
export function readSpecs(
  dir = e2e,
  durations = JSON.parse(readFileSync(path.join(dir, 'spec-durations.json'), 'utf8')),
) {
  const specSeconds = Object.entries(durations)
    .filter(([name]) => name !== setupKey)
    .map(([, seconds]) => seconds)
  const fallback = median(specSeconds) ?? 1
  return readdirSync(path.join(dir, 'specs'))
    .filter((name) => name.endsWith('.spec.ts'))
    .map((name) => {
      const specPath = `specs/${name}`
      const source = readFileSync(path.join(dir, specPath), 'utf8')
      return { path: specPath, seconds: durations[specPath] ?? fallback, serial: isSerial(source) }
    })
}

/** The longest estimated shard, weighed with `weighBy`, of the split that `splitBy` gives at its own count. */
export function longest(splitBy, weighBy, dir = e2e) {
  const specs = readSpecs(dir, splitBy)
  const { total } = count(specs, workers, budgetSeconds, maxShards.e2e, splitBy[setupKey] ?? 0)
  return longestShard(split(specs, total, workers), readSpecs(dir, weighBy), weighBy[setupKey] ?? 0, workers)
}

/** The seconds of each chromium spec in Playwright JUnit texts, by spec path. A suite's name is its
 * file relative to the specs folder, such as form.spec.ts. */
export function junitDurations(texts) {
  const durations = {}
  for (const text of texts) {
    for (const [, attributes] of text.matchAll(/<testsuite\s([^>]*)>/g)) {
      // A space before the name, so name never matches the end of hostname.
      const attribute = (name) => new RegExp(`\\s${name}="([^"]*)"`).exec(` ${attributes}`)?.[1]
      if (attribute('hostname') !== 'chromium') continue
      const name = `specs/${attribute('name')}`
      durations[name] = (durations[name] ?? 0) + Math.round(Number(attribute('time')))
    }
  }
  return durations
}

/**
 * The shard setup of the JUnit texts: the mean over the files with a wall time of that time minus the estimate
 * of the specs the file ran, or undefined when no file has one.
 */
export function junitSetup(texts, specs, workers) {
  const setups = []
  for (const text of texts) {
    const wall = /<testsuites\s[^>]*\stime="([^"]*)"/.exec(text)?.[1]
    if (wall === undefined) continue
    const ran = Object.entries(junitDurations([text])).map(([specPath, seconds]) => ({
      path: specPath,
      seconds,
      serial: specs.find((spec) => spec.path === specPath)?.serial ?? false,
    }))
    setups.push(Number(wall) - estimate(ran, workers))
  }
  if (setups.length === 0) return undefined
  return Math.round(setups.reduce((sum, setup) => sum + setup, 0) / setups.length)
}

/** Runs the command line and returns the text for stdout. Throws on a malformed shard. */
export function main(args, dir = e2e, warn = (line) => console.error(line)) {
  if (args[0] === '--count') {
    const { total, overBudget } = count(readSpecs(dir), workers, budgetSeconds, maxShards.e2e, readSetup(dir))
    for (const line of overBudget) warn(line)
    return String(total)
  }
  if (args[0] === '--durations') {
    const file = path.join(dir, 'spec-durations.json')
    const texts = args.slice(1).map((junit) => readFileSync(junit, 'utf8'))
    const specs = readSpecs(dir)
    const measured = junitDurations(texts)
    const setup = junitSetup(texts, specs, workers)
    const fresh = setup === undefined ? measured : { ...measured, [setupKey]: setup }
    const names = [...specs.map((spec) => spec.path), setupKey]
    const merged = mergeDurations(JSON.parse(readFileSync(file, 'utf8')), fresh, names)
    writeFileSync(file, `${JSON.stringify(merged, null, 2)}\n`)
    return `${Object.keys(measured).length} specs measured, ${Object.keys(merged).length} kept in ${file}.`
  }
  const { index, total } = parseShard(args[0])
  return split(readSpecs(dir), total, workers)[index - 1].join('\n')
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  console.log(main(process.argv.slice(2)))
}
