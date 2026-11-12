// Smoke test for the authoring eval's local stack, see docs/reference/model-bindings.md#the-authoring-eval: starts
// PostgreSQL and Keycloak in Docker and the built server, publisher and agent host, with the agent host replaying
// one scripted answer, so no model provider is called. It then creates a tenant from the built-in approvals application
// and reads the application's authoring requests as the tenant's signed-in author, which answers 200, and without the
// session, which answers 401. The stack builds the hosts, the Keycloak theme, the modules and the templates it runs.
// Requires Docker. Run from scripts/smoke.sh.
import assert from 'node:assert/strict'
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { after, before, describe, test } from 'node:test'
import { createTenant, startStack } from './lib/ai-eval-stack.mjs'

const timeout = 30 * 60 * 1000

// One answer, shaped as a Messages API response. The test sends no request through the agent host, so none is replayed.
const answer = { content: [{ type: 'text', text: 'Nothing to change.' }], stop_reason: 'end_turn', usage: { input_tokens: 1, output_tokens: 1 } }

describe('the authoring eval stack', () => {
  let folder
  let stack

  before(
    async () => {
      folder = mkdtempSync(join(tmpdir(), 'axis-ai-eval-answers-'))
      const answersFile = join(folder, 'answers.json')
      writeFileSync(answersFile, JSON.stringify([answer]))
      stack = await startStack({ answersFile })
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

  test('a new tenant has its starting application and its author reads the authoring requests', { timeout }, async () => {
    try {
      const tenant = await createTenant(stack, { id: 'eval-smoke', start: { from: 'application', name: 'approvals' } })
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
})
