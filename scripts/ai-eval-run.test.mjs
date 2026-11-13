// Smoke test of scripts/ai-eval.sh, see docs/reference/model-bindings.md#the-authoring-eval: runs the script with
// `--binding standin` against a stand-in Messages API server that this test runs, so no paid model is called. Requires Docker.
// Run from scripts/smoke.sh, and one at a time, because each run starts the eval stack.
//
// The stand-in plays a model that changes nothing. It answers each step of the agent host by the step's system text: a plan for
// the planning step, no tool call for the working step and PASSED for the review. A draft with no change is accepted, so every
// case ends with an accepted draft. In the second run the stand-in also writes a `relax` entry for the point of the refusal case
// `requester-approves-own-small-request`, because a fixed answers file cannot: each new application has a fresh manifest id.
import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { mkdtempSync, readFileSync, rmSync } from 'node:fs'
import http from 'node:http'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { describe, test } from 'node:test'
import { readCases } from './lib/ai-eval-run.mjs'

const timeout = 60 * 60 * 1000
const root = path.resolve(import.meta.dirname, '..')
const usage = { input_tokens: 10, output_tokens: 5 }
const refusalName = 'requester-approves-own-small-request'
const relaxedPoint = 'Request/transitions/approve'

const cases = readCases()
const refusal = cases.find(({ name }) => name === refusalName)

const reply = (content, stopReason) => ({ id: 'msg', type: 'message', role: 'assistant', model: 'standin', content, stop_reason: stopReason, usage })
const say = (text) => reply([{ type: 'text', text }], 'end_turn')

/** The stand-in model. With `relax`, the working step of the refusal case writes a relax entry for the refusal's point. */
function standIn({ relax }) {
  let calls = 0
  const log = []

  // The name of the tool that the last user message answers, or undefined when it holds no tool result.
  const answered = (messages) => {
    const result = messages.at(-1).content.find((block) => block.type === 'tool_result')
    if (result === undefined) return undefined
    const call = messages.flatMap(({ content }) => content).find((block) => block.type === 'tool_use' && block.id === result.tool_use_id)
    return { name: call.name, error: result.is_error, content: result.content }
  }

  const answer = ({ system, messages }) => {
    if (system.startsWith('You plan')) return say('Change nothing.')
    if (system.startsWith('You review')) return say('PASSED')
    if (!system.startsWith('You make')) throw new Error(`An unknown step: ${system.slice(0, 40)}`)

    const application = /'([^']+)'/.exec(system)[1]
    const request = messages[0].content[0].text
    if (!relax || !request.startsWith(`The request:\n${refusal.request}`)) return say('Nothing to change.')
    const tool = (name, input) => reply([{ type: 'tool_use', id: `call-${++calls}`, name, input: { application, ...input } }], 'tool_use')

    // List the resources, read the manifest, write it with the relax entry on the approvals module, and end the turn.
    const last = answered(messages)
    if (last === undefined) return tool('list_resources', {})
    if (last.error) return say(`A tool failed: ${last.content}`)
    const content = JSON.parse(last.content)
    if (last.name === 'list_resources') return tool('read_resource', { id: content.items.find((item) => item.file === 'application.json').id })
    if (last.name === 'read_resource') {
      const manifest = content.content
      const module = manifest.modules.find(({ id }) => id === 'approvals')
      module.relax = [{ point: relaxedPoint, reason: 'Stand-in model of the eval smoke test.' }]
      return tool('write_resource', { content: manifest })
    }
    return say('Done.')
  }

  const server = http.createServer((request, response) => {
    const chunks = []
    request.on('data', (chunk) => chunks.push(chunk))
    request.on('end', () => {
      try {
        assert.equal(`${request.method} ${request.url}`, 'POST /v1/messages')
        const body = answer(JSON.parse(Buffer.concat(chunks).toString()))
        log.push(`${request.url} -> ${body.stop_reason}`)
        response.writeHead(200, { 'Content-Type': 'application/json' })
        response.end(JSON.stringify(body))
      } catch (error) {
        log.push(String(error))
        response.writeHead(500)
        response.end()
      }
    })
  })
  return { server, log }
}

/** Runs scripts/ai-eval.sh --binding standin against the stand-in and resolves with the exit code, the output and the parsed report. */
async function runEval({ relax }) {
  const { server, log } = standIn({ relax })
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve))
  const folder = mkdtempSync(path.join(tmpdir(), 'axis-ai-eval-report-'))
  try {
    const child = spawn('bash', ['scripts/ai-eval.sh', '--binding', 'standin'], {
      cwd: root,
      env: {
        ...process.env,
        Ai__Bindings__standin__Kind: 'anthropic',
        Ai__Bindings__standin__Model: 'standin',
        Ai__Bindings__standin__Endpoint: `http://127.0.0.1:${server.address().port}`,
        AXIS_AI_EVAL_REPORT_DIR: folder,
      },
    })
    let output = ''
    child.stdout.on('data', (chunk) => (output += chunk))
    child.stderr.on('data', (chunk) => (output += chunk))
    // The stand-in serves from this process, so the script runs asynchronously.
    const status = await new Promise((resolve) => child.on('close', resolve))
    let report
    try {
      report = JSON.parse(readFileSync(path.join(folder, 'report.json'), 'utf8'))
    } catch {
      // Left undefined: the assertions on the report fail and print the output.
    }
    return { status, output: `${output}\nThe stand-in answered:\n${log.join('\n')}`, report }
  } finally {
    server.close()
    rmSync(folder, { recursive: true, force: true })
  }
}

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
