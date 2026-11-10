// Splits Axis.Integration.Tests over the integration shards by how long its test classes run.
//
// Usage: node scripts/lib/integration-shard.mjs K/N
//        node scripts/lib/integration-shard.mjs --classes K/N
//        node scripts/lib/integration-shard.mjs --count
//        node scripts/lib/integration-shard.mjs --durations JUNIT...
//
// The first prints the dotnet test filter arguments of shard K of N, one per line, and fails when the
// shard has no class, since an empty filter would run every test. Shards 1 to N-1
// run the classes they are given (--filter-class). Shard N runs every class that no other shard
// runs (--filter-not-class), so a test class this script does not find still runs there. No shard
// runs the performance tests. Every shard computes the same split, so each class runs in exactly
// one shard.
// The second prints the full names of the classes given to shard K, one per line.
// The third prints the fewest shards whose longest estimated shard fits the budget in
// scripts/lib/shards.mjs, at most its cap. A class, a collection or the control plane suite that alone
// takes longer than the budget gets an `Over budget:` line on stderr, and the count still prints.
// The fourth rewrites tests/Axis.Integration.Tests/class-durations.json from xUnit JUnit files, such
// as the artifacts/test-results/Axis.Integration.Tests.xml of each shard of a full run, and the
// Axis.ControlPlane.Integration.Tests.xml of shard 1. A class missing from them keeps its entry, and an
// entry whose class no longer exists is dropped.
//
// A class is a type whose name ends in Tests, in a .cs file of the test project that holds a [Fact]
// or a [Theory]. Classes that share an xUnit [Collection] stay in one shard, as one unit. Each class
// weighs its seconds in class-durations.json. A class without an entry weighs its number of tests
// times the median seconds per test of the classes that have one, or its number of tests when none
// has. Shard 1 starts with the seconds of the control plane suite, which it runs beside its classes.
// The units go one at a time, the heaviest first, to the shard that is then the lightest.
import { readdirSync, readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { budgetSeconds, maxShards, parseShard, shardCount } from './shards.mjs'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..')
const project = path.join(root, 'tests/Axis.Integration.Tests')

/** Whether a class is a performance test, which scripts/performance.sh runs instead. */
export const isPerformance = (name) => name.endsWith('PerformanceTests')

/** The key in class-durations.json of the control plane suite, which shard 1 runs beside its classes (see scripts/integration.sh). */
export const controlPlane = 'Axis.ControlPlane.Integration.Tests'

/** The test classes of one C# source: [{ name, collection, tests }], with name its full name. */
export function parseSource(source) {
  if (!/\[(Fact|Theory)\b/.test(source)) return []
  const namespace = /^\s*namespace\s+([\w.]+)\s*[;{]/m.exec(source)?.[1]
  const declaration = /((?:\[[^\]\n]*\]\s*)*)(?:(?:public|internal|sealed|static|abstract|partial)\s+)*class\s+(\w+Tests)\b/g
  const found = [...source.matchAll(declaration)]
  return found.map((match, index) => {
    const body = source.slice(match.index, found[index + 1]?.index ?? source.length)
    // Collection(AdminCellRestoreCheckTests.ScratchTenantCollection) and Collection(ScratchTenantCollection)
    // name the same collection, so only the last part of its name counts.
    const collection = /\[Collection\(\s*([^)]*?)\s*\)\]/.exec(match[1])?.[1].split('.').pop().replace(/"/g, '')
    return {
      name: namespace ? `${namespace}.${match[2]}` : match[2],
      collection,
      tests: Math.max(1, (body.match(/\[(Fact|Theory)\b/g) ?? []).length),
    }
  })
}

function sources(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name)
    if (entry.isDirectory()) return entry.name === 'bin' || entry.name === 'obj' ? [] : sources(full)
    return entry.name.endsWith('.cs') ? [full] : []
  })
}

/** The test classes of the project, without the performance tests, sorted by name. */
export function readClasses(dir = project) {
  return sources(dir)
    .flatMap((file) => parseSource(readFileSync(file, 'utf8')))
    .filter((testClass) => !isPerformance(testClass.name))
    .sort((a, b) => (a.name < b.name ? -1 : 1))
}

function median(values) {
  const sorted = [...values].sort((a, b) => a - b)
  if (sorted.length === 0) return undefined
  const middle = Math.floor(sorted.length / 2)
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2
}

/** The seconds of each class: its measured ones, or an estimate from its number of tests. */
export function weigh(classes, durations) {
  const perTest =
    median(classes.filter((c) => durations[c.name] !== undefined).map((c) => durations[c.name] / c.tests)) ?? 1
  return classes.map((c) => ({ ...c, seconds: durations[c.name] ?? c.tests * perTest }))
}

