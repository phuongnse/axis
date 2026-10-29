// The load test of docs/reference/performance.md#load-test, run by scripts/load.sh. 50 virtual users each sign in once
// through the page of the realm `load`, with a password and then the authenticator code of users.json, and keep their
// session cookie. They then loop over the read and write routes of the objectives. The machine caller of caller.json
// calls the inbound endpoint. Every request goes through send(), which tags it with its objective and its route, so the
// thresholds below measure the routes that docs/reference/performance.md lists. tests/load/objectives.test.mjs keeps
// that list and the calls of send() equal.
import http from 'k6/http'
import { check, sleep } from 'k6'
import { Rate } from 'k6/metrics'
import { totp } from './lib/totp.js'

export const options = {
  vus: 50,
  duration: '5m',
  // k6 empties the cookie jar of a user at the start of each iteration, which would end the session after sign-in.
  noCookiesReset: true,
  thresholds: {
    'http_req_duration{objective:reads}': ['p(95)<300'],
    'http_req_duration{objective:writes}': ['p(95)<500'],
    server_errors: ['rate<0.001'],
    // Without it, a run where every request answers 401 or 403 would still meet the thresholds above.
    checks: ['rate>0.99'],
  },
}

const app = 'LoadData'
const site = 'load'
const entities = 40
// All 50 users share one caller id, so the endpoint is called every fifth iteration. A call in every iteration would come
// to about 1,250 calls a minute for a replica, over the default of RateLimits:Bearer.
const endpointEvery = 5

const users = JSON.parse(open(__ENV.AXIS_LOAD_USERS))
const caller = JSON.parse(open(__ENV.AXIS_LOAD_CALLER))

const serverErrors = new Rate('server_errors')

