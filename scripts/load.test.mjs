import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { loadUsers } from './lib/load-realm.mjs'

// Runs scripts/load.sh in a temporary copy of the files it needs, with docker, npm and curl replaced by stubs that log
// their calls. The real node and openssl write the certificates, the secret files and the override file.
const root = fileURLToPath(new URL('..', import.meta.url))

const log = (name) => `echo "${name} $*" >> "$STUB_LOG"\n`

const stubs = {
  // STUB_FAIL_K6 makes the k6 call fail with status 3.
  docker: `#!/usr/bin/env bash
${log('docker')}if [ -n "\${STUB_FAIL_K6:-}" ] && [[ "$*" == *"run --rm --no-TTY k6"* ]]; then exit 3; fi
exit 0
`,
  // The tenant answers 401, as the wait for its host expects. The admin API of the identity provider answers by the URL,
  // which is the last argument. Anything else gives no output.
  curl: `#!/usr/bin/env bash
${log('curl')}case "$*" in *'%{http_code}'*) printf 401 ;; esac
case "\${@: -1}" in
  */openid-connect/token) printf '{"access_token":"stub-token"}' ;;
  *'/clients?clientId=axis') printf '[{"id":"client-1","clientId":"axis","secret":"s"}]' ;;
  */clients/client-1/roles)
    if [[ "$*" != *' -X POST '* ]]; then
      printf '[{"id":"role-1","name":"clerk"},{"id":"role-2","name":"reviewer"},{"id":"role-3","name":"auditor"}]'
    fi ;;
  */admin/realms/load/users) printf 'HTTP/2 201\r\nlocation: https://id.axis.test/admin/realms/load/users/user-%s\r\n\r\n' "$RANDOM" ;;
esac
exit 0
`,
  // The generator writes the roles of the application into the folder after --.
  dotnet: `#!/usr/bin/env bash
${log('dotnet')}if [ "$1" = run ]; then
  for role in clerk reviewer auditor; do
    mkdir -p "\${@: -3:1}/roles"
    printf '{"kind":"role","name":"%s"}' "$role" > "\${@: -3:1}/roles/$role.json"
  done
fi
exit 0
`,
  npm: `#!/usr/bin/env bash
${log('npm')}exit 0
`,
}

