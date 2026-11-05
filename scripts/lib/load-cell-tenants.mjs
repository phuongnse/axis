// Creates the load tenants of a staging cell through the provisioning API (docs/reference/tenant-lifecycle.md#provisioning-api)
// and adds their users, for the cell load test (docs/reference/performance.md#cell-load-test). It signs in as a client of the
// operator realm with a bearer token and queues one create job for each tenant: the busy tenants load-busy-01 to
// load-busy-50, then the idle tenants load-idle-001 to load-idle-949. Each create has its own Idempotency-Key, fixed by the
// seed and the tenant id, so a second run within 24 hours replays the first jobs. It then polls the jobs in order until each
// has succeeded, and stops at the first job that failed or timed out.
// When every job has succeeded, it adds the users through the identity provider's admin API, as scripts/lib/load-realm.mjs
// does: the roles of the generated application, the roles mapper, 10 users in each busy realm and 1 in each idle realm,
// with a password and an authenticator, and the machine caller load-caller in each busy realm. It writes
// <folder>/k6/tenants.json, the file that tests/load/cell.js reads through AXIS_LOAD_TENANTS:
// { "busy": [{ url, tokenUrl, caller, users }], "idle": [{ url, tokenUrl, users }] }.
// Run it as `node scripts/lib/load-cell-tenants.mjs <folder> <seed>`. <folder> holds the generated application in
// apps/load/roles and receives realm/ and k6/tenants.json. Run it again after a failure only with a new seed or after the
// users are removed: the create jobs replay, but the identity provider refuses a user that exists.
// Every request goes through curl, so that a test can replace it with a stub. The settings come from the environment, so no
// secret is on a command line:
//   AXIS_CELL_URL                      the address of the provisioning API, such as https://console.cell.example.com
//   AXIS_CELL_TENANT_DOMAIN            the domain of the tenant hosts, Cell:TenantDomain
//   AXIS_IDENTITY_URL                  the address of the identity provider
//   AXIS_CELL_OPERATOR_REALM           the operator realm, Cell:OperatorRealm
//   AXIS_CELL_OPERATOR_CLIENT_ID       a client of the operator realm with the operator role
//   AXIS_CELL_OPERATOR_CLIENT_SECRET   the secret of that client
//   AXIS_IDENTITY_ADMIN_CLIENT_SECRET  the secret of the client axis-admin of the master realm
//   AXIS_LOAD_APPLICATION              the template that each tenant gets
//   AXIS_LOAD_ADMINISTRATOR            the email of each tenant's first administrator
//   AXIS_LOAD_BUSY_TENANTS (50), AXIS_LOAD_IDLE_TENANTS (949)       the tenant counts
//   AXIS_LOAD_POLL_SECONDS (5), AXIS_LOAD_JOB_TIMEOUT_SECONDS (1800)  how a job is polled, and how long one job may take
//   AXIS_CA_FILE                       optional: the certificate authority that curl trusts
import { randomBytes } from 'node:crypto'
import { chmodSync, mkdirSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import {
  addLoadCaller,
  addLoadUsers,
  bearerFile,
  curlRequest,
  ensureRealmRoles,
  loadUsers,
  readDevelopmentRealm,
  readRoleNames,
  realmAdmin,
} from './load-realm.mjs'

const busyUsers = 10
const idleUsers = 1

const required = [
  'AXIS_CELL_URL',
  'AXIS_CELL_TENANT_DOMAIN',
  'AXIS_IDENTITY_URL',
  'AXIS_CELL_OPERATOR_REALM',
  'AXIS_CELL_OPERATOR_CLIENT_ID',
  'AXIS_CELL_OPERATOR_CLIENT_SECRET',
  'AXIS_IDENTITY_ADMIN_CLIENT_SECRET',
  'AXIS_LOAD_APPLICATION',
  'AXIS_LOAD_ADMINISTRATOR',
]

const trimSlash = (url) => url.replace(/\/+$/, '')
const pause = (seconds) => {
  if (seconds > 0) Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, seconds * 1000)
}

// [{ id, busy }]: the busy tenants first, then the idle tenants.
export const tenantIds = (busy, idle) => [
  ...Array.from({ length: busy }, (_, index) => ({ id: `load-busy-${String(index + 1).padStart(2, '0')}`, busy: true })),
  ...Array.from({ length: idle }, (_, index) => ({ id: `load-idle-${String(index + 1).padStart(3, '0')}`, busy: false })),
]

