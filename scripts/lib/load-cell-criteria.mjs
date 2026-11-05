// Checks a cell load test run against the pass criteria of docs/reference/performance.md#cell-load-test.
import { claimDelayLimit, claimDelayP95, claimDelayTenants, ms, objectives, percent } from './load-objectives.mjs'

const under = (limit) => (value) => value < limit
const idleSignInLimit = 2000
const usageLimit = 0.8

const busyKeys = {
  'http_req_duration{objective:reads}': 'http_req_duration{scenario:busy,objective:reads,phase:run}',
  'http_req_duration{objective:writes}': 'http_req_duration{scenario:busy,objective:writes,phase:run}',
  server_errors: 'server_errors{scenario:busy,phase:run}',
}

const criteria = [
  ...objectives.map((objective) => ({
    ...objective,
    name: `Busy ${objective.name.charAt(0).toLowerCase()}${objective.name.slice(1)}`,
    metric: busyKeys[objective.metric],
    passes: under(objective.limit),
  })),
  {
    name: 'Idle sign-ins',
    metric: 'idle_sign_ins{phase:run}',
    stat: 'value',
    target: '100%',
    show: percent,
    passes: (value) => value === 1,
  },
  {
    name: 'Idle sign-in p95',
    metric: 'idle_sign_in{phase:run}',
    stat: 'p(95)',
    target: 'under 2 s',
    show: ms,
    passes: under(idleSignInLimit),
  },
]

const usageCriteria = [
  { name: 'Cell CPU peak', key: 'cpu' },
  { name: 'Cell memory peak', key: 'memory' },
  { name: 'Cell database connections peak', key: 'connections' },
]
const usageCriterion = { target: 'under 80%', show: percent, passes: under(usageLimit) }

export const summaryMetrics = criteria.map((criterion) => criterion.metric)

function judge(name, value, source, criterion) {
  if (typeof value !== 'number') return { line: `${name} missing from the ${source}: missed`, passes: false }
  const passes = criterion.passes(value)
  return { line: `${name} ${criterion.show(value)}, objective ${criterion.target}: ${passes ? 'met' : 'missed'}`, passes }
}

// Every tenant with claims counts except the backlogged one, whose own slow claims must not fail the run.
function claimDelayResult(workerMetrics, backlogTenant) {
  let worst
  for (const tenant of claimDelayTenants(workerMetrics)) {
    if (tenant === backlogTenant) continue
    const delay = claimDelayP95(workerMetrics, tenant)
    if (delay !== undefined && (worst === undefined || delay > worst.delay)) worst = { tenant, delay }
  }
  if (worst === undefined) return { line: 'Worker claim p95 missing from the worker metrics: missed', passes: false }
  const passes = worst.delay < claimDelayLimit
  const worstTenant = `at tenant ${worst.tenant}`
  const backlog = `backlogged tenant ${backlogTenant} left out`
  const verdict = passes ? 'met' : 'missed'
  return {
    line: `Worker claim p95 ${worst.delay.toFixed(2)} s ${worstTenant}, objective under ${claimDelayLimit} s, ${backlog}: ${verdict}`,
    passes,
  }
}

export function evaluate(summary, workerMetrics, usage, backlogTenant) {
  const results = [
    ...criteria.map((criterion) =>
      judge(criterion.name, summary.metrics?.[criterion.metric]?.[criterion.stat], 'summary', criterion),
    ),
    ...usageCriteria.map(({ name, key }) => judge(name, usage?.[key], 'usage', usageCriterion)),
    claimDelayResult(workerMetrics, backlogTenant),
  ]
  return { lines: results.map((result) => result.line), met: results.every((result) => result.passes) }
}

export async function findCap(runAt, start = 1000, step = 100) {
  for (let count = start; count >= step; count -= step) {
    if (await runAt(count)) return count
  }
  return null
}
