import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { claimDelayP95, evaluate } from './lib/load-objectives.mjs'

const cli = fileURLToPath(new URL('lib/load-objectives.mjs', import.meta.url))

const summary = ({ reads = 241, writes = 412, errors = 0.0002 } = {}) => ({
  metrics: {
    'http_req_duration{objective:reads}': { 'p(95)': reads },
    'http_req_duration{objective:writes}': { 'p(95)': writes },
    server_errors: { value: errors },
  },
})

const without = (metric) => {
  const built = summary()
  delete built.metrics[metric]
  return built
}

const bounds = [0.1, 0.25, 0.5, 1, 2, 3, 4, 5, 6, 7.5, 10, 15, 30, 60, 300, 900]

// Counts of 100 in the bucket with the given index: (0.5, 1] is 3, (5, 6] is 8.
const inBucket = (index, count = 100) => Array.from({ length: bounds.length + 1 }, (_, i) => (i === index ? count : 0))

// One line of the worker metrics file, an OTLP JSON document with a claim delay histogram.
const workerMetrics = ({
  buckets = inBucket(3),
  tenant = 'load',
  kind = 'process-step',
  time = '2',
  temporality = 2,
  name = 'axis.work_items.claim_delay',
} = {}) =>
  JSON.stringify({
    resourceMetrics: [
      {
        scopeMetrics: [
          {
            metrics: [
              {
                name,
                histogram: {
                  aggregationTemporality: temporality,
                  dataPoints: [
                    {
                      attributes: [
                        { key: 'axis.tenant', value: { stringValue: tenant } },
                        { key: 'axis.work_item.kind', value: { stringValue: kind } },
                      ],
                      startTimeUnixNano: '1',
                      timeUnixNano: time,
                      count: String(buckets.reduce((sum, count) => sum + count, 0)),
                      sum: 0,
                      bucketCounts: buckets.map(String),
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

describe('evaluate', () => {
  test('a summary under all three objectives is met and prints each with its value', () => {
    const { lines, met } = evaluate(summary(), workerMetrics())

    assert.equal(met, true)
    assert.equal(lines.length, 4)
    assert.deepEqual(lines.slice(0, 3), [
      'Reads p95 241 ms, objective under 300 ms: met',
      'Writes p95 412 ms, objective under 500 ms: met',
      'Server errors 0.02%, objective under 0.1%: met',
    ])
    assert.match(lines[3], /^Worker claim p95 0\.9\d s, objective under 5 s: met$/)
  })

  test('a read p95 of 300 ms or more, a write p95 of 500 ms or more, or a 5xx rate of 0.1% or more is missed, and named', () => {
    const cases = [
      [{ reads: 300 }, 'Reads p95 300 ms, objective under 300 ms: missed'],
      [{ writes: 500 }, 'Writes p95 500 ms, objective under 500 ms: missed'],
      [{ errors: 0.001 }, 'Server errors 0.10%, objective under 0.1%: missed'],
    ]
    for (const [overrides, line] of cases) {
      const { lines, met } = evaluate(summary(overrides), workerMetrics())

      assert.equal(met, false, line)
      assert.equal(lines.filter((text) => text.endsWith('missed')).length, 1, line)
      assert.ok(lines.includes(line), lines.join('\n'))
    }
  })

  test('a metric missing from the summary is missed, and named', () => {
    const cases = [
      ['http_req_duration{objective:reads}', 'Reads p95 missing from the summary: missed'],
      ['http_req_duration{objective:writes}', 'Writes p95 missing from the summary: missed'],
      ['server_errors', 'Server errors missing from the summary: missed'],
    ]
    for (const [metric, line] of cases) {
      const { lines, met } = evaluate(without(metric), workerMetrics())

      assert.equal(met, false, line)
      assert.ok(lines.includes(line), lines.join('\n'))
    }
  })

  test('a claim delay p95 of 5 s or more is missed, and named', () => {
    const { lines, met } = evaluate(summary(), workerMetrics({ buckets: inBucket(8) }))

    assert.equal(met, false)
    assert.ok(lines.includes('Worker claim p95 5.95 s, objective under 5 s: missed'), lines.join('\n'))
  })

  test('a worker metrics file without the claim delay of the tenant load is missed, and named', () => {
    const cases = [
      ['', 'no metrics'],
      [workerMetrics({ name: 'axis.work_items.other' }), 'only another metric'],
      [workerMetrics({ tenant: 'other' }), 'only tenant other'],
    ]
    for (const [text, why] of cases) {
      const { lines, met } = evaluate(summary(), text)

      assert.equal(met, false, why)
      assert.ok(lines.includes('Worker claim p95 missing from the worker metrics: missed'), `${why}\n${lines.join('\n')}`)
    }
  })
})

describe('claimDelayP95', () => {
  test('interpolates inside the bucket that holds the 95th percentile', () => {
    assert.equal(claimDelayP95(workerMetrics({ buckets: inBucket(8) })), 5 + 0.95)
  })

  test('a delay above the last bound counts as that bound', () => {
    assert.equal(claimDelayP95(workerMetrics({ buckets: inBucket(bounds.length) })), 900)
  })

  test('cumulative data points of one series count once, the latest one', () => {
    const earlier = workerMetrics({ buckets: inBucket(8), time: '2' })
    const later = workerMetrics({ buckets: inBucket(3), time: '3' })

    assert.equal(claimDelayP95([later, earlier].join('\n')), claimDelayP95(later))
  })

  test('delta data points are summed', () => {
    const first = workerMetrics({ buckets: inBucket(8), time: '2', temporality: 1 })
    const second = workerMetrics({ buckets: inBucket(3), time: '3', temporality: 1 })

    assert.ok(Math.abs(claimDelayP95([first, second].join('\n')) - 5.9) < 1e-9)
  })

  test('the bucket counts of several kinds add up', () => {
    const process = workerMetrics({ buckets: inBucket(3), kind: 'process-step' })
    const review = workerMetrics({ buckets: inBucket(8), kind: 'review-task' })

    assert.ok(Math.abs(claimDelayP95([process, review].join('\n')) - 5.9) < 1e-9)
  })
})

describe('the command line', () => {
  const runCli = (summaryJson, metrics = workerMetrics()) => {
    const dir = mkdtempSync(join(tmpdir(), 'load-objectives-'))
    try {
      const file = join(dir, 'summary.json')
      const metricsFile = join(dir, 'worker-metrics.json')
      writeFileSync(file, JSON.stringify(summaryJson))
      if (metrics !== null) writeFileSync(metricsFile, metrics)
      return spawnSync('node', [cli, file, metricsFile], { encoding: 'utf8' })
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  }

  test('exits 0 when every objective is met', () => {
    const result = runCli(summary())

    assert.equal(result.status, 0, result.stderr)
    assert.match(result.stdout, /Reads p95 241 ms, objective under 300 ms: met/)
    assert.match(result.stdout, /Worker claim p95 \d\.\d{2} s, objective under 5 s: met/)
  })

  test('exits 1 and names the missed objective', () => {
    const result = runCli(summary({ reads: 400 }))

    assert.equal(result.status, 1, result.stderr)
    assert.match(result.stdout, /Reads p95 400 ms, objective under 300 ms: missed/)
  })

  test('exits 1 and names the worker objective when the claim delay p95 is 5 s or more', () => {
    const result = runCli(summary(), workerMetrics({ buckets: inBucket(8) }))

    assert.equal(result.status, 1, result.stderr)
    assert.match(result.stdout, /Worker claim p95 5\.95 s, objective under 5 s: missed/)
  })

  test('exits 1 and names the missing worker metrics when the metrics file does not exist', () => {
    const result = runCli(summary(), null)

    assert.equal(result.status, 1, result.stderr)
    assert.match(result.stdout, /Worker claim p95 missing from the worker metrics: missed/)
  })
})
