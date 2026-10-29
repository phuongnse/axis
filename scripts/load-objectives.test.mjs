import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { evaluate } from './lib/load-objectives.mjs'

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

describe('evaluate', () => {
  test('a summary under all three objectives is met and prints each with its value', () => {
    const { lines, met } = evaluate(summary())

    assert.equal(met, true)
    assert.deepEqual(lines, [
      'Reads p95 241 ms, objective under 300 ms: met',
      'Writes p95 412 ms, objective under 500 ms: met',
      'Server errors 0.02%, objective under 0.1%: met',
    ])
  })

  test('a read p95 of 300 ms or more, a write p95 of 500 ms or more, or a 5xx rate of 0.1% or more is missed, and named', () => {
    const cases = [
      [{ reads: 300 }, 'Reads p95 300 ms, objective under 300 ms: missed'],
      [{ writes: 500 }, 'Writes p95 500 ms, objective under 500 ms: missed'],
      [{ errors: 0.001 }, 'Server errors 0.10%, objective under 0.1%: missed'],
    ]
    for (const [overrides, line] of cases) {
      const { lines, met } = evaluate(summary(overrides))

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
      const { lines, met } = evaluate(without(metric))

      assert.equal(met, false, line)
      assert.ok(lines.includes(line), lines.join('\n'))
    }
  })
})

describe('the command line', () => {
  const runCli = (summaryJson) => {
    const dir = mkdtempSync(join(tmpdir(), 'load-objectives-'))
    try {
      const file = join(dir, 'summary.json')
      writeFileSync(file, JSON.stringify(summaryJson))
      return spawnSync('node', [cli, file], { encoding: 'utf8' })
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  }

  test('exits 0 when every objective is met', () => {
    const result = runCli(summary())

    assert.equal(result.status, 0, result.stderr)
    assert.match(result.stdout, /Reads p95 241 ms, objective under 300 ms: met/)
  })

  test('exits 1 and names the missed objective', () => {
    const result = runCli(summary({ reads: 400 }))

    assert.equal(result.status, 1, result.stderr)
    assert.match(result.stdout, /Reads p95 400 ms, objective under 300 ms: missed/)
  })
})
