// Checks a k6 summary export against the objectives of docs/reference/performance.md#objectives.
// Usage: node scripts/lib/load-objectives.mjs SUMMARY.json
import { readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const ms = (value) => `${Math.round(value)} ms`
const percent = (rate) => `${(rate * 100).toFixed(2)}%`

const objectives = [
  { name: 'Reads p95', metric: 'http_req_duration{objective:reads}', stat: 'p(95)', limit: 300, target: 'under 300 ms', show: ms },
  { name: 'Writes p95', metric: 'http_req_duration{objective:writes}', stat: 'p(95)', limit: 500, target: 'under 500 ms', show: ms },
  { name: 'Server errors', metric: 'server_errors', stat: 'value', limit: 0.001, target: 'under 0.1%', show: percent },
]

export function evaluate(summary) {
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
  return { lines, met }
}

export function main(args) {
  const [file] = args
  if (!file) {
    console.error('Usage: node scripts/lib/load-objectives.mjs SUMMARY.json')
    return 2
  }
  const { lines, met } = evaluate(JSON.parse(readFileSync(file, 'utf8')))
  for (const line of lines) console.log(line)
  return met ? 0 : 1
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(main(process.argv.slice(2)))
}