// The only place that sends a request. objective is 'reads', 'writes' or 'signIn' (the sign-in pages and the token
// request are not routes of the Axis API). method and template are string literals at every call, so that the route test
// can read them. {key} in the template is replaced by values.key. A url replaces the host and path of the template.
function send(objective, method, template, { values = {}, url, body, form, headers = {}, query = '' } = {}) {
  const address = url ?? __ENV.AXIS_LOAD_URL + template.replace(/\{(\w+)\}/g, (_, key) => encodeURIComponent(values[key])) + query
  const requestHeaders = { ...headers }
  let content = null
  if (method !== 'GET') requestHeaders['X-Axis-Csrf'] = '1'
  if (body !== undefined) {
    requestHeaders['Content-Type'] = 'application/json'
    content = JSON.stringify(body)
  } else if (form !== undefined) {
    content = Object.entries(form)
      .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`)
      .join('&')
    requestHeaders['Content-Type'] = 'application/x-www-form-urlencoded'
  }
  const response = http.request(method, address, content, {
    headers: requestHeaders,
    tags: { objective, name: `${method} ${template}` },
    redirects: 0,
  })
  serverErrors.add(response.status >= 500)
  return response
}

const pad = (number, width) => String(number).padStart(width, '0')

// Sign-in: the same steps as tests/Axis.Integration.Tests/KeycloakSignIn.cs, through the site's client.
let signedIn = false

function expectStatus(response, status, step) {
  if (response.status !== status) throw new Error(`${step} answered ${response.status} and not ${status}: ${String(response.body).slice(0, 300)}`)
  return response
}

function signInContext(response, step) {
  const match = /<script type="application\/json" id="axis-sign-in-context">([\s\S]*?)<\/script>/.exec(String(response.body))
  if (!match) throw new Error(`${step} gave no sign-in context`)
  return JSON.parse(match[1])
}

function signIn(user) {
  const start = expectStatus(send('signIn', 'GET', '/api/auth/sign-in', { query: '?returnUrl=%2Fload' }), 302, 'sign-in')
  const login = expectStatus(send('signIn', 'GET', '/realms/{realm}/protocol/openid-connect/auth', { url: start.headers.Location }), 200, 'the login page')
  const password = expectStatus(
    send('signIn', 'POST', '/realms/{realm}/login-actions/authenticate', {
      url: signInContext(login, 'the login page').loginAction,
      form: { username: user.username, password: user.password, credentialId: '' },
    }),
    200,
    'the password',
  )
  const second = signInContext(password, 'the password')
  if (second.page !== 'twoStep') throw new Error(`the password led to the page ${second.page}${second.error ? `: ${second.error}` : ''}`)
  const code = expectStatus(
    send('signIn', 'POST', '/realms/{realm}/login-actions/authenticate', {
      url: second.loginAction,
      form: { otp: totp(user.secret), login: 'Sign In' },
    }),
    302,
    'the authenticator code',
  )
  expectStatus(send('signIn', 'GET', '/api/auth/callback', { url: code.headers.Location }), 302, 'the callback')
}

// The token of the machine caller, kept per virtual user until 30 seconds before it ends.
let token
let tokenEnds = 0

function callerToken() {
  if (token && Date.now() < tokenEnds) return token
  const response = expectStatus(
    send('signIn', 'POST', '/realms/{realm}/protocol/openid-connect/token', {
      url: __ENV.AXIS_LOAD_TOKEN_URL,
      form: { grant_type: 'client_credentials', client_id: caller.clientId, client_secret: caller.secret },
    }),
    200,
    'the token request',
  )
  const body = response.json()
  token = body.access_token
  tokenEnds = Date.now() + (body.expires_in - 30) * 1000
  return token
}

// The subject of the review task that this virtual user started in its previous iteration.
let previousSubject

export default function () {
  const user = users[(__VU - 1) % users.length]
  if (!signedIn) {
    signIn(user)
    signedIn = true
  }

  const number = (__VU - 1) % entities
  const entityName = `Entity${pad(number, 2)}`
  const values = { app, entity: entityName, dataSource: `DataSource${pad(number, 3)}`, path: site, page: `${entityName}List`, process: `${entityName}Process00` }

  // Reads
  const list = send('reads', 'GET', '/api/apps/{app}/entities/{entity}/records', { values, query: '?pageSize=20' })
  check(list, { 'the record list answers 200': (r) => r.status === 200 })
  const first = list.status === 200 ? list.json('items.0.id') : undefined
  if (first) {
    const one = send('reads', 'GET', '/api/apps/{app}/entities/{entity}/records/{id}', { values: { ...values, id: first } })
    check(one, { 'the record answers 200': (r) => r.status === 200 })
  }
  check(send('reads', 'GET', '/api/apps/{app}/data-sources/{dataSource}/rows', { values }), { 'the data source rows answer 200': (r) => r.status === 200 })
  check(send('reads', 'GET', '/api/sites/{path}', { values }), { 'the site answers 200': (r) => r.status === 200 })
  check(send('reads', 'GET', '/api/sites/{path}/pages/{page}', { values }), { 'the page answers 200': (r) => r.status === 200 })
  const tasks = send('reads', 'GET', '/api/apps/{app}/tasks', { values, query: '?pageSize=100' })
  check(tasks, { 'the task list answers 200': (r) => r.status === 200 })

  // Writes: a record of the user's own, which gets a process, and a separate record that is deleted.
  const created = send('writes', 'POST', '/api/apps/{app}/entities/{entity}/records', {
    values,
    body: { values: { name: `load ${__VU}-${__ITER}`, quantity: Math.floor(Math.random() * 1001), status: 'open' } },
  })
  if (check(created, { 'the record create answers 201': (r) => r.status === 201 })) {
    const record = created.json()
    const updated = send('writes', 'PATCH', '/api/apps/{app}/entities/{entity}/records/{id}', {
      values: { ...values, id: record.id },
      body: { version: record.version, values: { quantity: Math.floor(Math.random() * 1001) } },
    })
    check(updated, { 'the record update answers 200': (r) => r.status === 200 })
    const started = send('writes', 'POST', '/api/apps/{app}/processes/{process}/instances', { values, body: { subjectId: record.id } })
    const completing = previousSubject
    previousSubject = check(started, { 'the process start answers 201': (r) => r.status === 201 }) ? record.id : undefined

    // The task of the previous iteration's record: the worker has had a second to create it.
    const task = tasks.status === 200 && completing ? tasks.json('items').find((item) => item.subject.id === completing) : undefined
    if (task) {
      const done = send('writes', 'POST', '/api/apps/{app}/tasks/{id}/complete', { values: { ...values, id: task.id }, body: { outcome: 'approve' } })
      check(done, { 'the task complete answers 200': (r) => r.status === 200 })
    }
  }

  const spare = send('writes', 'POST', '/api/apps/{app}/entities/{entity}/records', {
    values,
    body: { values: { name: `spare ${__VU}-${__ITER}`, quantity: 1, status: 'open' } },
  })
  if (check(spare, { 'the spare record create answers 201': (r) => r.status === 201 })) {
    const removed = send('writes', 'DELETE', '/api/apps/{app}/entities/{entity}/records/{id}', { values: { ...values, id: spare.json('id') } })
    check(removed, { 'the record delete answers 204': (r) => r.status === 204 })
  }

  if (__ITER % endpointEvery === 0) {
    const called = send('writes', 'POST', '/api/apps/{app}/endpoints/{name}', {
      values: { app, name: 'CreateEntity00' },
      headers: { Authorization: `Bearer ${callerToken()}` },
      body: { input: { name: `endpoint ${__VU}-${__ITER}`, quantity: Math.floor(Math.random() * 1001) } },
    })
    check(called, { 'the endpoint call answers 201': (r) => r.status === 201 })
  }

  sleep(1)
}
