// Smoke test for the authoring eval's local stack, see docs/reference/model-bindings.md#the-authoring-eval: starts
// PostgreSQL and Keycloak in Docker and the built server, publisher and agent host, with the agent host replaying
// scripted answers, so no model provider is called. It creates a tenant from the built-in approvals application. As the
// tenant's signed-in author it reads the application's authoring requests, which answers 200, and without the session,
// which answers 401. It sends an authoring request and sees it reach its plan, which the agent host makes through the
// MCP server at `https://<host>/mcp`. It also sees the MCP server answer 401 over https to a call without a token.
// The stack builds the hosts, the Keycloak theme, the modules and the templates it runs.
// Requires Docker. Run from scripts/smoke.sh.
import assert from 'node:assert/strict'
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { after, before, describe, test } from 'node:test'
import { applicationName, createTenant, startStack } from './lib/ai-eval-stack.mjs'

const timeout = 30 * 60 * 1000

const start = { from: 'application', name: 'approvals' }
const plan = 'Add a cost centre field to the request.'
const usage = { input_tokens: 1, output_tokens: 1 }

// The answers of the agent host, shaped as Messages API responses: it lists the application's resources and then gives its plan.
const answers = [
  {
    content: [{ type: 'tool_use', id: 'call-1', name: 'list_resources', input: { application: applicationName(start, 'eval-smoke') } }],
    stop_reason: 'tool_use',
    usage,
  },
  { content: [{ type: 'text', text: plan }], stop_reason: 'end_turn', usage },
]

describe('the authoring eval stack', () => {
  let folder
  let stack
  let tenant

  before(
    async () => {
      folder = mkdtempSync(join(tmpdir(), 'axis-ai-eval-answers-'))
      const answersFile = join(folder, 'answers.json')
      writeFileSync(answersFile, JSON.stringify(answers))
      stack = await startStack({ answersFile })
      try {
        tenant = await createTenant(stack, { id: 'eval-smoke', start })
      } catch (error) {
        console.error(stack.logs())
        throw error
      }
    },
    { timeout },
  )

  after(async () => {
    await stack?.stop()
    if (folder) rmSync(folder, { recursive: true, force: true })
  })

  test('the agent host is ready', { timeout }, async () => {
    try {
      const response = await fetch(`${stack.agentHealthUrl}/health/ready`)
      assert.equal(response.status, 200)
    } catch (error) {
      console.error(stack?.logs())
      throw error
    }
  })

  test('the author reads the authoring requests and a call without the session is refused', { timeout }, async () => {
    try {
      const path = `/api/studio/apps/${tenant.application}/authoring-requests`

      const allowed = await tenant.request(path)
      assert.equal(allowed.status, 200, allowed.body)
      assert.ok(Array.isArray(JSON.parse(allowed.body).items), `a list of requests: ${allowed.body}`)

      const denied = await stack.request(tenant.host, path)
      assert.equal(denied.status, 401, denied.body)
    } catch (error) {
      console.error(stack?.logs())
      throw error
    }
  })

  test('an authoring request reaches its plan through the agent host and the MCP server', { timeout }, async () => {
    try {
      const path = `/api/studio/apps/${tenant.application}/authoring-requests`
      const sent = await tenant.request(path, { method: 'POST', body: { text: 'Add a cost centre to every request.' } })
      assert.equal(sent.status, 202, sent.body)
      const { id } = JSON.parse(sent.body)

      // The request stays in planning until the agent host has called the MCP server and the model has answered.
      let current
      for (let i = 0; i < 120; i++) {
        if (i > 0) await new Promise((resolve) => setTimeout(resolve, 1000))
        const read = await tenant.request(`${path}/${id}`)
        assert.equal(read.status, 200, read.body)
        current = JSON.parse(read.body)
        if (current.state !== 'planning') break
      }
      assert.equal(current.state, 'awaitingApproval', JSON.stringify(current))
      assert.equal(current.plan, plan)
    } catch (error) {
      console.error(stack?.logs())
      throw error
    }
  })

  test('the MCP server over https refuses a call without a token', { timeout }, async () => {
    const response = await stack.secureRequest(tenant.host, '/mcp', {
      method: 'POST',
      headers: { 'content-type': 'application/json', accept: 'application/json, text/event-stream' },
      body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'initialize', params: {} }),
    })
    assert.equal(response.status, 401, response.body)
  })
})
