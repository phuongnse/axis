// Splits the smoke suites over the smoke shards by how long they run.
//
// Usage: node scripts/lib/smoke-shard.mjs K/N
//        node scripts/lib/smoke-shard.mjs --count
//        node scripts/lib/smoke-shard.mjs --durations JUNIT...
//
// The first prints the names of the suites of shard K of N, one per line. Every shard computes the same
// split, so each suite runs in exactly one shard. The second prints the fewest shards whose longest
// estimated shard fits the budget in scripts/lib/shards.mjs, at most its cap. A suite that alone takes
// longer than the budget gets an `Over budget:` line on stderr, and the count still prints. The third
// rewrites scripts/smoke-durations.json from node --test JUnit files, such as the
// artifacts/test-results/dev-script.xml of a full run.
//
// Each suite weighs its seconds in scripts/smoke-durations.json, and a suite without an entry weighs the
// median of the known ones. The image builds are not in those seconds, so a shard that builds the images
// takes longer than its estimate.
import { readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { budgetSeconds, maxShards, median, mergeDurations, packLightest, parseShard, shardCount } from './shards.mjs'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..')
const durationsFile = path.join(root, 'scripts/smoke-durations.json')

/** The suites of scripts/smoke.sh, in its order. */
export const suites = ['dev-script', 'compose', 'images', 'onprem']

/** The suites with their seconds from `durations`, by default the file's. A suite without an entry weighs the median of the known ones, or 1. */
export function readSuites(file, durations = JSON.parse(readFileSync(file, 'utf8'))) {
  const known = suites.filter((name) => durations[name] !== undefined).map((name) => durations[name])
  const fallback = median(known) ?? 1
  return suites.map((name) => ({ name, seconds: durations[name] ?? fallback }))
}

/** The suites as units for packLightest, heaviest first. */
function units(list) {
  return [...list]
    .sort((a, b) => b.seconds - a.seconds || (a.name < b.name ? -1 : 1))
    .map((suite) => ({ names: [suite.name], seconds: suite.seconds }))
}

/** Splits the suites over `total` shards and returns the names of each, sorted. */
export function split(list, total) {
  return packLightest(units(list), total).map((shard) => shard.names.sort())
}

/** The estimated seconds of the longest of `shards`, each a list of suite names, weighed by the seconds of `list`. */
export function longestShard(shards, list) {
  return Math.max(
    ...shards.map((names) =>
      list.filter((suite) => names.includes(suite.name)).reduce((sum, suite) => sum + suite.seconds, 0),
    ),
  )
}

/** The longest estimated shard, weighed with `weighBy`, of the split that `splitBy` gives at its own count. */
export function longest(splitBy, weighBy) {
  const suites = readSuites(durationsFile, splitBy)
  const { total } = count(suites)
  return longestShard(split(suites, total), readSuites(durationsFile, weighBy))
}

/** The fewest shards that fit the budget, and an `Over budget:` line for each suite that alone does not. */
export function count(list, budget = budgetSeconds, max = maxShards.smoke) {
  const longestOf = (total) => longestShard(split(list, total), list)
  const overBudget = list
    .filter((suite) => suite.seconds > budget)
    .map(
      (suite) =>
        `Over budget: ${suite.name} takes about ${Math.round(suite.seconds)} s, longer than the budget of ${budget} s.`,
    )
  return { total: shardCount(longestOf, budget, max), overBudget }
}

/** The seconds of each suite in node --test JUnit files, by file name without .xml. Only top-level elements count, since nested tests belong to their describe block. */
export function junitDurations(files) {
  const durations = {}
  for (const file of files) {
    let depth = 0
    let seconds = 0
    for (const [, closing, , attributes, selfClosing] of readFileSync(file, 'utf8').matchAll(
      /<(\/?)(testsuite|testcase)\b([^>]*?)(\/?)>/g,
    )) {
      if (closing) {
        depth--
        continue
      }
      if (depth === 0) seconds += Number(/\stime="([^"]*)"/.exec(attributes)?.[1] ?? 0)
      if (!selfClosing) depth++
    }
    durations[path.basename(file, '.xml')] = Math.round(seconds)
  }
  return durations
}

/** Runs the command line and returns the text for stdout. Throws on a malformed shard or a shard with no suite. */
export function main(args, file = durationsFile, warn = (line) => console.error(line)) {
  if (args[0] === '--count') {
    const { total, overBudget } = count(readSuites(file))
    for (const line of overBudget) warn(line)
    return String(total)
  }
  if (args[0] === '--durations') {
    const measured = junitDurations(args.slice(1))
    const merged = mergeDurations(JSON.parse(readFileSync(file, 'utf8')), measured, suites)
    writeFileSync(file, `${JSON.stringify(merged, null, 2)}\n`)
    return `${Object.keys(measured).length} suites measured, ${Object.keys(merged).length} kept in ${file}.`
  }
  const { index, total } = parseShard(args[0])
  const names = split(readSuites(file), total)[index - 1]
  if (names.length === 0) throw new Error(`Shard ${args[0]} has no smoke suite. Use fewer shards.`)
  return names.join('\n')
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  console.log(main(process.argv.slice(2)))
}
