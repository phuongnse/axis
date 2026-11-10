// The shard budget, the most shards --count prints for each split, and the helpers that the E2E,
// integration, smoke and unit test splits share: scripts/lib/e2e-shard.mjs,
// scripts/lib/integration-shard.mjs, scripts/lib/smoke-shard.mjs and scripts/lib/test-shard.mjs. The
// budget leaves room under the check timeouts in .nexkit/config.json.

/** The estimated seconds of tests that one shard may take. */
export const budgetSeconds = 15 * 60

/**
 * The most shards --count prints for each split. A sharded check's max in .nexkit/config.json matches its entry,
 * since NexKit fails a check whose count passes its max (docs/development.md#shards). Two NexKit rounds at these
 * caps, with build-lint and smoke, fit within the jobs GitHub runs at once. Smoke runs as one check, since a sharded
 * check's max is at least 2 and a second smoke job would take two rounds past the limit, so its cap only bounds the
 * count that the durations job reads.
 */
export const maxShards = { e2e: 3, integration: 3, smoke: 6, test: 2 }

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

/** The jobs GitHub's Free plan runs at once for the account. configProblems checks that two NexKit rounds fit (docs/development.md#shards). */
export const jobLimit = 20

/** The split of each sharded check, by the check's name, and the script the check runs. */
export const shardedChecks = {
  e2e: { split: 'e2e', run: 'scripts/e2e.sh' },
  integration: { split: 'integration', run: 'scripts/integration.sh' },
  test: { split: 'test', run: 'scripts/test.sh' },
}

/**
 * What is wrong with the checks of a .nexkit/config.json: a sharded check missing or listed twice, one whose count is
 * not its split's --count or whose max is not its split's cap, a fixed shard such as e2e-1 left over, or caps that
 * leave no room for two rounds within the job limit. Returns one line per problem.
 */
export function configProblems(config) {
  const problems = []
  const checks = config.checks ?? []
  for (const [name, { split, run }] of Object.entries(shardedChecks)) {
    const found = checks.filter((check) => check.name === name)
    if (found.length !== 1) {
      problems.push(`${name} is listed ${found.length} times, not once.`)
      continue
    }
    const [check] = found
    if (check.run !== run) problems.push(`${name} runs '${check.run}', not '${run}'.`)
    const count = `node scripts/lib/${split}-shard.mjs --count`
    if (check.shards?.count !== count) problems.push(`${name} counts its shards with '${check.shards?.count}', not '${count}'.`)
    if (check.shards?.max !== maxShards[split]) {
      problems.push(`${name} has max ${check.shards?.max}, not its split's cap of ${maxShards[split]}.`)
    }
  }
  for (const check of checks) {
    if (/^(e2e|integration|test)-\d+$/.test(check.name)) problems.push(`${check.name} is a fixed shard; its check has shards.`)
  }
  const jobs = new Set(checks.filter((check) => !check.shards).map((check) => check.group ?? check.name)).size
  const round = jobs + checks.reduce((sum, check) => sum + (check.shards?.max ?? 0), 0)
  if (2 * round > jobLimit) {
    problems.push(`A round at its caps runs ${round} jobs, so two rounds pass the ${jobLimit} jobs GitHub runs at once.`)
  }
  return problems
}
