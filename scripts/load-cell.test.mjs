import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { evaluate, findCap, summaryMetrics } from './lib/load-cell-criteria.mjs'

// The cell scenario imports k6 modules, which Node cannot load, so this test reads tests/load/cell.js as text. It keeps the
// numbers of the cell load test in docs/reference/performance.md equal to the numbers and thresholds of the scenario.
const root = fileURLToPath(new URL('..', import.meta.url))
const scenario = readFileSync(`${root}tests/load/cell.js`, 'utf8')
const performance = readFileSync(`${root}docs/reference/performance.md`, 'utf8')

const cellSection = performance.slice(performance.indexOf('### Cell load test'))
const objectivesSection = performance.slice(performance.indexOf('## Objectives'), performance.indexOf('## Reference environment'))

const constant = (name) => {
  const match = new RegExp(`^const ${name} = (\\d+)$`, 'm').exec(scenario)
  assert.ok(match, `cell.js has no integer constant ${name}`)
  return Number(match[1])
}

const thresholds = new Map([...scenario.matchAll(/^\s*'([^']+)': \['([^']+)'\],?$/gm)].map((match) => [match[1], match[2]]))

describe('tests/load/cell.js and the cell load test of performance.md', () => {
  test('the tenants, the warm-up and the run are the numbers of the doc', () => {
    const busy = cellSection.match(/(\d+) busy tenants, each with (\d+) users/)
    const idle = cellSection.match(/([\d,]+) idle tenants/)
    const warmUp = cellSection.match(/warm-up of (\d+) minutes/)
    assert.ok(busy && idle && warmUp, 'performance.md does not state the cell load test numbers')
    assert.match(cellSection, /over one hour/)
    assert.deepEqual(
      {
        busyTenants: constant('busyTenants'),
        usersPerBusyTenant: constant('usersPerBusyTenant'),
        idleTenants: constant('idleTenants'),
        warmUpMinutes: constant('warmUpMinutes'),
        measuredMinutes: constant('measuredMinutes'),
      },
      {
        busyTenants: Number(busy[1]),
        usersPerBusyTenant: Number(busy[2]),
        idleTenants: Number(idle[1].replace(/,/g, '')),
        warmUpMinutes: Number(warmUp[1]),
        measuredMinutes: 60,
      },
    )
  })

  test('the busy scenario runs 50 tenants of 10 users, and the idle scenario one user per idle tenant', () => {
    assert.match(scenario, /busy: \{[^}]*vus: busyTenants \* usersPerBusyTenant,/)
    assert.match(scenario, /idle: \{[^}]*vus: idleTenants,/)
  })

  test('the thresholds carry the objectives and the sign-in p95 of the doc', () => {
    const reads = objectivesSection.match(/^\| Reads\s*\|[^\n]*p95 under (\d+) ms/m)
    const writes = objectivesSection.match(/^\| Writes\s*\|[^\n]*p95 under (\d+) ms/m)
    const errors = objectivesSection.match(/^\| Errors\s*\|[^\n]*Under ([\d.]+)%/m)
    const signIn = cellSection.match(/p95 is under (\d+) s\b/)
    assert.ok(reads && writes && errors && signIn, 'performance.md does not state the objectives or the sign-in p95')

    assert.equal(thresholds.get('http_req_duration{scenario:busy,objective:reads,phase:run}'), `p(95)<${reads[1]}`)
    assert.equal(thresholds.get('http_req_duration{scenario:busy,objective:writes,phase:run}'), `p(95)<${writes[1]}`)
    const rate = thresholds.get('server_errors{scenario:busy,phase:run}')
    assert.ok(rate?.startsWith('rate<'), rate)
    assert.equal((Number(rate.slice('rate<'.length)) * 100).toFixed(4), Number(errors[1]).toFixed(4))
    assert.equal(thresholds.get('idle_sign_in{phase:run}'), `p(95)<${Number(signIn[1]) * 1000}`)
  })

  test('every threshold reads the run phase only, so the warm-up does not count', () => {
    assert.ok(thresholds.size > 0)
    for (const key of thresholds.keys()) assert.match(key, /phase:run/, key)
  })
})

