import assert from 'node:assert/strict'
import { createServer } from 'node:http'
import { after, describe, test } from 'node:test'

// Runs scripts/lib/ai-eval-case.mjs against a local server that plays the authoring request, publish request, revision and
// resource routes of one application. It keeps the state of the request in memory and records every call. The clock is
// fake, so the duration is computed from it and never from real time.
import { runCase } from './lib/ai-eval-case.mjs'

const cookie = 'axis=session'
const apps = '/api/studio/apps/leave%20requests'
const manifest = { name: 'leave requests', modules: [{ id: 'approvals' }] }
const sha = 'a'.repeat(40)

const startAxis = async ({ endState, sendStatus = 202, publishStatus = 'succeeded' }) => {
  const calls = []
  let state = 'planning'
  const server = createServer((request, response) => {
    let text = ''
    request.on('data', (chunk) => (text += chunk))
    request.on('end', () => {
      const { method, url } = request
      calls.push({
        method,
        url,
        cookie: request.headers.cookie,
        csrf: request.headers['x-axis-csrf'],
        body: text === '' ? undefined : JSON.parse(text),
      })
      const reply = (status, value) => {
        response.writeHead(status, { 'Content-Type': 'application/json' })
        response.end(value === undefined ? '' : JSON.stringify(value))
      }
      if (method === 'POST' && url === `${apps}/authoring-requests`) {
        if (sendStatus !== 202) return reply(sendStatus, { title: 'refused' })
        state = 'awaitingApproval'
        return reply(202, { id: 'r1', state: 'planning' })
      }
      if (method === 'GET' && url === `${apps}/authoring-requests/r1`)
        return reply(200, { id: 'r1', state, tokens: 1540 })
      if (method === 'POST' && url === `${apps}/authoring-requests/r1/approve`) {
        state = endState
        return reply(200, { id: 'r1', state: 'working' })
      }
      if (method === 'POST' && url === `${apps}/authoring-requests/r1/accept`)
        return reply(202, { id: 'p1', status: 'queued' })
      if (method === 'GET' && url === `${apps}/requests/p1`) {
        return reply(200, {
          id: 'p1',
          status: publishStatus,
          scenarios: [
            {
              name: 'AmountIsFrozenAfterSubmit',
              outcome: 'passed',
              resourceId: 'x',
            },
            { name: 'ManagerApproves', outcome: 'skipped', detail: 'no data' },
          ],
        })
      }
      if (method === 'GET' && url === `${apps}/revisions`) {
        return reply(200, {
          items: [
            { id: 'b'.repeat(40), request: null },
            { id: sha, request: 'p1' },
          ],
        })
      }
      if (method === 'GET' && url === `${apps}/revisions/${sha}`) {
        return reply(200, {
          revision: { id: sha },
          resources: [{ file: 'entities/leave.json' }, { file: 'roles/manager.json' }],
        })
      }
      if (method === 'GET' && url === `${apps}/resources`) {
        return reply(200, {
          items: [
            { id: 'e1', file: 'entities/leave.json' },
            { id: 'm1', file: 'application.json' },
          ],
        })
      }
      if (method === 'GET' && url === `${apps}/resources/m1`)
        return reply(200, {
          id: 'm1',
          file: 'application.json',
          content: manifest,
        })
      reply(404, { title: `unexpected ${method} ${url}` })
    })
  })
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve))
  return {
    calls,
    server,
    session: { server: `http://127.0.0.1:${server.address().port}`, cookie },
  }
}

const servers = []
const axis = async (options) => {
  const started = await startAxis(options)
  servers.push(started.server)
  return started
}
after(() => Promise.all(servers.map((server) => new Promise((resolve) => server.close(resolve)))))

const clock = () => {
  const times = [1000, 6000]
  return () => times.shift() ?? 6000
}
const options = { pollMs: 0, now: clock() }
const writes = (calls) => calls.filter((call) => call.method !== 'GET')

