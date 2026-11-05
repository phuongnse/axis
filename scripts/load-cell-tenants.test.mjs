import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// Runs scripts/lib/load-cell-tenants.mjs with 2 busy and 3 idle tenants, and curl replaced by a stub that logs its calls. The
// stub answers the provisioning API, the token endpoints and the admin API of the identity provider for any realm.
const root = fileURLToPath(new URL('..', import.meta.url))

const curl = `#!/usr/bin/env bash
echo "$*" >> "$STUB_LOG"
body=''
if [[ " $* " == *' --data-binary @- '* ]]; then body=$(cat); fi
last="\${@: -1}"
case "$last" in
  */api/cell/tenants)
    id=$(printf '%s' "$body" | sed -E 's/.*"id":"([^"]+)".*/\\1/')
    printf '{"id":"job-%s","state":"queued"}' "$id" ;;
  */api/cell/jobs/job-*)
    id="\${last##*/job-}"
    if [ "$id" = "\${STUB_FAIL_TENANT:-}" ]; then
      printf '{"id":"job-%s","state":"failed","steps":[{"name":"tenant","state":"succeeded"},{"name":"application","state":"failed","error":"the template is gone"}],"error":"the template is gone"}' "$id"
    elif [ "$id" = "\${STUB_RUNNING_TENANT:-}" ]; then
      printf '{"id":"job-%s","state":"running","steps":[]}' "$id"
    else
      printf '{"id":"job-%s","state":"succeeded","steps":[]}' "$id"
    fi ;;
  */openid-connect/token) printf '{"access_token":"stub-token"}' ;;
  *'/clients?clientId=axis') printf '[{"id":"client-1","clientId":"axis","secret":"s"}]' ;;
  */clients/client-1/roles)
    if [[ "$*" != *' -X POST '* ]]; then
      printf '[{"id":"role-1","name":"clerk"},{"id":"role-2","name":"reviewer"},{"id":"role-3","name":"load-caller"}]'
    fi ;;
  */clients/caller-1/service-account-user) printf '{"id":"service-user-1"}' ;;
  */admin/realms/*/clients) printf 'HTTP/2 201\\r\\nlocation: https://id.axis.test/admin/realms/x/clients/caller-1\\r\\n\\r\\n' ;;
  */admin/realms/*/users) printf 'HTTP/2 201\\r\\nlocation: https://id.axis.test/admin/realms/x/users/user-%s\\r\\n\\r\\n' "$RANDOM" ;;
esac
exit 0
`

const settings = {
  AXIS_CELL_URL: 'https://console.cell.test/',
  AXIS_CELL_TENANT_DOMAIN: 'cell.test',
  AXIS_IDENTITY_URL: 'https://id.cell.test',
  AXIS_CELL_OPERATOR_REALM: 'operators',
  AXIS_CELL_OPERATOR_CLIENT_ID: 'load-operator',
  AXIS_CELL_OPERATOR_CLIENT_SECRET: 'operator-secret',
  AXIS_IDENTITY_ADMIN_CLIENT_SECRET: 'admin-secret',
  AXIS_LOAD_APPLICATION: 'approvals',
  AXIS_LOAD_ADMINISTRATOR: 'load@axis.test',
  AXIS_LOAD_BUSY_TENANTS: '2',
  AXIS_LOAD_IDLE_TENANTS: '3',
  AXIS_LOAD_POLL_SECONDS: '0',
}