export const provisionLoadTenants = ({ folder: target, seed, env }) => {
  const folder = resolve(target)
  const cellUrl = trimSlash(env.AXIS_CELL_URL)
  const identityUrl = trimSlash(env.AXIS_IDENTITY_URL)
  const domain = env.AXIS_CELL_TENANT_DOMAIN
  const busyCount = Number(env.AXIS_LOAD_BUSY_TENANTS ?? 50)
  const idleCount = Number(env.AXIS_LOAD_IDLE_TENANTS ?? 949)
  const pollSeconds = Number(env.AXIS_LOAD_POLL_SECONDS ?? 5)
  const timeoutSeconds = Number(env.AXIS_LOAD_JOB_TIMEOUT_SECONDS ?? 1800)
  const options = env.AXIS_CA_FILE ? ['--cacert', env.AXIS_CA_FILE] : []

  const realmFolder = join(folder, 'realm')
  mkdirSync(realmFolder, { recursive: true, mode: 0o700 })
  const identity = curlRequest({ base: identityUrl, options })
  const cell = curlRequest({ base: cellUrl, options })
  const tokenPath = (realm) => `/realms/${realm}/protocol/openid-connect/token`

  const operatorAuthorization = join(realmFolder, 'operator-authorization')
  const authenticateOperator = bearerFile({
    request: identity,
    tokenPath: tokenPath(env.AXIS_CELL_OPERATOR_REALM),
    clientId: env.AXIS_CELL_OPERATOR_CLIENT_ID,
    clientSecret: env.AXIS_CELL_OPERATOR_CLIENT_SECRET,
    file: operatorAuthorization,
  })
  const cellCall = (method, path, body, extraHeaders = []) => {
    authenticateOperator()
    const json = body === undefined ? undefined : JSON.stringify(body)
    const headers = ['-H', `@${operatorAuthorization}`, ...(json === undefined ? [] : ['-H', 'Content-Type: application/json']), ...extraHeaders]
    return JSON.parse(cell(method, path, { headers, body: json }))
  }

  const tenants = tenantIds(busyCount, idleCount)
  const jobs = tenants.map(({ id }) => {
    const job = cellCall(
      'POST',
      '/api/cell/tenants',
      { id, application: env.AXIS_LOAD_APPLICATION, administrator: env.AXIS_LOAD_ADMINISTRATOR },
      ['-H', `Idempotency-Key: load-cell-${seed}-${id}`],
    )
    return { id, job: job.id }
  })

  for (const { id, job } of jobs) {
    const deadline = Date.now() + timeoutSeconds * 1000
    for (;;) {
      const { state, error, steps = [] } = cellCall('GET', `/api/cell/jobs/${job}`)
      if (state === 'succeeded') break
      if (state === 'failed') {
        const reason = error ?? steps.find((step) => step.error)?.error ?? 'no error given'
        throw new Error(`the create job ${job} of tenant ${id} failed: ${reason}`)
      }
      if (Date.now() >= deadline) throw new Error(`the create job ${job} of tenant ${id} failed: it did not end in ${timeoutSeconds} s`)
      pause(pollSeconds)
    }
  }

  const adminAuthorization = join(realmFolder, 'authorization')
  const authenticateAdmin = bearerFile({
    request: identity,
    tokenPath: tokenPath('master'),
    clientId: 'axis-admin',
    clientSecret: env.AXIS_IDENTITY_ADMIN_CLIENT_SECRET,
    file: adminAuthorization,
  })
  const developmentRealm = readDevelopmentRealm()
  const roleNames = readRoleNames(folder)
  const result = { busy: [], idle: [] }
  for (const { id, busy } of tenants) {
    const admin = realmAdmin({ request: identity, authenticate: authenticateAdmin, authorization: adminAuthorization, realm: id })
    const { platformClient, roles } = ensureRealmRoles({ admin, roleNames, developmentRealm })
    const users = loadUsers(seed, randomBytes(18).toString('hex'), { count: busy ? busyUsers : idleUsers, key: `axis-load-${seed}-${id}` })
    addLoadUsers({ admin, platformClient, roles, users })
    const entry = { url: `https://${id}.${domain}`, tokenUrl: `${identityUrl}${tokenPath(id)}` }
    if (busy) result.busy.push({ ...entry, caller: addLoadCaller({ admin, platformClient, roles, developmentRealm }), users })
    else result.idle.push({ ...entry, users })
  }

  const k6Folder = join(folder, 'k6')
  mkdirSync(k6Folder, { recursive: true })
  const output = join(k6Folder, 'tenants.json')
  writeFileSync(output, JSON.stringify(result, null, 2))
  chmodSync(output, 0o644)
  return result
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const [folder, seed] = process.argv.slice(2)
  const missing = required.filter((name) => !process.env[name])
  if (!folder || seed === undefined || missing.length > 0) {
    if (missing.length > 0) console.error(`Missing environment: ${missing.join(', ')}`)
    console.error('Usage: node scripts/lib/load-cell-tenants.mjs <folder> <seed>')
    process.exit(2)
  }
  try {
    provisionLoadTenants({ folder, seed, env: process.env })
  } catch (error) {
    console.error(error.message)
    process.exit(1)
  }
}
