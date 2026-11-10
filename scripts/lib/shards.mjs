// The shard budget and the most shards that scripts/lib/e2e-shard.mjs and scripts/lib/integration-shard.mjs
// use with --count. The budget leaves room under the check timeouts in .nexkit/config.json.

/** The estimated seconds of tests that one shard may take. */
export const budgetSeconds = 15 * 60

/** The most shards --count prints. */
export const maxShards = 6

/** The smallest total from 1 to max whose longest(total) is at most budget, else max. */
export function shardCount(longest, budget = budgetSeconds, max = maxShards) {
  for (let total = 1; total < max; total++) if (longest(total) <= budget) return total
  return max
}

/** Parses a shard such as 2/3 into { index, total }. Throws on a malformed one. */
export function parseShard(text) {
  const match = /^([1-9][0-9]*)\/([1-9][0-9]*)$/.exec(text ?? '')
  if (!match || Number(match[1]) > Number(match[2])) throw new Error(`A shard such as 1/3, not '${text}'.`)
  return { index: Number(match[1]), total: Number(match[2]) }
}