const cellSummary = ({ reads = 241, writes = 412, errors = 0.0002, signIns = 1, signInP95 = 1500 } = {}) => ({
  metrics: {
    'http_req_duration{scenario:busy,objective:reads,phase:run}': { 'p(95)': reads },
    'http_req_duration{scenario:busy,objective:writes,phase:run}': { 'p(95)': writes },
    'server_errors{scenario:busy,phase:run}': { value: errors },
    'idle_sign_ins{phase:run}': { value: signIns },
    'idle_sign_in{phase:run}': { 'p(95)': signInP95 },
  },
})

const usage = ({ cpu = 0.62, memory = 0.55, connections = 0.71 } = {}) => ({ cpu, memory, connections })

const bounds = [0.1, 0.25, 0.5, 1, 2, 3, 4, 5, 6, 7.5, 10, 15, 30, 60, 300, 900]

// Counts of 100 in the bucket with the given index: (0.5, 1] is 3, (5, 6] is 8. An index of -1 gives no claims.
const inBucket = (index) => Array.from({ length: bounds.length + 1 }, (_, i) => (i === index ? 100 : 0))

// One line of the worker metrics file for one tenant, with its claim delay histogram.
const tenantMetrics = (tenant, bucket) =>
  JSON.stringify({
    resourceMetrics: [
      {
        scopeMetrics: [
          {
            metrics: [
              {
                name: 'axis.work_items.claim_delay',
                histogram: {
                  aggregationTemporality: 2,
                  dataPoints: [
                    {
                      attributes: [{ key: 'axis.tenant', value: { stringValue: tenant } }],
                      startTimeUnixNano: '1',
                      timeUnixNano: '2',
                      bucketCounts: inBucket(bucket),
                      explicitBounds: bounds,
                    },
                  ],
                },
              },
            ],
          },
        ],
      },
    ],
  })

// Busy and idle tenants at 0.975 s, and the backlogged tenant. A bucket index sets how slow a tenant is.
const workerMetrics = ({ busy = 3, idle = 3, backlog = 3 } = {}) =>
  [tenantMetrics('busy-1', busy), tenantMetrics('idle-1', idle), tenantMetrics('backlog', backlog)].join('\n')

