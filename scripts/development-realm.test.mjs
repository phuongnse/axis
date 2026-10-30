import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { createServer } from 'node:http'
import { after, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// Runs scripts/lib/development-realm.mjs against a local server that plays the admin API of the identity provider for
// the realm axis. The server keeps the flows and the steps of the flow in memory and records every request.
const root = fileURLToPath(new URL('..', import.meta.url))
const alias = 'axis first login'
const flowPath = `/admin/realms/axis/authentication/flows/${encodeURIComponent(alias)}/executions`

const startKeycloak = async ({ flows, executions, fail }) => {
  const state = { flows: [...flows], executions: [...executions], requests: [] }
  let nextId = 1
  const server = createServer((request, response) => {
    let text = ''
    request.on('data', (chunk) => (text += chunk))
    request.on('end', () => {
      const body = text === '' ? undefined : request.headers['content-type'] === 'application/json' ? JSON.parse(text) : text
      const { method, url } = request
      state.requests.push({ method, url, body, authorization: request.headers.authorization })
      const reply = (status, value) => {
        response.writeHead(status, { 'Content-Type': 'application/json' })
        response.end(value === undefined ? '' : JSON.stringify(value))
      }
      if (fail && fail.method === method && fail.url === url) return reply(fail.status, { error: 'failed' })
      if (method === 'POST' && url === '/realms/master/protocol/openid-connect/token') return reply(200, { access_token: 'token' })
      if (method === 'GET' && url === '/admin/realms/axis/authentication/flows') return reply(200, state.flows)
      if (method === 'POST' && url === '/admin/realms/axis/authentication/flows') {
        state.flows.push(body)
        return reply(201)
      }
      if (method === 'GET' && url === flowPath) return reply(200, state.executions)
      if (method === 'POST' && url === `${flowPath}/execution`) {
        state.executions.push({ id: `new-${nextId++}`, providerId: body.provider, requirement: 'DISABLED' })
        return reply(201)
      }
      if (method === 'PUT' && url === flowPath) {
        state.executions = state.executions.map((execution) => (execution.id === body.id ? body : execution))
        return reply(204)
      }
      const deleted = url.match(/^\/admin\/realms\/axis\/authentication\/executions\/(.+)$/)
      if (method === 'DELETE' && deleted) {
        state.executions = state.executions.filter((execution) => execution.id !== deleted[1])
        return reply(204)
      }
      reply(404, { error: `unexpected ${method} ${url}` })
    })
  })
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve))
  return { state, url: `http://127.0.0.1:${server.address().port}`, server }
}

const servers = []
const keycloak = async (options) => {
  const started = await startKeycloak(options)
  servers.push(started.server)
  return started
}
after(() => servers.forEach((server) => server.close()))

// The script runs as a child process, so that the server above keeps answering while it runs.
const run = (url) =>
  new Promise((resolve) => {
    const child = spawn(process.execPath, ['scripts/lib/development-realm.mjs', ...(url ? [url] : [])], { cwd: root })
    let stdout = ''
    let stderr = ''
    child.stdout.on('data', (chunk) => (stdout += chunk))
    child.stderr.on('data', (chunk) => (stderr += chunk))
    child.on('close', (status) => resolve({ status, stdout, stderr }))
  })

const summary = (requests) => requests.map(({ method, url }) => `${method} ${url}`)
const changes = (requests) => requests.filter(({ method }) => method !== 'GET')

describe('scripts/lib/development-realm.mjs adds the axis first login flow to the axis realm', () => {
  test('it creates the flow and a required create-user step, and deletes any other step', async () => {
    // The flow does not exist yet, and the server gives it a step that the script must remove.
    const { state, url } = await keycloak({
      flows: [{ alias: 'browser' }],
      executions: [{ id: 'extra', providerId: 'idp-review-profile', requirement: 'REQUIRED' }],
    })

    const result = await run(url)

    assert.equal(result.status, 0, result.stdout + result.stderr)
    assert.deepEqual(summary(changes(state.requests)), [
      'POST /realms/master/protocol/openid-connect/token',
      'POST /admin/realms/axis/authentication/flows',
      `POST ${flowPath}/execution`,
      'DELETE /admin/realms/axis/authentication/executions/extra',
      `PUT ${flowPath}`,
    ])
    const created = changes(state.requests)[1].body
    assert.deepEqual(
      { providerId: created.providerId, topLevel: created.topLevel, builtIn: created.builtIn, alias: created.alias },
      { providerId: 'basic-flow', topLevel: true, builtIn: false, alias },
    )
    assert.deepEqual(changes(state.requests)[2].body, { provider: 'idp-create-user-if-unique' })
    assert.equal(changes(state.requests)[4].body.requirement, 'REQUIRED')
    assert.deepEqual(
      state.executions.map(({ providerId, requirement }) => ({ providerId, requirement })),
      [{ providerId: 'idp-create-user-if-unique', requirement: 'REQUIRED' }],
    )
    assert.ok(state.requests.slice(1).every((request) => request.authorization === 'Bearer token'))
  })

  test('it sends no change when the flow already has only the required create-user step', async () => {
    const { state, url } = await keycloak({
      flows: [{ alias }],
      executions: [{ id: 'create', providerId: 'idp-create-user-if-unique', requirement: 'REQUIRED' }],
    })

    const result = await run(url)

    assert.equal(result.status, 0, result.stdout + result.stderr)
    assert.deepEqual(summary(changes(state.requests)), ['POST /realms/master/protocol/openid-connect/token'])
    // The token, the flows and the steps of the flow.
    assert.equal(state.requests.length, 3)
  })

  test('it exits non-zero and names the step and the status when an admin call fails', async () => {
    const { state, url } = await keycloak({
      flows: [],
      executions: [],
      fail: { method: 'GET', url: '/admin/realms/axis/authentication/flows', status: 500 },
    })

    const result = await run(url)

    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /list the authentication flows failed with HTTP 500/)
    assert.deepEqual(summary(changes(state.requests)), ['POST /realms/master/protocol/openid-connect/token'])
  })

  test('it prints its usage and exits with 2 without a Keycloak address', async () => {
    const result = await run()

    assert.equal(result.status, 2)
    assert.match(result.stderr, /Usage: node scripts\/lib\/development-realm\.mjs <keycloak url>/)
  })
})
