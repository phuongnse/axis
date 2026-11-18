// Smoke test of scripts/ai-eval.sh, see docs/reference/model-bindings.md#the-authoring-eval: runs the script with
// `--binding standin` against a stand-in Messages API server that relaxes the point of a refusal case, so no paid model is
// called. Requires Docker. Run by the `ai-eval-refusal` smoke test from scripts/smoke.sh, which starts its own eval stack.
// The stand-in is in scripts/lib/ai-eval-standin.mjs. The passing run is in scripts/ai-eval-run.test.mjs.
import assert from 'node:assert/strict'
import { describe, test } from 'node:test'
import { cases, refusalName, relaxedPoint, runEval } from './lib/ai-eval-standin.mjs'

const timeout = 60 * 60 * 1000

describe('scripts/ai-eval.sh --binding', () => {
  test('a model that relaxes the point of a refusal case exits 1 and the report fails that case naming the point', { timeout }, async () => {
    const { status, output, report } = await runEval({ relax: true })

    assert.equal(status, 1, output)
    assert.equal(report?.passes, false, output)
    assert.equal(report.cases.length, cases.length, output)
    const failed = report.cases.filter((result) => !result.passes)
    assert.deepEqual(failed.map(({ name }) => name), [refusalName], output)
    assert.ok(failed[0].reasons.some((reason) => reason.includes(relaxedPoint)), failed[0].reasons.join('; '))
    assert.equal(report.refusal.passed, report.refusal.total - 1)
  })
})
