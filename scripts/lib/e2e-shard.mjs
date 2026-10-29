// Splits the Playwright specs over the E2E shards by how long they run.
//
// Usage: node scripts/lib/e2e-shard.mjs K/N
//        node scripts/lib/e2e-shard.mjs --durations JUNIT...
//
// The first prints the specs of shard K of N, one path per line relative to tests/e2e, such as
// specs/form.spec.ts. Every shard computes the same split, so each spec runs in exactly one shard.
// The second rewrites tests/e2e/spec-durations.json from Playwright JUnit files, such as the
// artifacts/test-results/playwright-*.xml of a full run. A spec missing from them keeps its entry,
// and an entry whose spec no longer exists is dropped.
//
// Playwright's own --shard splits by test count, so one shard got both long serial journeys and took
// twice as long as the others. Here each spec weighs its seconds in tests/e2e/spec-durations.json,
// and a spec without an entry weighs the median. The specs go one at a time, the heaviest first, to
// the shard whose estimated run then ends soonest. A shard runs on `workers` workers: its tests share
// them, except that the tests of a serial spec run one after another, starting once the specs before
// it in Playwright's file order have shared the workers.
import { readdirSync, readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..')
const e2e = path.join(root, 'tests/e2e')

// Playwright uses two workers on the CI and NexKit runners.
const workers = 2

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

/** The median of the known durations, for a spec without one. */
function median(values) {
  const sorted = [...values].sort((a, b) => a - b)
  if (sorted.length === 0) return 1
  const middle = Math.floor(sorted.length / 2)
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2
}

/** Reads the specs of tests/e2e/specs with their weights. */
export function readSpecs(dir = e2e) {
  const durations = JSON.parse(readFileSync(path.join(dir, 'spec-durations.json'), 'utf8'))
  const fallback = median(Object.values(durations))
  return readdirSync(path.join(dir, 'specs'))
    .filter((name) => name.endsWith('.spec.ts'))
    .map((name) => {
      const specPath = `specs/${name}`
      const source = readFileSync(path.join(dir, specPath), 'utf8')
      return { path: specPath, seconds: durations[specPath] ?? fallback, serial: isSerial(source) }
    })
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

/** The new durations: measured ones replace known ones, and only specs that exist are kept. */
export function mergeDurations(known, measured, specPaths) {
  const merged = {}
  for (const specPath of [...specPaths].sort()) {
    const seconds = measured[specPath] ?? known[specPath]
    if (seconds !== undefined) merged[specPath] = seconds
  }
  return merged
}

/** Runs the command line and returns the text for stdout. Throws on a malformed shard. */
export function main(args, dir = e2e) {
  if (args[0] === '--durations') {
    const file = path.join(dir, 'spec-durations.json')
    const measured = junitDurations(args.slice(1).map((junit) => readFileSync(junit, 'utf8')))
    const specPaths = readSpecs(dir).map((spec) => spec.path)
    const merged = mergeDurations(JSON.parse(readFileSync(file, 'utf8')), measured, specPaths)
    writeFileSync(file, `${JSON.stringify(merged, null, 2)}\n`)
    return `${Object.keys(measured).length} specs measured, ${Object.keys(merged).length} kept in ${file}.`
  }
  const match = /^([1-9][0-9]*)\/([1-9][0-9]*)$/.exec(args[0] ?? '')
  if (!match || Number(match[1]) > Number(match[2])) throw new Error(`A shard such as 1/3, not '${args[0]}'.`)
  const shards = split(readSpecs(dir), Number(match[2]), workers)
  return shards[Number(match[1]) - 1].join('\n')
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  console.log(main(process.argv.slice(2)))
}
