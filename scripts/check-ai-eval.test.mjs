import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { afterEach, beforeEach, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { checkAiEval, defaultFolder } from './check-ai-eval.mjs'

const script = fileURLToPath(new URL('./check-ai-eval.mjs', import.meta.url))
const start = { from: 'application', name: 'approvals' }
const changeCase = {
  start,
  request: 'Add a cost centre to every request.',
  outcome: { kind: 'change', scenarios: ['AmountIsFrozenAfterSubmit'], unchanged: ['roles/admin.json'] },
}
const refusalCase = {
  start,
  request: 'Let a requester approve their own request.',
  outcome: { kind: 'refusal', module: 'approvals', point: 'Request/transitions/approve' },
}

let folder

function write(file, content) {
  writeFileSync(path.join(folder, file), typeof content === 'string' ? content : JSON.stringify(content))
}

const check = () => checkAiEval(folder)

beforeEach(() => {
  folder = mkdtempSync(path.join(tmpdir(), 'check-ai-eval-'))
  mkdirSync(path.join(folder, 'cases'))
  write('supported-models.json', { models: [] })
})

afterEach(() => rmSync(folder, { recursive: true, force: true }))

describe('checkAiEval', () => {
  test('passes a change case, a refusal case and an empty models list', () => {
    write('cases/change.json', changeCase)
    write('cases/refusal.json', refusalCase)
    assert.deepEqual(check(), [])
  })

  test('names the file of a case without an outcome', () => {
    write('cases/no-outcome.json', { start, request: 'Add a field.' })
    const problems = check()
    assert.equal(problems.length, 1)
    assert.match(problems[0], /^cases\/no-outcome\.json: .*outcome/)
  })

  test('names the missing point of a refusal case', () => {
    write('cases/no-point.json', { ...refusalCase, outcome: { kind: 'refusal', module: 'approvals' } })
    const problems = check()
    assert.equal(problems.length, 1)
    assert.match(problems[0], /^cases\/no-point\.json: \/outcome .*'point'/)
  })

  test('rejects a change case without scenarios', () => {
    write('cases/no-scenarios.json', { ...changeCase, outcome: { kind: 'change' } })
    assert.match(check().join('\n'), /cases\/no-scenarios\.json: .*scenarios/)
  })

  test('rejects a point that is not written as a contract point', () => {
    write('cases/bad-point.json', { ...refusalCase, outcome: { ...refusalCase.outcome, point: 'approve' } })
    assert.match(check().join('\n'), /cases\/bad-point\.json: \/outcome\/point .*pattern/)
  })

  test('rejects a file in cases that is not JSON', () => {
    write('cases/broken.json', '{ not json')
    write('cases/notes.md', 'notes')
    const problems = check()
    assert.match(problems.join('\n'), /cases\/broken\.json: is not JSON/)
    assert.match(problems.join('\n'), /cases\/notes\.md: is not a \.json file/)
  })

  test('reports a missing cases folder and a missing models list', () => {
    rmSync(path.join(folder, 'cases'), { recursive: true })
    rmSync(path.join(folder, 'supported-models.json'))
    assert.deepEqual(check(), ['cases: the folder is missing', 'supported-models.json: is missing'])
  })

  test('rejects a supported models entry without its date', () => {
    write('supported-models.json', {
      models: [{ kind: 'anthropic', model: 'claude-sonnet-5-5', axisVersion: '1.0.0' }],
    })
    assert.match(check().join('\n'), /supported-models\.json: \/models\/0 .*'date'/)
  })

  test('accepts a supported models entry with every field', () => {
    write('supported-models.json', {
      models: [{ kind: 'openai-compatible', model: 'llama', date: '2026-11-12', axisVersion: '1.2.3' }],
    })
    assert.deepEqual(check(), [])
  })
})

describe('the committed authoring eval', () => {
  test('has no problems and keeps the supported models list empty', () => {
    assert.deepEqual(checkAiEval(defaultFolder), [])
    assert.deepEqual(JSON.parse(readFileSync(path.join(defaultFolder, 'supported-models.json'), 'utf8')).models, [])
  })

  test('has a change case and a refusal case on the built-in approvals application', () => {
    const casesFolder = path.join(defaultFolder, 'cases')
    const cases = readdirSync(casesFolder).map((file) => JSON.parse(readFileSync(path.join(casesFolder, file), 'utf8')))
    for (const kind of ['change', 'refusal']) {
      assert.ok(
        cases.some((c) => c.outcome.kind === kind && c.start.from === 'application' && c.start.name === 'approvals'),
        `no ${kind} case starts from the approvals application`,
      )
    }
  })
})

describe('the script', () => {
  test('prints a problem on stderr and exits 1', () => {
    write('cases/no-outcome.json', { start, request: 'Add a field.' })
    const result = spawnSync(process.execPath, [script, folder], { encoding: 'utf8' })
    assert.equal(result.status, 1)
    assert.match(result.stderr, /cases\/no-outcome\.json/)
  })
})