describe('evaluate of the cell load test', () => {
  test('a run that meets every criterion passes, and each criterion prints its value', () => {
    const { lines, met } = evaluate(cellSummary(), workerMetrics({ backlog: 8 }), usage(), 'backlog')

    assert.equal(met, true)
    assert.deepEqual(lines.slice(0, 8), [
      'Busy reads p95 241 ms, objective under 300 ms: met',
      'Busy writes p95 412 ms, objective under 500 ms: met',
      'Busy server errors 0.02%, objective under 0.1%: met',
      'Idle sign-ins 100.00%, objective 100%: met',
      'Idle sign-in p95 1500 ms, objective under 2 s: met',
      'Cell CPU peak 62.00%, objective under 80%: met',
      'Cell memory peak 55.00%, objective under 80%: met',
      'Cell database connections peak 71.00%, objective under 80%: met',
    ])
    assert.match(lines[8], /^Worker claim p95 0\.9\d s at tenant busy-1, objective under 5 s, backlogged tenant backlog left out: met$/)
  })

  test('a run that misses exactly one criterion fails, and only that criterion ends in missed', () => {
    const cases = [
      ['Busy reads p95 300 ms, objective under 300 ms: missed', { summary: cellSummary({ reads: 300 }) }],
      ['Busy writes p95 500 ms, objective under 500 ms: missed', { summary: cellSummary({ writes: 500 }) }],
      ['Busy server errors 0.10%, objective under 0.1%: missed', { summary: cellSummary({ errors: 0.001 }) }],
      ['Idle sign-ins 99.00%, objective 100%: missed', { summary: cellSummary({ signIns: 0.99 }) }],
      ['Idle sign-in p95 2000 ms, objective under 2 s: missed', { summary: cellSummary({ signInP95: 2000 }) }],
      ['Cell CPU peak 80.00%, objective under 80%: missed', { usage: usage({ cpu: 0.8 }) }],
      ['Cell memory peak 80.00%, objective under 80%: missed', { usage: usage({ memory: 0.8 }) }],
      ['Cell database connections peak 80.00%, objective under 80%: missed', { usage: usage({ connections: 0.8 }) }],
      [
        'Worker claim p95 5.95 s at tenant idle-1, objective under 5 s, backlogged tenant backlog left out: missed',
        { metrics: workerMetrics({ idle: 8 }) },
      ],
    ]
    for (const [line, { summary = cellSummary(), metrics = workerMetrics(), usage: usages = usage() }] of cases) {
      const { lines, met } = evaluate(summary, metrics, usages, 'backlog')

      assert.equal(met, false, line)
      assert.deepEqual(lines.filter((text) => text.endsWith('missed')), [line])
    }
  })

  test('a criterion missing from the summary or the usage is missed, and named', () => {
    const withoutSignIns = cellSummary()
    delete withoutSignIns.metrics['idle_sign_ins{phase:run}']
    const cases = [
      [withoutSignIns, usage(), 'Idle sign-ins missing from the summary: missed'],
      [cellSummary(), { memory: 0.55, connections: 0.71 }, 'Cell CPU peak missing from the usage: missed'],
    ]
    for (const [summary, usageValue, line] of cases) {
      const { lines, met } = evaluate(summary, workerMetrics(), usageValue, 'backlog')

      assert.equal(met, false, line)
      assert.ok(lines.includes(line), lines.join('\n'))
    }
  })

  test('the backlogged tenant with a claim delay p95 of 5 s or more does not fail the run', () => {
    const { met, lines } = evaluate(cellSummary(), workerMetrics({ backlog: 8 }), usage(), 'backlog')

    assert.equal(met, true)
    assert.match(lines.at(-1), /backlogged tenant backlog left out: met$/)
  })

  test('another tenant with a claim delay p95 of 5 s or more fails the run, and the line names it', () => {
    const { met, lines } = evaluate(cellSummary(), workerMetrics({ idle: 8 }), usage(), 'backlog')

    assert.equal(met, false)
    assert.match(lines.at(-1), /^Worker claim p95 5\.95 s at tenant idle-1, .*: missed$/)
  })

  test('a tenant without claims is skipped, and the worker objective is missed only when no other tenant has claims', () => {
    const { met, lines } = evaluate(cellSummary(), [tenantMetrics('quiet', -1), tenantMetrics('backlog', 8)].join('\n'), usage(), 'backlog')

    assert.equal(met, false)
    assert.ok(lines.includes('Worker claim p95 missing from the worker metrics: missed'), lines.join('\n'))
  })
})

describe('findCap', () => {
  test('tries 1,000, then lower by 100, and returns the first count that passes', async () => {
    const tried = []
    const cap = await findCap(async (count) => {
      tried.push(count)
      return count <= 700
    })

    assert.equal(cap, 700)
    assert.deepEqual(tried, [1000, 900, 800, 700])
  })

  test('returns null when no count down to 100 passes', async () => {
    const tried = []
    const cap = await findCap((count) => {
      tried.push(count)
      return false
    })

    assert.equal(cap, null)
    assert.equal(tried.length, 10)
    assert.equal(tried.at(-1), 100)
  })
})

describe('the summary metrics of the cell checker', () => {
  test('every summary metric the checker reads is a threshold of tests/load/cell.js', () => {
    for (const metric of summaryMetrics) assert.ok(thresholds.has(metric), metric)
  })
})
