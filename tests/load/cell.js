// The cell load test of docs/reference/performance.md#cell-load-test. Busy tenants: 10 users each loop over the objectives.
// Idle tenants: one user signs in once an hour and reads a page every 10 minutes. AXIS_LOAD_TENANTS names a JSON file with
// one entry per tenant, each with its own host and realm: { "busy": [{ url, tokenUrl, caller, users }], "idle": [{ url,
// tokenUrl, users }] }. Each sample is tagged phase:warmup or phase:run, and the thresholds read only phase:run.
import exec from 'k6/execution'
import { check, sleep } from 'k6'
import { Rate, Trend } from 'k6/metrics'
import { configure, objectivesIteration, readPage, signIn, signOut } from './lib/objectives-loop.js'

const busyTenants = 50
const usersPerBusyTenant = 10
const idleTenants = 949
const warmUpMinutes = 10
const measuredMinutes = 60
const signInEvery = 60 * 60
const readEvery = 10 * 60
const endSeconds = (warmUpMinutes + measuredMinutes) * 60

const tenants = JSON.parse(open(__ENV.AXIS_LOAD_TENANTS))
const shapeMatches =
  tenants.busy.length === busyTenants &&
  tenants.busy.every((tenant) => tenant.users.length === usersPerBusyTenant) &&
  tenants.idle.length === idleTenants &&
  tenants.idle.every((tenant) => tenant.users.length === 1)
if (!shapeMatches) {
  throw new Error(`AXIS_LOAD_TENANTS must list ${busyTenants} busy tenants of ${usersPerBusyTenant} users and ${idleTenants} idle tenants`)
}

export const options = {
  noCookiesReset: true,
  scenarios: {
    busy: { executor: 'per-vu-iterations', vus: busyTenants * usersPerBusyTenant, iterations: 1, maxDuration: '75m', exec: 'busy' },
    idle: { executor: 'per-vu-iterations', vus: idleTenants, iterations: 1, maxDuration: '75m', exec: 'idle' },
  },
  thresholds: {
    'http_req_duration{scenario:busy,objective:reads,phase:run}': ['p(95)<300'],
    'http_req_duration{scenario:busy,objective:writes,phase:run}': ['p(95)<500'],
    'server_errors{scenario:busy,phase:run}': ['rate<0.001'],
    'checks{scenario:busy,phase:run}': ['rate>0.99'],
    'idle_sign_in{phase:run}': ['p(95)<2000'],
    'idle_sign_ins{phase:run}': ['rate==1'],
  },
}

const idleSignInTime = new Trend('idle_sign_in', true)
const idleSignIns = new Rate('idle_sign_ins')

const elapsedSeconds = () => (Date.now() - exec.scenario.startTime) / 1000

function tagPhase() {
  exec.vu.metrics.tags.phase = elapsedSeconds() < warmUpMinutes * 60 ? 'warmup' : 'run'
}

function waitUntil(seconds) {
  const wait = exec.scenario.startTime + seconds * 1000 - Date.now()
  if (wait > 0) sleep(wait / 1000)
}

function timedSignIn(user) {
  const started = Date.now()
  let succeeded = true
  try {
    signIn(user)
  } catch (error) {
    succeeded = false
  }
  idleSignInTime.add(Date.now() - started)
  idleSignIns.add(succeeded)
}

// Busy user i of the scenario is user i % 10 of tenant floor(i / 10). The executor gives each index to one VU only.
export function busy() {
  const index = exec.scenario.iterationInTest
  const tenant = tenants.busy[Math.floor(index / usersPerBusyTenant)]
  const number = index % usersPerBusyTenant
  configure(tenant)
  let iteration = 0
  while (elapsedSeconds() < endSeconds) {
    tagPhase()
    try {
      objectivesIteration({ user: tenant.users[number], number, iteration })
    } catch (error) {
      check(null, { 'the step ran': () => false })
    }
    iteration++
    sleep(1)
  }
}

// Idle tenant i has its user at the offset i * 10 minutes / 949 of the warm-up, so the sign-ins do not come at one time.
export function idle() {
  const index = exec.scenario.iterationInTest
  const tenant = tenants.idle[index]
  configure(tenant)
  const offset = (index * warmUpMinutes * 60) / idleTenants
  const stepsPerSignIn = signInEvery / readEvery
  for (let step = 0; offset + step * readEvery < endSeconds; step++) {
    waitUntil(offset + step * readEvery)
    tagPhase()
    if (step % stepsPerSignIn === 0) {
      signOut()
      timedSignIn(tenant.users[0])
    } else {
      try {
        readPage(index)
      } catch (error) {
        check(null, { 'the step ran': () => false })
      }
    }
  }
}