/** Groups weighed classes into units, heaviest first: the classes of one collection form one unit. */
function units(classes) {
  const grouped = new Map()
  for (const testClass of classes) {
    const key = testClass.collection ? `collection:${testClass.collection}` : testClass.name
    const unit = grouped.get(key) ?? { names: [], seconds: 0, collection: testClass.collection }
    unit.names.push(testClass.name)
    unit.seconds += testClass.seconds
    grouped.set(key, unit)
  }
  return [...grouped.values()].sort((a, b) => b.seconds - a.seconds || (a.names[0] < b.names[0] ? -1 : 1))
}

/** Packs units over `total` shards, the lightest first. Shard 1 starts at `firstShardSeconds`. */
function pack(sorted, total, firstShardSeconds) {
  const shards = Array.from({ length: total }, () => ({ names: [], seconds: 0 }))
  shards[0].seconds = firstShardSeconds
  for (const unit of sorted) {
    let lightest = shards[0]
    for (const shard of shards) if (shard.seconds < lightest.seconds) lightest = shard
    lightest.names.push(...unit.names)
    lightest.seconds += unit.seconds
  }
  return shards
}

/** Splits weighed classes over `total` shards and returns the class names of each. Shard 1 starts at `firstShardSeconds`. */
export function split(classes, total, firstShardSeconds = 0) {
  return pack(units(classes), total, firstShardSeconds).map((shard) => shard.names.sort())
}

/** The fewest shards that fit the budget, and an `Over budget:` line for each unit that alone does not. */
export function count(classes, firstShardSeconds, budget = budgetSeconds, max = maxShards) {
  const all = units(classes)
  const longest = (total) => Math.max(...pack(all, total, firstShardSeconds).map((shard) => shard.seconds))
  const line = (name, seconds) =>
    `Over budget: ${name} takes about ${Math.round(seconds)} s, longer than the budget of ${budget} s.`
  const overBudget = []
  if (firstShardSeconds > budget) overBudget.push(line(controlPlane, firstShardSeconds))
  for (const unit of all) {
    if (unit.seconds <= budget) continue
    const members = [...unit.names].sort().join(', ')
    overBudget.push(line(unit.collection ? `collection ${unit.collection} (${members})` : members, unit.seconds))
  }
  return { total: shardCount(longest, budget, max), overBudget }
}

/** The dotnet test filter arguments of shard `index` (1-based) of `shards`. */
export function filterArgs(shards, index) {
  // A shard without a class would get no filter and run every test.
  if (shards[index - 1].length === 0) throw new Error(`Shard ${index}/${shards.length} has no test class. Use fewer shards.`)
  if (index < shards.length) return shards[index - 1].flatMap((name) => ['--filter-class', name])
  return [
    '--filter-not-class',
    '*PerformanceTests',
    ...shards.slice(0, -1).flatMap((names) => names.flatMap((name) => ['--filter-not-class', name])),
  ]
}

/** The seconds of each class in xUnit JUnit texts, by full class name. */
export function junitDurations(texts) {
  const durations = {}
  for (const text of texts) {
    for (const [, attributes] of text.matchAll(/<testcase\s([^>]*?)\/?>/g)) {
      const attribute = (name) => new RegExp(`\\s${name}="([^"]*)"`).exec(` ${attributes}`)?.[1]
      const name = attribute('classname')
      if (!name) continue
      const key = name.startsWith(`${controlPlane}.`) ? controlPlane : name
      durations[key] = (durations[key] ?? 0) + Number(attribute('time') ?? 0)
    }
  }
  return Object.fromEntries(Object.entries(durations).map(([name, seconds]) => [name, Math.round(seconds)]))
}

/** The new durations: measured ones replace known ones, and only classes that exist are kept. */
export function mergeDurations(known, measured, names) {
  const merged = {}
  for (const name of [...names].sort()) {
    const seconds = measured[name] ?? known[name]
    if (seconds !== undefined) merged[name] = seconds
  }
  return merged
}

/** Runs the command line and returns the text for stdout. Throws on a malformed shard. */
export function main(args, dir = project, warn = (line) => console.error(line)) {
  const file = path.join(dir, 'class-durations.json')
  const durations = JSON.parse(readFileSync(file, 'utf8'))
  const classes = readClasses(dir)
  if (args[0] === '--durations') {
    const measured = junitDurations(args.slice(1).map((junit) => readFileSync(junit, 'utf8')))
    const merged = mergeDurations(durations, measured, [...classes.map((c) => c.name), controlPlane])
    writeFileSync(file, `${JSON.stringify(merged, null, 2)}\n`)
    return `${Object.keys(measured).length} classes measured, ${Object.keys(merged).length} kept in ${file}.`
  }
  if (args[0] === '--count') {
    const { total, overBudget } = count(weigh(classes, durations), durations[controlPlane] ?? 0)
    for (const line of overBudget) warn(line)
    return String(total)
  }
  const listing = args[0] === '--classes'
  const { index, total } = parseShard(listing ? args[1] : args[0])
  const shards = split(weigh(classes, durations), total, durations[controlPlane] ?? 0)
  return (listing ? shards[index - 1] : filterArgs(shards, index)).join('\n')
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  console.log(main(process.argv.slice(2)))
}
