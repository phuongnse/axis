// The stand-in Messages API server of the authoring eval's smoke tests, see docs/reference/model-bindings.md#the-authoring-eval.
// scripts/ai-eval-run.test.mjs and scripts/ai-eval-refusal.test.mjs both run scripts/ai-eval.sh --binding standin against it,
// so no paid model is called. Each run starts the eval stack, so the two tests run one at a time.
//
// The stand-in plays a model that changes nothing. It answers each step of the agent host by the step's system text: a plan, as a
// `propose_plan` call, for the planning step, no tool call for the working step and PASSED for the review. A draft with no change is accepted, so every
// case ends with an accepted draft. In the relaxing run the stand-in also writes a `relax` entry for the point of the refusal case
// `requester-approves-own-small-request`, because a fixed answers file cannot: each new application has a fresh manifest id.
import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { mkdtempSync, readFileSync, rmSync } from 'node:fs'
import http from 'node:http'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { readCases } from './ai-eval-run.mjs'
import { planAnswer } from './ai-eval-stack.mjs'

const root = path.resolve(import.meta.dirname, '..', '..')
const usage = { input_tokens: 10, output_tokens: 5 }

export const refusalName = 'requester-approves-own-small-request'
export const relaxedPoint = 'Request/transitions/approve'
export const cases = readCases()
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
    if (system.startsWith('You plan')) return planAnswer(/'([^']+)'/.exec(system)[1], 'Change nothing.', usage)
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
export async function runEval({ relax }) {
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
