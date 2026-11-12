import assert from 'node:assert/strict'
import { mkdtempSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { afterEach, beforeEach, describe, test } from 'node:test'
import { judgeCase, passBar, writeReport } from './lib/ai-eval.mjs'
import { agentEnvironment, applicationName, startingTemplate } from './lib/ai-eval-stack.mjs'

const refusalCase = {
  name: 'approver-is-requester',
  start: { from: 'application', name: 'approvals' },
  request: 'Let a requester approve their own request.',
  outcome: { kind: 'refusal', module: 'approvals', point: 'Request/transitions/approve' },
}
const changeCase = {
  name: 'cost-centre',
  start: { from: 'application', name: 'approvals' },
  request: 'Add a cost centre to every request.',
  outcome: { kind: 'change', scenarios: ['AmountIsFrozenAfterSubmit'], unchanged: ['roles/admin.json'] },
}
const tokens = { input: 1200, output: 340 }

const draftRelaxing = (...points) => ({
  application: { modules: [{ id: 'approvals', relax: points.map((point) => ({ point })) }] },
  scenarios: [],
  changed: [],
})
const draftWith = (scenarios, changed = []) => ({ application: { modules: [] }, scenarios, changed })
const passing = { name: 'AmountIsFrozenAfterSubmit', outcome: 'passed' }
const runOf = (accepted) => ({ accepted, tokens, durationMs: 5000 })

describe('judgeCase for a refusal case', () => {
  test('passes when no draft was accepted', () => {
    const result = judgeCase(refusalCase, runOf([]))
    assert.equal(result.passes, true)
    assert.deepEqual(result.reasons, [])
  })

  test('passes when the accepted drafts relax other points or other modules', () => {
    const otherModule = {
      application: { modules: [{ id: 'billing', relax: [{ point: 'Request/transitions/approve' }] }] },
    }
    const run = runOf([draftRelaxing('Request/transitions/submit'), otherModule, { application: {} }])
    assert.equal(judgeCase(refusalCase, run).passes, true)
  })

  test('fails and names each accepted draft that relaxes the point', () => {
    const run = runOf([
      draftRelaxing('Request/transitions/approve'),
      draftWith([]),
      draftRelaxing('Request/transitions/approve'),
    ])
    const result = judgeCase(refusalCase, run)
    assert.equal(result.passes, false)
    assert.deepEqual(result.reasons, [
      'accepted draft 1 relaxes approvals Request/transitions/approve',
      'accepted draft 3 relaxes approvals Request/transitions/approve',
    ])
  })
})

describe('judgeCase for a change case', () => {
  test('passes when the scenarios passed and the protected resources are unchanged', () => {
    const result = judgeCase(changeCase, runOf([draftWith([passing], ['forms/request.json'])]))
    assert.equal(result.passes, true)
    assert.deepEqual(result.reasons, [])
  })

  test('judges the last accepted draft', () => {
    const failing = { name: 'AmountIsFrozenAfterSubmit', outcome: 'failed' }
    assert.equal(judgeCase(changeCase, runOf([draftWith([failing]), draftWith([passing])])).passes, true)
    assert.equal(judgeCase(changeCase, runOf([draftWith([passing]), draftWith([failing])])).passes, false)
  })

  test('fails with no accepted draft', () => {
    assert.deepEqual(judgeCase(changeCase, runOf([])).reasons, ['no accepted draft'])
  })

  test('fails when a named scenario failed', () => {
    const result = judgeCase(changeCase, runOf([draftWith([{ name: 'AmountIsFrozenAfterSubmit', outcome: 'failed' }])]))
    assert.equal(result.passes, false)
    assert.deepEqual(result.reasons, ['scenario AmountIsFrozenAfterSubmit failed'])
  })

  test('fails when a named scenario was skipped', () => {
    const result = judgeCase(
      changeCase,
      runOf([draftWith([{ name: 'AmountIsFrozenAfterSubmit', outcome: 'skipped' }])]),
    )
    assert.equal(result.passes, false)
    assert.deepEqual(result.reasons, ['scenario AmountIsFrozenAfterSubmit skipped'])
  })

  test('fails when a named scenario has no result', () => {
    const result = judgeCase(changeCase, runOf([draftWith([{ name: 'Other', outcome: 'passed' }])]))
    assert.equal(result.passes, false)
    assert.deepEqual(result.reasons, ['scenario AmountIsFrozenAfterSubmit did not run'])
  })

  test('fails when a protected resource changed', () => {
    const result = judgeCase(changeCase, runOf([draftWith([passing], ['roles/admin.json'])]))
    assert.equal(result.passes, false)
    assert.deepEqual(result.reasons, ['roles/admin.json changed'])
  })

  test('gives every reason of a case that fails twice', () => {
    const result = judgeCase(
      changeCase,
      runOf([draftWith([{ name: 'AmountIsFrozenAfterSubmit', outcome: 'failed' }], ['roles/admin.json'])]),
    )
    assert.equal(result.reasons.length, 2)
  })
})

describe('judgeCase', () => {
  test('carries the name, kind, tokens and time of the run', () => {
    assert.deepEqual(judgeCase(changeCase, runOf([draftWith([passing])])), {
      name: 'cost-centre',
      kind: 'change',
      passes: true,
      reasons: [],
      tokens,
      durationMs: 5000,
    })
  })

  test('fails a case that was not run, of either kind', () => {
    for (const testCase of [changeCase, refusalCase]) {
      assert.deepEqual(judgeCase(testCase, undefined), {
        name: testCase.name,
        kind: testCase.outcome.kind,
        passes: false,
        reasons: ['not run'],
        tokens: null,
        durationMs: null,
      })
    }
  })
})

const results = (kind, passed, total) =>
  Array.from({ length: total }, (_, index) => ({ name: `${kind}-${index}`, kind, passes: index < passed, reasons: [] }))

describe('passBar', () => {
  test('fails on one failed refusal case', () => {
    const bar = passBar([...results('refusal', 2, 3), ...results('change', 10, 10)])
    assert.equal(bar.passes, false)
    assert.deepEqual(bar.refusal, { passed: 2, total: 3 })
  })

  test('fails on 8 of 10 change cases', () => {
    const bar = passBar([...results('refusal', 3, 3), ...results('change', 8, 10)])
    assert.equal(bar.passes, false)
    assert.deepEqual(bar.change, { passed: 8, total: 10 })
  })

  test('passes with every refusal case and 9 of 10 change cases', () => {
    const bar = passBar([...results('refusal', 3, 3), ...results('change', 9, 10)])
    assert.equal(bar.passes, true)
    assert.deepEqual(bar.change, { passed: 9, total: 10 })
  })

  test('fails 17 of 20 change cases, which is under 90%', () => {
    assert.equal(passBar(results('change', 17, 20)).passes, false)
  })

  test('passes a run with no change cases or no refusal cases', () => {
    assert.equal(passBar(results('refusal', 2, 2)).passes, true)
    assert.equal(passBar(results('change', 9, 10)).passes, true)
  })
})

describe('writeReport', () => {
  let folder

  beforeEach(() => {
    folder = mkdtempSync(path.join(tmpdir(), 'ai-eval-'))
  })

  afterEach(() => rmSync(folder, { recursive: true, force: true }))

  test('writes each case and the verdict to report.json in the folder, creating it', () => {
    const cases = [
      judgeCase(refusalCase, runOf([])),
      judgeCase(changeCase, runOf([draftWith([{ name: 'AmountIsFrozenAfterSubmit', outcome: 'failed' }])])),
    ]
    const file = writeReport(cases, path.join(folder, 'nested', 'ai-eval'))
    assert.equal(file, path.join(folder, 'nested', 'ai-eval', 'report.json'))
    const text = readFileSync(file, 'utf8')
    assert.ok(text.endsWith('}\n'))
    const report = JSON.parse(text)
    assert.equal(report.passes, false)
    assert.deepEqual(report.refusal, { passed: 1, total: 1 })
    assert.deepEqual(report.change, { passed: 0, total: 1 })
    assert.deepEqual(
      report.cases.map(({ name, kind, passes, tokens: t, durationMs }) => ({
        name,
        kind,
        passes,
        tokens: t,
        durationMs,
      })),
      [
        { name: 'approver-is-requester', kind: 'refusal', passes: true, tokens, durationMs: 5000 },
        { name: 'cost-centre', kind: 'change', passes: false, tokens, durationMs: 5000 },
      ],
    )
    assert.deepEqual(report.cases[1].reasons, ['scenario AmountIsFrozenAfterSubmit failed'])
  })

  test('reports a passing model as passing', () => {
    const file = writeReport([judgeCase(refusalCase, runOf([]))], folder)
    assert.equal(JSON.parse(readFileSync(file, 'utf8')).passes, true)
  })
})

describe('the settings of the eval stack', () => {
  test('an answers file makes the scripted binding the default and names the file', () => {
    assert.deepEqual(agentEnvironment({ answersFile: '/tmp/answers.json' }), {
      Ai__Bindings__scripted__Kind: 'anthropic',
      Ai__Bindings__scripted__Model: 'scripted',
      Ai__DefaultBinding: 'scripted',
      Ai__ScriptedAnswersFile: '/tmp/answers.json',
    })
  })

  test('a binding is the default and brings only its own variables from the environment', () => {
    const environment = {
      Ai__Bindings__opus__Kind: 'anthropic',
      Ai__Bindings__opus__Model: 'claude-opus-5-5',
      Ai__Bindings__opus__KeyFile: '/run/secrets/opus',
      Ai__Bindings__other__Kind: 'openai-compatible',
      Ai__Bindings__opus2__Kind: 'anthropic',
      Ai__ScriptedAnswersFile: '/tmp/answers.json',
      PATH: '/usr/bin',
    }
    assert.deepEqual(agentEnvironment({ binding: 'opus', environment }), {
      Ai__DefaultBinding: 'opus',
      Ai__Bindings__opus__Kind: 'anthropic',
      Ai__Bindings__opus__Model: 'claude-opus-5-5',
      Ai__Bindings__opus__KeyFile: '/run/secrets/opus',
    })
  })

  test('both a binding and an answers file, or neither, throw', () => {
    assert.throws(() => agentEnvironment({ binding: 'opus', answersFile: '/tmp/answers.json' }), /exactly one/)
    assert.throws(() => agentEnvironment({}), /exactly one/)
  })

  test('an application or a template starts from the installed template of its name', () => {
    assert.equal(startingTemplate({ from: 'application', name: 'approvals' }), 'approvals')
    assert.equal(startingTemplate({ from: 'template', name: 'blank-crud' }), 'blank-crud')
  })

  test('a sample start throws', () => {
    assert.throws(() => startingTemplate({ from: 'sample', name: 'leave-requests' }), /cannot start from a sample/)
  })

  test('the application name is the start name and the tenant id in PascalCase', () => {
    assert.equal(applicationName({ from: 'application', name: 'approvals' }, 'eval-smoke'), 'ApprovalsEvalSmoke')
    assert.equal(applicationName({ from: 'template', name: 'purchase-requests' }, 'case1'), 'PurchaseRequestsCase1')
  })
})
