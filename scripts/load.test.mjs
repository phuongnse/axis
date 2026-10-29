import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

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
  // The tenant answers 401, as the wait for its host expects.
  curl: `#!/usr/bin/env bash
${log('curl')}case "$*" in *'%{http_code}'*) printf 401 ;; esac
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
    const calls = existsSync(env.STUB_LOG) ? readFileSync(env.STUB_LOG, 'utf8').trimEnd().split('\n') : []
    return { ...result, calls, dir, left: existsSync(join(dir, 'artifacts/load/override.json')) }
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
