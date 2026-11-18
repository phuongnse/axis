// Smoke test of scripts/ai-eval.sh, see docs/reference/model-bindings.md#the-authoring-eval: runs the script with
// `--binding standin` against a stand-in Messages API server that this test runs, so no paid model is called. Requires Docker.
// Run by the `ai-eval-pass` smoke test from scripts/smoke.sh, after scripts/ai-eval-stack.test.mjs, because each run starts the eval stack.
// The stand-in is in scripts/lib/ai-eval-standin.mjs. The refusal run is in scripts/ai-eval-refusal.test.mjs.
import assert from 'node:assert/strict'
import { describe, test } from 'node:test'
import { cases, runEval } from './lib/ai-eval-standin.mjs'

const timeout = 60 * 60 * 1000

describe('scripts/ai-eval.sh --binding', () => {
  test('a model that passes every case exits 0 and the report passes each case with its tokens and time', { timeout }, async () => {
    const { status, output, report } = await runEval({ relax: false })

    assert.equal(status, 0, output)
    assert.equal(report?.passes, true, output)
    assert.equal(report.cases.length, cases.length, output)
    for (const result of report.cases) {
      assert.equal(result.passes, true, `${result.name}: ${result.reasons.join('; ')}\n${output}`)
      assert.ok(result.tokens > 0, `${result.name} tokens: ${result.tokens}`)
      assert.ok(result.durationMs > 0, `${result.name} durationMs: ${result.durationMs}`)
    }
  })
})
