// Usage: node scripts/lib/test-shard.mjs K/N | --count | --durations JUNIT...
import { globSync, readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { budgetSeconds, maxShards, median, mergeDurations, parseShard, shardCount } from './shards.mjs'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..')
const web = path.join(root, 'web')

// vitestWorkers on a 4-core, 16 GB runner, as GitHub's standard runners are.
export const workers = 4

/** The Vitest files of the folder, sorted, with the seconds of each from vitest-durations.json. */
export function readFiles(dir = web) {
  const durations = JSON.parse(readFileSync(path.join(dir, 'vitest-durations.json'), 'utf8'))
  const fallback = median(Object.values(durations)) ?? 1
  return globSync('**/*.{test,spec}.{js,mjs,cjs,ts,mts,cts,jsx,tsx}', {
    cwd: dir,
    exclude: (name) => name.includes('node_modules'),
  })
    .sort()
    .map((filePath) => ({ path: filePath, seconds: durations[filePath] ?? fallback }))
}

/** The estimated seconds of a Vitest shard that runs these files on the workers. */
export function estimate(files, workers) {
  const sum = files.reduce((total, file) => total + file.seconds, 0)
  const longest = files.reduce((most, file) => Math.max(most, file.seconds), 0)
  return Math.max(sum / workers, longest)
}

/** Packs the files, heaviest first, into `vitestShards` shards of file objects. */
function pack(files, vitestShards, workers) {
  const shards = Array.from({ length: vitestShards }, () => [])
  const heaviestFirst = [...files].sort((a, b) => b.seconds - a.seconds || (a.path < b.path ? -1 : 1))
  for (const file of heaviestFirst) {
    let best = 0
    let bestEnd = Infinity
    shards.forEach((shard, index) => {
      const end = estimate([...shard, file], workers)
      if (end < bestEnd) {
        best = index
        bestEnd = end
      }
    })
    shards[best].push(file)
  }
  return shards
}

/** Splits the files over `vitestShards` shards and returns the paths of each, sorted. */
export function split(files, vitestShards, workers) {
  return pack(files, vitestShards, workers).map((shard) => shard.map((file) => file.path).sort())
}

/** The paths of the Vitest files of shard `index` (1-based) of `total`. */
export function shardFiles(files, index, total, workers) {
  if (total === 1) return files.map((file) => file.path)
  if (index === 1) return []
  return split(files, total - 1, workers)[index - 2]
}

/** The fewest test shards that fit the budget, and an `Over budget:` line for each file that alone does not. */
export function count(files, workers, budget = budgetSeconds, max = maxShards) {
  const longest = (vitestShards) =>
    Math.max(...pack(files, vitestShards, workers).map((shard) => estimate(shard, workers)))
  const overBudget = files
    .filter((file) => file.seconds > budget)
    .map(
      (file) =>
        `Over budget: ${file.path} takes about ${Math.round(file.seconds)} s, longer than the budget of ${budget} s.`,
    )
  return { total: 1 + shardCount(longest, budget, max - 1), overBudget }
}

/** The seconds of each file in Vitest JUnit texts, by path relative to web/. */
export function junitDurations(texts) {
  const durations = {}
  for (const text of texts) {
    for (const [, attributes] of text.matchAll(/<testsuite\s([^>]*)>/g)) {
      // A space before the name, so name never matches the end of hostname.
      const attribute = (name) => new RegExp(`\\s${name}="([^"]*)"`).exec(` ${attributes}`)?.[1]
      const name = attribute('name')
      durations[name] = (durations[name] ?? 0) + Number(attribute('time'))
    }
  }
  return Object.fromEntries(Object.entries(durations).map(([name, seconds]) => [name, Math.round(seconds)]))
}

/** Runs the command line and returns the text for stdout. Throws on a malformed shard. */
export function main(args, dir = web, warn = (line) => console.error(line)) {
  if (args[0] === '--count') {
    const { total, overBudget } = count(readFiles(dir), workers)
    for (const line of overBudget) warn(line)
    return String(total)
  }
  if (args[0] === '--durations') {
    const file = path.join(dir, 'vitest-durations.json')
    const measured = junitDurations(args.slice(1).map((junit) => readFileSync(junit, 'utf8')))
    const paths = readFiles(dir).map((testFile) => testFile.path)
    const merged = mergeDurations(JSON.parse(readFileSync(file, 'utf8')), measured, paths)
    writeFileSync(file, `${JSON.stringify(merged, null, 2)}\n`)
    return `${Object.keys(measured).length} files measured, ${Object.keys(merged).length} kept in ${file}.`
  }
  const { index, total } = parseShard(args[0])
  return shardFiles(readFiles(dir), index, total, workers).join('\n')
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  console.log(main(process.argv.slice(2)))
}