function runLoad(args = [], extraEnv = {}) {
  const dir = mkdtempSync(join(tmpdir(), 'axis-load-'))
  try {
    for (const file of [
      'scripts/load.sh',
      'scripts/lib/load-stack.mjs',
      'scripts/lib/load-realm.mjs',
      'samples/keycloak/axis-realm.json',
      'scripts/lib/postgres-tls.mjs',
      'scripts/lib/retry.sh',
      'deploy/compose/compose.yaml',
      'tests/load/placeholder.js',
    ]) {
      mkdirSync(dirname(join(dir, file)), { recursive: true })
      copyFileSync(join(root, file), join(dir, file))
    }
    const bin = join(dir, 'bin')
    mkdirSync(bin)
    for (const [name, body] of Object.entries(stubs)) {
      writeFileSync(join(bin, name), body, { mode: 0o755 })
    }

    const env = {
      ...process.env,
      PATH: `${bin}:${process.env.PATH}`,
      STUB_LOG: join(dir, 'log'),
      AXIS_RETRY_DELAY_SECONDS: '0',
      ...extraEnv,
    }
    for (const name of Object.keys(env)) if (name.startsWith('AXIS_') && !['AXIS_RETRY_DELAY_SECONDS'].includes(name)) delete env[name]

    const result = spawnSync('bash', ['scripts/load.sh', ...args], { cwd: dir, encoding: 'utf8', timeout: 120_000, env })
    const usersFile = join(dir, 'artifacts/load/k6/users.json')
    const users = existsSync(usersFile) ? JSON.parse(readFileSync(usersFile, 'utf8')) : undefined
    const calls = existsSync(env.STUB_LOG) ? readFileSync(env.STUB_LOG, 'utf8').trimEnd().split('\n') : []
    return { ...result, calls, dir, users, left: existsSync(join(dir, 'artifacts/load/override.json')) }
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
}

const composeCalls = (run) => run.calls.filter((call) => call.startsWith('docker compose '))
const indexOf = (run, text) => {
  const index = run.calls.findIndex((call) => call.includes(text))
  assert.notEqual(index, -1, `no call includes "${text}"\n${run.calls.join('\n')}`)
  return index
}
const k6Index = (run) => indexOf(run, 'run --rm --no-TTY k6')

describe('scripts/load.sh starts the reference stack and runs the placeholder scenario', () => {
  test('every compose call names compose.yaml and the load override, and server starts before server-2', () => {
    const run = runLoad()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    const calls = composeCalls(run)
    assert.ok(calls.length > 0)
    for (const call of calls) {
      assert.match(call, / -p axis-load /, call)
      assert.match(call, / -f \S+\/deploy\/compose\/compose\.yaml /, call)
      assert.match(call, / -f \S+\/artifacts\/load\/override\.json /, call)
    }
    assert.ok(run.calls.some((call) => call.startsWith('npm run build:keycloak-theme')))
    for (const target of ['axis-server', 'axis-worker', 'axis-admin']) {
      assert.ok(run.calls.some((call) => call.startsWith(`docker build --target ${target} `)), target)
    }

    const first = indexOf(run, ' up -d server worker')
    const second = indexOf(run, '--profile replicas up -d server-2')
    assert.ok(indexOf(run, ' up -d postgres') < indexOf(run, ' up -d identity proxy'))
    assert.ok(indexOf(run, ' up -d identity proxy') < first)
    assert.ok(first < second)
    assert.ok(second < indexOf(run, 'admin migrate'))
    assert.ok(indexOf(run, 'admin migrate') < indexOf(run, 'admin tenant create load --host load.axis.test'))
    assert.ok(indexOf(run, 'admin tenant create load') < k6Index(run))
  })

  test('an unknown argument exits with code 2 and calls no docker', () => {
    const run = runLoad(['--bogus'])

    assert.equal(run.status, 2, run.stdout + run.stderr)
    assert.match(run.stderr, /Usage: scripts\/load\.sh \[--keep\] \[-- <k6 arguments>\]/)
    assert.deepEqual(
      run.calls.filter((call) => call.startsWith('docker ')),
      [],
    )
    assert.deepEqual(run.calls, [])
  })

  test('the stack is removed after the k6 run', () => {
    const run = runLoad()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.ok(indexOf(run, ' down -v ') > k6Index(run))
    assert.equal(run.left, false)
  })

  test('--keep leaves the stack in place and prints the command that removes it', () => {
    const run = runLoad(['--keep'])

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.equal(run.calls.some((call) => call.includes(' down ')), false, run.calls.join('\n'))
    assert.match(run.stdout, /down -v --remove-orphans/)
  })

  test('arguments after -- go to k6 before the script', () => {
    const run = runLoad(['--', '--vus', '5'])

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.match(run.calls[k6Index(run)], / k6 run --vus 5 \/scripts\/placeholder\.js$/)
  })

  test('without arguments after --, k6 gets the script alone', () => {
    const run = runLoad()

    assert.match(run.calls[k6Index(run)], / k6 run \/scripts\/placeholder\.js$/)
  })

  test('a failing k6 run gives its exit code, and the stack is still removed', () => {
    const run = runLoad([], { STUB_FAIL_K6: '1' })

    assert.equal(run.status, 3, run.stdout + run.stderr)
    assert.ok(indexOf(run, ' down -v ') > k6Index(run))
  })
})

describe('scripts/load.sh writes the application and activates it in the load tenant', () => {
  const activate = 'admin activate /apps/load --tenant load'

  test('the generator runs with seed 1 into the run folder before the activation', () => {
    const run = runLoad()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    const generator = indexOf(run, 'run --project tests/Axis.LoadData')
    assert.match(run.calls[generator], / -- \S+\/artifacts\/load\/apps\/load --seed 1$/)
    assert.ok(generator < indexOf(run, activate))
  })

  test('the activation comes after tenant create and before the k6 run', () => {
    const run = runLoad()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.ok(indexOf(run, 'admin tenant create load') < indexOf(run, activate))
    assert.ok(indexOf(run, activate) < k6Index(run))
  })

  test('the realm is prepared and 50 users are created before the activation', () => {
    const run = runLoad()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    const activation = indexOf(run, activate)
    const posts = run.calls.map((call, index) => ({ call, index })).filter(({ call }) => / -X POST .*\/admin\/realms\/load\/users$/.test(call))
    assert.equal(posts.length, 50)
    for (const { index } of posts) assert.ok(index < activation)
    assert.ok(indexOf(run, ' -X PUT ') < activation)
    assert.ok(indexOf(run, '/protocol-mappers/models') < activation)
    for (const call of run.calls.filter((call) => call.startsWith('curl ') && call.includes('/admin/realms/'))) {
      assert.match(call, / -H @\S+\/realm\/authorization /, call)
      assert.doesNotMatch(call, /stub-token/, call)
    }
  })

  test('--keep leaves users.json with 50 users, each with a password and a distinct authenticator secret', () => {
    const run = runLoad(['--keep'])

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.equal(run.users.length, 50)
    run.users.forEach((user, index) => assert.equal(user.username, `load-user-${String(index + 1).padStart(2, '0')}`))
    for (const user of run.users) {
      assert.ok(user.password.length > 0)
      assert.match(user.secret, /^[A-Z2-7]{32}$/)
    }
    assert.equal(new Set(run.users.map((user) => user.secret)).size, 50)
  })

  test('the same seed gives the same secrets and another seed gives different ones', () => {
    assert.deepEqual(loadUsers(1, 'p'), loadUsers(1, 'p'))
    assert.notDeepEqual(
      loadUsers(1, 'p').map((user) => user.secret),
      loadUsers(2, 'p').map((user) => user.secret),
    )
  })
})
