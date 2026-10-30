// Checks a k6 summary export and the worker metrics file against the objectives of docs/reference/performance.md#objectives.
// Usage: node scripts/lib/load-objectives.mjs SUMMARY.json WORKER-METRICS.json
import { existsSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const ms = (value) => `${Math.round(value)} ms`
const percent = (rate) => `${(rate * 100).toFixed(2)}%`

const objectives = [
  { name: 'Reads p95', metric: 'http_req_duration{objective:reads}', stat: 'p(95)', limit: 300, target: 'under 300 ms', show: ms },
  { name: 'Writes p95', metric: 'http_req_duration{objective:writes}', stat: 'p(95)', limit: 500, target: 'under 500 ms', show: ms },
  { name: 'Server errors', metric: 'server_errors', stat: 'value', limit: 0.001, target: 'under 0.1%', show: percent },
]

const claimDelayMetric = 'axis.work_items.claim_delay'
const claimDelayLimit = 5
const cumulative = 2

// Each data point of a cumulative series repeats the running total, so only the latest point of a series counts.
export function claimDelayP95(text, tenant = 'load') {
  const cumulativeSeries = new Map()
  const deltaPoints = []
  for (const line of text.split('\n')) {
    if (!line.trim()) continue
    for (const resource of JSON.parse(line).resourceMetrics ?? []) {
      for (const scope of resource.scopeMetrics ?? []) {
        for (const metric of scope.metrics ?? []) {
          if (metric.name !== claimDelayMetric || !metric.histogram) continue
          for (const point of metric.histogram.dataPoints ?? []) {
            const attributes = Object.fromEntries(point.attributes.map((attribute) => [attribute.key, attribute.value.stringValue]))
            if (attributes['axis.tenant'] !== tenant) continue
            if (metric.histogram.aggregationTemporality !== cumulative) {
              deltaPoints.push(point)
              continue
            }
            const key = JSON.stringify([Object.entries(attributes).sort(), point.startTimeUnixNano])
            const kept = cumulativeSeries.get(key)
            if (!kept || BigInt(point.timeUnixNano) > BigInt(kept.timeUnixNano)) cumulativeSeries.set(key, point)
          }
        }
      }
    }
  }

  const points = [...cumulativeSeries.values(), ...deltaPoints]
  if (points.length === 0) return undefined
  const bounds = points[0].explicitBounds.map(Number)
  const counts = new Array(bounds.length + 1).fill(0)
  for (const point of points) {
    point.bucketCounts.forEach((count, index) => {
      counts[index] += Number(count)
    })
  }
  const total = counts.reduce((sum, count) => sum + count, 0)
  if (total === 0) return undefined

  // Like Prometheus' histogram_quantile, the percentile is interpolated linearly inside the bucket that holds it.
  const rank = 0.95 * total
  let index = 0
  let before = 0
  while (before + counts[index] < rank) before += counts[index++]
  const lower = index === 0 ? 0 : bounds[index - 1]
  const upper = bounds[index] ?? bounds.at(-1)
  return lower + ((upper - lower) * (rank - before)) / counts[index]
}

export function evaluate(summary, workerMetrics) {
  const lines = []
  let met = true
  for (const objective of objectives) {
    const value = summary.metrics?.[objective.metric]?.[objective.stat]
    if (typeof value !== 'number') {
      lines.push(`${objective.name} missing from the summary: missed`)
      met = false
      continue
    }
    const objectiveMet = value < objective.limit
    met &&= objectiveMet
    lines.push(`${objective.name} ${objective.show(value)}, objective ${objective.target}: ${objectiveMet ? 'met' : 'missed'}`)
  }

  const claimDelay = claimDelayP95(workerMetrics)
  if (claimDelay === undefined) {
    lines.push('Worker claim p95 missing from the worker metrics: missed')
    met = false
  } else {
    const claimMet = claimDelay < claimDelayLimit
    met &&= claimMet
    lines.push(`Worker claim p95 ${claimDelay.toFixed(2)} s, objective under ${claimDelayLimit} s: ${claimMet ? 'met' : 'missed'}`)
  }
  return { lines, met }
}

export function main(args) {
  const [summaryFile, metricsFile] = args
  if (!summaryFile || !metricsFile) {
    console.error('Usage: node scripts/lib/load-objectives.mjs SUMMARY.json WORKER-METRICS.json')
    return 2
  }
  const workerMetrics = existsSync(metricsFile) ? readFileSync(metricsFile, 'utf8') : ''
  const { lines, met } = evaluate(JSON.parse(readFileSync(summaryFile, 'utf8')), workerMetrics)
  for (const line of lines) console.log(line)
  return met ? 0 : 1
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(main(process.argv.slice(2)))
}