function runScript({ env = {}, args } = {}) {
  const dir = mkdtempSync(join(tmpdir(), 'axis-load-cell-'))
  try {
    const bin = join(dir, 'bin')
    const folder = join(dir, 'run')
    mkdirSync(bin)
    mkdirSync(join(folder, 'apps/load/roles'), { recursive: true })
    for (const role of ['clerk', 'reviewer', 'load-caller']) {
      writeFileSync(join(folder, 'apps/load/roles', `${role}.json`), JSON.stringify({ kind: 'role', name: role }))
    }
    writeFileSync(join(bin, 'curl'), curl, { mode: 0o755 })

    const log = join(dir, 'log')
    const processEnv = { ...process.env, PATH: `${bin}:${process.env.PATH}`, STUB_LOG: log, ...settings, ...env }
    for (const name of Object.keys(processEnv)) {
      if (name.startsWith('AXIS_') && !(name in settings) && !(name in env)) delete processEnv[name]
    }
    const result = spawnSync('node', ['scripts/lib/load-cell-tenants.mjs', ...(args ?? [folder, '7'])], {
      cwd: root,
      encoding: 'utf8',
      timeout: 60_000,
      env: processEnv,
    })
    const tenantsFile = join(folder, 'k6/tenants.json')
    return {
      ...result,
      calls: existsSync(log) ? readFileSync(log, 'utf8').trimEnd().split('\n') : [],
      tenants: existsSync(tenantsFile) ? JSON.parse(readFileSync(tenantsFile, 'utf8')) : undefined,
      tenantsMode: existsSync(tenantsFile) ? statSync(tenantsFile).mode & 0o777 : undefined,
      realmMode: existsSync(join(folder, 'realm')) ? statSync(join(folder, 'realm')).mode & 0o777 : undefined,
      authorizationMode: existsSync(join(folder, 'realm/authorization')) ? statSync(join(folder, 'realm/authorization')).mode & 0o777 : undefined,
    }
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
}

const ids = ['load-busy-01', 'load-busy-02', 'load-idle-001', 'load-idle-002', 'load-idle-003']

describe('scripts/lib/load-cell-tenants.mjs creates the load tenants through the provisioning API', () => {
  test('it queues one create and polls one job for each tenant, each create with its own idempotency key', () => {
    const run = runScript()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    const creates = run.calls.filter((call) => / -X POST .*\/api\/cell\/tenants$/.test(call))
    assert.equal(creates.length, ids.length)
    const keys = creates.map((call) => call.match(/ -H Idempotency-Key: (\S+) /)?.[1])
    assert.deepEqual(keys, ids.map((id) => `load-cell-7-${id}`))
    assert.equal(new Set(keys).size, ids.length)
    for (const call of creates) assert.match(call, / -H Content-Type: application\/json /, call)
    const polls = run.calls.filter((call) => / -X GET .*\/api\/cell\/jobs\/job-/.test(call))
    assert.equal(polls.length, ids.length)
    ids.forEach((id, index) => assert.match(polls[index], new RegExp(`https://console\\.cell\\.test/api/cell/jobs/job-${id}$`)))
    const lastCreate = run.calls.lastIndexOf(creates.at(-1))
    assert.ok(run.calls.findIndex((call) => call.includes('/api/cell/jobs/')) > lastCreate, 'every create is queued before the first poll')
  })

  test('the written file holds 10 users on each busy tenant and 1 on each idle tenant, with the urls and a caller on each busy tenant', () => {
    const run = runScript()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.equal(run.tenants.busy.length, 2)
    assert.equal(run.tenants.idle.length, 3)
    run.tenants.busy.forEach((tenant, index) => {
      const id = ids[index]
      assert.equal(tenant.url, `https://${id}.cell.test`)
      assert.equal(tenant.tokenUrl, `https://id.cell.test/realms/${id}/protocol/openid-connect/token`)
      assert.equal(tenant.users.length, 10)
      assert.equal(tenant.caller.clientId, 'load-caller')
      assert.ok(tenant.caller.secret.length > 0)
    })
    run.tenants.idle.forEach((tenant, index) => {
      const id = ids[index + 2]
      assert.equal(tenant.url, `https://${id}.cell.test`)
      assert.equal(tenant.tokenUrl, `https://id.cell.test/realms/${id}/protocol/openid-connect/token`)
      assert.equal(tenant.users.length, 1)
      assert.equal(tenant.caller, undefined)
    })
    const users = [...run.tenants.busy, ...run.tenants.idle].flatMap((tenant) => tenant.users)
    for (const user of users) {
      assert.ok(user.password.length > 0)
      assert.match(user.secret, /^[A-Z2-7]{32}$/)
    }
    assert.equal(new Set(users.map((user) => user.secret)).size, users.length, 'each realm has its own secrets')
    assert.equal(run.tenantsMode, 0o644)
    assert.equal(run.realmMode, 0o700)
    assert.equal(run.authorizationMode, 0o600)
  })

  test('the identity provider is called for every tenant realm, and no token is on a command line', () => {
    const run = runScript()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    for (const id of ids) {
      assert.ok(run.calls.some((call) => / -X POST .*\/admin\/realms\/\S+\/users$/.test(call) && call.includes(`/admin/realms/${id}/users`)), id)
    }
    assert.equal(run.calls.filter((call) => / -X POST .*\/admin\/realms\/\S+\/users$/.test(call)).length, 2 * 10 + 3)
    assert.equal(run.calls.filter((call) => / -X POST .*\/admin\/realms\/\S+\/clients$/.test(call)).length, 2)
    for (const call of run.calls.filter((call) => call.includes('/admin/realms/') || call.includes('/api/cell/'))) {
      assert.match(call, / -H @\S+\/realm\/(operator-)?authorization /, call)
    }
    for (const call of run.calls) {
      assert.doesNotMatch(call, /stub-token|operator-secret|admin-secret/, call)
      assert.doesNotMatch(call, /--cacert/, call)
    }
  })

  test('AXIS_CA_FILE makes curl trust that authority', () => {
    const run = runScript({ env: { AXIS_CA_FILE: '/etc/cell/ca.crt' } })

    assert.equal(run.status, 0, run.stdout + run.stderr)
    for (const call of run.calls) assert.match(call, / --cacert \/etc\/cell\/ca\.crt /, call)
  })

  test('a failed create job stops the script, names the tenant and the error, and writes no tenants file', () => {
    const run = runScript({ env: { STUB_FAIL_TENANT: 'load-idle-002' } })

    assert.equal(run.status, 1)
    assert.match(run.stderr, /load-idle-002/)
    assert.match(run.stderr, /the template is gone/)
    assert.equal(run.tenants, undefined)
    assert.equal(run.calls.some((call) => call.includes('/admin/realms/')), false, 'no user is added after a failed job')
    assert.equal(run.calls.some((call) => call.includes('job-load-idle-003')), false, 'the polling stops at the failed job')
  })

  test('a job that does not end in time stops the script, names the tenant and writes no tenants file', () => {
    const run = runScript({ env: { AXIS_LOAD_JOB_TIMEOUT_SECONDS: '0', STUB_RUNNING_TENANT: 'load-busy-02' } })

    assert.equal(run.status, 1, run.stdout + run.stderr)
    assert.match(run.stderr, /load-busy-02/)
    assert.match(run.stderr, /did not end in 0 s/)
    assert.equal(run.tenants, undefined)
  })

  test('a missing setting prints the usage and exits with code 2', () => {
    const run = runScript({ env: { AXIS_CELL_URL: '' } })

    assert.equal(run.status, 2, run.stdout + run.stderr)
    assert.match(run.stderr, /AXIS_CELL_URL/)
    assert.match(run.stderr, /Usage: node scripts\/lib\/load-cell-tenants\.mjs <folder> <seed>/)
    assert.deepEqual(run.calls, [])
  })

  test('a missing folder prints the usage and exits with code 2', () => {
    const run = runScript({ args: [] })

    assert.equal(run.status, 2, run.stdout + run.stderr)
    assert.match(run.stderr, /Usage: /)
  })
})