describe('runCase', () => {
  test('sends, approves and accepts, then returns the accepted draft', async () => {
    const { calls, session } = await axis({ endState: 'ready' })

    const run = await runCase(session, 'leave requests', 'Add a cost centre.', {
      ...options,
      now: clock(),
    })

    assert.deepEqual(
      writes(calls).map(({ method, url }) => `${method} ${url}`),
      [
        `POST ${apps}/authoring-requests`,
        `POST ${apps}/authoring-requests/r1/approve`,
        `POST ${apps}/authoring-requests/r1/accept`,
      ],
    )
    for (const call of calls) assert.equal(call.cookie, cookie)
    for (const call of writes(calls)) assert.equal(call.csrf, '1')
    assert.deepEqual(writes(calls)[0].body, { text: 'Add a cost centre.' })
    assert.deepEqual(run, {
      accepted: [
        {
          application: manifest,
          scenarios: [
            { name: 'AmountIsFrozenAfterSubmit', outcome: 'passed' },
            { name: 'ManagerApproves', outcome: 'skipped' },
          ],
          changed: ['entities/leave.json', 'roles/manager.json'],
        },
      ],
      tokens: 1540,
      durationMs: 5000,
    })
  })

  test('sends every call through the given fetch with the cookie and the CSRF header', async () => {
    const { calls, session } = await axis({ endState: 'ready' })
    const sent = []
    // The address of the session does not resolve. The fetch sends each call to the local server instead.
    const fetch = (url, init) => {
      sent.push({ url, ...init })
      return globalThis.fetch(url.replace('http://tenant.invalid', session.server), init)
    }

    const run = await runCase({ ...session, server: 'http://tenant.invalid/' }, 'leave requests', 'Add a cost centre.', {
      ...options,
      now: clock(),
      fetch,
    })

    assert.equal(run.accepted.length, 1)
    assert.equal(sent.length, calls.length)
    for (const call of sent) {
      assert.ok(call.url.startsWith(`http://tenant.invalid${apps}/`), call.url)
      assert.equal(call.headers.Cookie, cookie)
    }
    for (const call of sent.filter(({ method }) => method === 'POST')) assert.equal(call.headers['X-Axis-Csrf'], '1')
    assert.deepEqual(
      sent.map(({ method, url }) => `${method} ${new URL(url).pathname}`),
      calls.map(({ method, url }) => `${method} ${url}`),
    )
  })

  test('returns no accepted draft and makes no accept call for a request that fails', async () => {
    const { calls, session } = await axis({ endState: 'failed' })

    const run = await runCase(session, 'leave requests', 'Add a cost centre.', {
      ...options,
      now: clock(),
    })

    assert.deepEqual(run, { accepted: [], tokens: 1540, durationMs: 5000 })
    assert.equal(
      calls.some((call) => call.url.endsWith('/accept')),
      false,
    )
  })

  test('returns no accepted draft when the accept fails', async () => {
    const { session } = await axis({
      endState: 'ready',
      publishStatus: 'failed',
    })

    const run = await runCase(session, 'leave requests', 'Add a cost centre.', {
      ...options,
      now: clock(),
    })

    assert.deepEqual(run, { accepted: [], tokens: 1540, durationMs: 5000 })
  })

  test('names the route and the status of a refused call', async () => {
    const { session } = await axis({ endState: 'ready', sendStatus: 403 })

    await assert.rejects(
      runCase(session, 'leave requests', 'Add a cost centre.', options),
      new Error(`POST ${apps}/authoring-requests failed with HTTP 403`),
    )
  })

  test('throws when the request is still working at the time limit', async () => {
    const { session } = await axis({ endState: 'working' })
    const times = [0, 10, 20]

    await assert.rejects(
      runCase(session, 'leave requests', 'Add a cost centre.', {
        pollMs: 0,
        timeoutMs: 5,
        now: () => times.shift() ?? 99,
      }),
      /is still working after 5 ms/,
    )
  })
})
