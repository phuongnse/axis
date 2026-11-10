// The shard budget, the most shards --count prints for each split, and the helpers that the E2E,
// integration, smoke and unit test splits share: scripts/lib/e2e-shard.mjs,
// scripts/lib/integration-shard.mjs, scripts/lib/smoke-shard.mjs and scripts/lib/test-shard.mjs. The
// budget leaves room under the check timeouts in .nexkit/config.json.

/** The estimated seconds of tests that one shard may take. */
export const budgetSeconds = 15 * 60

/** The most shards --count prints for each split. A sharded check's max in .nexkit/config.json must match its entry (#3610). */
export const maxShards = { e2e: 3, integration: 3, smoke: 6, test: 6 }

/** The median of the values, or undefined when there are none. */
export function median(values) {
  const sorted = [...values].sort((a, b) => a - b)
  if (sorted.length === 0) return undefined
  const middle = Math.floor(sorted.length / 2)
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2
}

/** The new durations: measured ones replace known ones, and only names that exist are kept. */
export function mergeDurations(known, measured, names) {
  const merged = {}
  for (const name of [...names].sort()) {
    const seconds = measured[name] ?? known[name]
    if (seconds !== undefined) merged[name] = seconds
  }
  return merged
}

/** The smallest total from 1 to max whose longest(total) is at most budget, else max. */
export function shardCount(longest, budget, max) {
  for (let total = 1; total < max; total++) if (longest(total) <= budget) return total
  return max
}

/** Parses a shard such as 2/3 into { index, total }. Throws on a malformed one. */
export function parseShard(text) {
  const match = /^([1-9][0-9]*)\/([1-9][0-9]*)$/.exec(text ?? '')
  if (!match || Number(match[1]) > Number(match[2])) throw new Error(`A shard such as 1/3, not '${text}'.`)
  return { index: Number(match[1]), total: Number(match[2]) }
}

/**
 * Packs units, each { names, seconds } and sorted heaviest first, over `total` shards. Each unit goes to the
 * shard that is then the lightest. Shard 1 starts at `firstShardSeconds`.
 */
export function packLightest(units, total, firstShardSeconds = 0) {
  const shards = Array.from({ length: total }, () => ({
    names: [],
    seconds: 0,
  }))
  shards[0].seconds = firstShardSeconds
  for (const unit of units) {
    let lightest = shards[0]
    for (const shard of shards) if (shard.seconds < lightest.seconds) lightest = shard
    lightest.names.push(...unit.names)
    lightest.seconds += unit.seconds
  }
  return shards
}
