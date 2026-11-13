// Runs one case of the authoring eval through the authoring request routes, as an author does in Studio, see
// docs/reference/authoring-requests.md#routes and docs/reference/model-bindings.md#the-authoring-eval.
//
// It sends the case's request, approves the plan and waits for the request to end. It accepts a `ready` request and reads
// the accepted draft back. It returns the RUN that judgeCase in scripts/lib/ai-eval.mjs takes. The session is the server's
// address and the value of the `Cookie` header of a signed-in author, and signing in is the caller's. Every call goes through
// `fetch`, which a caller replaces when the server's address does not resolve, such as a tenant host on a machine that does not
// map `*.eval.localhost` to the loopback address.
const planning = 'planning'
const awaitingApproval = 'awaitingApproval'
const ended = ['ready', 'failed', 'dropped', 'accepted']

/** Runs one case's request as its author through the authoring request routes and returns the RUN that judgeCase takes. */
export async function runCase(
  { server, cookie },
  application,
  text,
  { pollMs = 2000, timeoutMs = 3_600_000, now = Date.now, fetch = globalThis.fetch } = {},
) {
  const base = server.replace(/\/+$/, '')
  const apps = `/api/studio/apps/${encodeURIComponent(application)}`

  const call = async (method, route, body) => {
    const headers = { Cookie: cookie, Accept: 'application/json' }
    if (method === 'POST') headers['X-Axis-Csrf'] = '1'
    if (body !== undefined) headers['Content-Type'] = 'application/json'
    const response = await fetch(`${base}${apps}${route}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })
    if (!response.ok) throw new Error(`${method} ${apps}${route} failed with HTTP ${response.status}`)
    const answer = await response.text()
    return answer === '' ? undefined : JSON.parse(answer)
  }

  const start = now()
  const deadline = start + timeoutMs

  // Reads the route until `done` accepts the state, and gives up at the deadline of the whole case.
  const poll = async (route, label, done) => {
    for (;;) {
      const answer = await call('GET', route)
      const state = answer[label]
      if (done(state)) return answer
      if (now() > deadline) throw new Error(`GET ${apps}${route} is still ${state} after ${timeoutMs} ms`)
      await new Promise((resolve) => setTimeout(resolve, pollMs))
    }
  }

  const sent = await call('POST', '/authoring-requests', { text })
  const route = `/authoring-requests/${sent.id}`
  let request = await poll(route, 'state', (state) => state !== planning)
  if (request.state === awaitingApproval) {
    await call('POST', `${route}/approve`)
    request = await poll(route, 'state', (state) => ended.includes(state))
  }
  if (request.state !== 'ready') return { accepted: [], tokens: request.tokens, durationMs: now() - start }

  const accept = await call('POST', `${route}/accept`)
  const publish = await poll(
    `/requests/${accept.id}`,
    'status',
    (status) => status === 'succeeded' || status === 'failed',
  )
  if (publish.status === 'failed') {
    return { accepted: [], tokens: request.tokens, durationMs: now() - start }
  }
  const scenarios = (publish.scenarios ?? []).map(({ name, outcome }) => ({
    name,
    outcome,
  }))

  // The accept wrote one revision that names the accept's request, and its files are what the accepted draft changed.
  const revisions = await call('GET', '/revisions')
  const revision = revisions.items.find((item) => item.request === accept.id)
  if (revision === undefined) throw new Error(`GET ${apps}/revisions has no revision of accept request ${accept.id}`)
  const changed = (await call('GET', `/revisions/${revision.id}`)).resources.map((resource) => resource.file)

  // After the accept the working folder is the accepted draft.
  const resources = await call('GET', '/resources')
  const manifest = resources.items.find((item) => item.file === 'application.json')
  if (manifest === undefined) throw new Error(`GET ${apps}/resources has no application.json`)
  const { content: manifestContent } = await call('GET', `/resources/${manifest.id}`)

  const final = await call('GET', route)
  return {
    accepted: [{ application: manifestContent, scenarios, changed }],
    tokens: final.tokens,
    durationMs: now() - start,
  }
}
