import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { loadUsers } from './lib/load-realm.mjs'
import { writeLoadStack } from './lib/load-stack.mjs'

// Runs scripts/load.sh in a temporary copy of the files it needs, with docker, npm and curl replaced by stubs that log
// their calls. The real node and openssl write the certificates, the secret files and the override file.
const root = fileURLToPath(new URL('..', import.meta.url))

const log = (name) => `echo "${name} $*" >> "$STUB_LOG"\n`

const summaryJson = ({ reads = 241, writes = 412, errors = 0.0002 } = {}) =>
  JSON.stringify({
    metrics: {
      'http_req_duration{objective:reads}': { 'p(95)': reads },
      'http_req_duration{objective:writes}': { 'p(95)': writes },
      server_errors: { value: errors },
    },
  })

const stubs = {
  // The k6 call exits with STUB_K6_STATUS (default 0). When STUB_SUMMARY is set, it writes that summary into the folder
  // mounted at /results, as k6 does with --summary-export. The stop of the worker writes the metrics file, as the
  // collector does with its last export.
  docker: `#!/usr/bin/env bash
${log('docker')}if [[ "$*" == *" k6 run "* ]]; then
  for arg in "$@"; do
    if [ -n "\${STUB_SUMMARY:-}" ] && [[ "$arg" == *:/results ]]; then printf '%s' "$STUB_SUMMARY" > "\${arg%:/results}/summary.json"; fi
  done
  exit "\${STUB_K6_STATUS:-0}"
fi
if [[ "$*" == *" up -d server worker"* && -e artifacts/load-results/worker-metrics.json ]]; then echo "metrics-file-before-worker" >> "$STUB_LOG"; fi
if [[ "$*" == *" stop worker"* ]]; then printf '{}' > artifacts/load-results/worker-metrics.json; fi
exit 0
`,
  // The tenant answers 401, as the wait for its host expects. The admin API of the identity provider answers by the URL,
  // which is the last argument. Anything else gives no output.
  curl: `#!/usr/bin/env bash
${log('curl')}case "$*" in *'%{http_code}'*) printf 401 ;; esac
# The body arrives on stdin. It is kept in a second log, for the tests that check what a request sends.
[[ " $* " == *' --data-binary @- '* ]] && { echo "$*"; cat; echo; } >> "$STUB_LOG.body"
case "\${@: -1}" in
  */openid-connect/token) printf '{"access_token":"stub-token"}' ;;
  *'/clients?clientId=axis') printf '[{"id":"client-1","clientId":"axis","secret":"s"}]' ;;
  */clients/client-1/roles)
    if [[ "$*" != *' -X POST '* ]]; then
      printf '[{"id":"role-1","name":"clerk"},{"id":"role-2","name":"reviewer"},{"id":"role-3","name":"auditor"},{"id":"role-4","name":"load-caller"}]'
    fi ;;
  */clients/caller-1/service-account-user) printf '{"id":"service-user-1"}' ;;
  */admin/realms/load/clients) printf 'HTTP/2 201\r\nlocation: https://id.axis.test/admin/realms/load/clients/caller-1\r\n\r\n' ;;
  */admin/realms/load/users) printf 'HTTP/2 201\r\nlocation: https://id.axis.test/admin/realms/load/users/user-%s\r\n\r\n' "$RANDOM" ;;
esac
exit 0
`,
  // The generator writes the roles of the application into the folder after --.
  dotnet: `#!/usr/bin/env bash
${log('dotnet')}if [ "$1" = run ]; then
  for role in clerk reviewer auditor load-caller; do
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

function runLoad(args = [], extraEnv = {}, setup = () => {}) {
  const dir = mkdtempSync(join(tmpdir(), 'axis-load-'))
  try {
    for (const file of [
      'scripts/load.sh',
      'scripts/lib/load-stack.mjs',
      'scripts/lib/load-realm.mjs',
      'scripts/lib/load-objectives.mjs',
      'samples/keycloak/axis-realm.json',
      'scripts/lib/postgres-tls.mjs',
      'scripts/lib/retry.sh',
      'deploy/compose/compose.yaml',
      'tests/load/objectives.js',
      'tests/load/lib/totp.js',
    ]) {
      mkdirSync(dirname(join(dir, file)), { recursive: true })
      copyFileSync(join(root, file), join(dir, file))
    }
    setup(dir)
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
    const callerFile = join(dir, 'artifacts/load/k6/caller.json')
    const caller = existsSync(callerFile) ? JSON.parse(readFileSync(callerFile, 'utf8')) : undefined
    const bodies = existsSync(`${env.STUB_LOG}.body`) ? readFileSync(`${env.STUB_LOG}.body`, 'utf8') : ''
    const calls = existsSync(env.STUB_LOG) ? readFileSync(env.STUB_LOG, 'utf8').trimEnd().split('\n') : []
    const resultsFolder = join(dir, 'artifacts/load-results')
    const results = existsSync(resultsFolder)
    const resultsMode = results ? statSync(resultsFolder).mode & 0o777 : undefined
    return {
      ...result,
      calls,
      bodies,
      dir,
      users,
      caller,
      left: existsSync(join(dir, 'artifacts/load/override.json')),
      results,
      resultsMode,
      metricsKept: existsSync(join(resultsFolder, 'worker-metrics.json')),
    }
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
const k6Index = (run) => indexOf(run, ' k6 run ')

describe('scripts/load.sh starts the reference stack and runs the objectives scenario', () => {
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

  test('arguments after -- go to k6 after the summary flag and before the script', () => {
    const run = runLoad(['--', '--vus', '5'])

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.match(run.calls[k6Index(run)], / -v \S+\/artifacts\/load-results:\/results /)
    assert.match(run.calls[k6Index(run)], / k6 run --summary-export=\/results\/summary\.json --vus 5 \/scripts\/objectives\.js$/)
  })

  test('without arguments after --, k6 gets the summary flag and the script', () => {
    const run = runLoad()

    assert.match(run.calls[k6Index(run)], / -v \S+\/artifacts\/load-results:\/results /)
    assert.match(run.calls[k6Index(run)], / k6 run --summary-export=\/results\/summary\.json \/scripts\/objectives\.js$/)
  })

  test('a failing k6 run that writes no summary gives its exit code, and the stack is still removed', () => {
    const run = runLoad([], { STUB_K6_STATUS: '3' })

    assert.equal(run.status, 3, run.stdout + run.stderr)
    assert.ok(indexOf(run, ' down -v ') > k6Index(run))
  })

  test('a summary that meets the objectives gives exit code 0, prints them and removes the stack', () => {
    const run = runLoad([], { STUB_SUMMARY: summaryJson() })

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.match(run.stdout, /Reads p95 241 ms, objective under 300 ms: met/)
    assert.match(run.stdout, /Writes p95 412 ms, objective under 500 ms: met/)
    assert.match(run.stdout, /Server errors 0.02%, objective under 0.1%: met/)
    assert.ok(indexOf(run, ' down -v ') > k6Index(run))
  })

  test('a summary that misses an objective gives exit code 1, names it and removes the stack', () => {
    const run = runLoad([], { STUB_SUMMARY: summaryJson({ reads: 400 }) })

    assert.equal(run.status, 1, run.stdout + run.stderr)
    assert.match(run.stdout, /Reads p95 400 ms, objective under 300 ms: missed/)
    assert.ok(indexOf(run, ' down -v ') > k6Index(run))
  })

  test('a missed objective gives exit code 1 even when k6 also fails', () => {
    const run = runLoad([], { STUB_SUMMARY: summaryJson({ reads: 400 }), STUB_K6_STATUS: '99' })

    assert.equal(run.status, 1, run.stdout + run.stderr)
  })

  test('a summary that meets the objectives gives k6 exit code when k6 fails', () => {
    const run = runLoad([], { STUB_SUMMARY: summaryJson(), STUB_K6_STATUS: '99' })

    assert.equal(run.status, 99, run.stdout + run.stderr)
  })

  test('a summary left by an earlier run is removed before k6 runs', () => {
    const run = runLoad([], {}, (dir) => {
      mkdirSync(join(dir, 'artifacts/load-results'), { recursive: true })
      writeFileSync(join(dir, 'artifacts/load-results/summary.json'), summaryJson({ reads: 400 }))
    })

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.doesNotMatch(run.stdout, /Reads p95/)
  })

  test('without --keep, artifacts/load-results stays and anyone can write to it', () => {
    const run = runLoad([], { STUB_SUMMARY: summaryJson() })

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.equal(run.results, true)
    assert.equal(run.resultsMode, 0o777)
  })
})

describe('scripts/load.sh collects the metrics of the worker', () => {
  test('the worker stops after the k6 run and before the stack is removed, and the metrics file is kept', () => {
    const run = runLoad()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    const stop = indexOf(run, ' stop worker collector')
    assert.ok(stop > k6Index(run))
    assert.ok(stop < indexOf(run, ' down -v '))
    assert.equal(run.metricsKept, true)
  })

  test('the worker stops even when k6 fails, and the exit code is k6\'s', () => {
    const run = runLoad([], { STUB_K6_STATUS: '3' })

    assert.equal(run.status, 3, run.stdout + run.stderr)
    assert.ok(indexOf(run, ' stop worker collector') > k6Index(run))
  })

  test('a metrics file left by an earlier run is removed before the worker starts', () => {
    const run = runLoad([], {}, (dir) => {
      mkdirSync(join(dir, 'artifacts/load-results'), { recursive: true })
      writeFileSync(join(dir, 'artifacts/load-results/worker-metrics.json'), 'stale')
    })

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.ok(indexOf(run, ' up -d server worker') >= 0)
    assert.equal(run.calls.includes('metrics-file-before-worker'), false, run.calls.join('\n'))
  })

  describe('the load stack', () => {
    const withStack = (check) => {
      const dir = mkdtempSync(join(tmpdir(), 'axis-load-stack-'))
      try {
        const folder = join(dir, 'run')
        writeLoadStack(folder, { results: join(dir, 'artifacts/load-results') })
        check({
          folder,
          dir,
          override: JSON.parse(readFileSync(join(folder, 'override.json'), 'utf8')),
          collector: JSON.parse(readFileSync(join(folder, 'collector.json'), 'utf8')),
        })
      } finally {
        rmSync(dir, { recursive: true, force: true })
      }
    }

    test('the worker exports its metrics over https to the collector and trusts the run\'s authority', () => {
      withStack(({ folder, override }) => {
        const { worker } = override.services
        assert.match(worker.environment.OTEL_EXPORTER_OTLP_METRICS_ENDPOINT, /^https:\/\/collector:4318\//)
        assert.equal(worker.environment.OTEL_EXPORTER_OTLP_PROTOCOL, 'http/protobuf')
        assert.equal(worker.environment.OTEL_METRIC_EXPORT_INTERVAL, '5000')
        assert.equal(worker.environment.OTEL_EXPORTER_OTLP_ENDPOINT, undefined)
        assert.equal(worker.environment.SSL_CERT_FILE, '/run/test-ca/ca.crt')
        assert.ok(worker.volumes.includes(`${join(folder, 'ca')}:/run/test-ca:ro`), worker.volumes.join('\n'))
        assert.ok(worker.depends_on.includes('collector'))
      })
    })

    test('the collector certificate is signed by the authority and names the collector', () => {
      withStack(({ folder, collector, override }) => {
        const certificate = join(folder, 'collector-tls/tls.crt')
        const verify = spawnSync('openssl', ['verify', '-CAfile', join(folder, 'ca/ca.crt'), certificate], { encoding: 'utf8' })
        assert.equal(verify.status, 0, verify.stdout + verify.stderr)
        const names = spawnSync('openssl', ['x509', '-noout', '-ext', 'subjectAltName', '-in', certificate], { encoding: 'utf8' })
        assert.equal(names.status, 0, names.stdout + names.stderr)
        assert.match(names.stdout, /DNS:collector\b/)

        const { http } = collector.receivers.otlp.protocols
        assert.equal(http.endpoint, '0.0.0.0:4318')
        const mount = override.services.collector.volumes.find((volume) => volume.endsWith(':/collector-tls:ro'))
        assert.equal(mount, `${join(folder, 'collector-tls')}:/collector-tls:ro`)
        assert.equal(http.tls.cert_file, '/collector-tls/tls.crt')
        assert.equal(http.tls.key_file, '/collector-tls/tls.key')
        assert.deepEqual(collector.service.pipelines.metrics, { receivers: ['otlp'], exporters: ['file'] })
      })
    })

    test('the collector writes the metrics into the results folder', () => {
      withStack(({ dir, folder, collector, override }) => {
        const { volumes } = override.services.collector
        assert.equal(collector.exporters.file.path, '/results/worker-metrics.json')
        assert.ok(volumes.includes(`${join(dir, 'artifacts/load-results')}:/results`), volumes.join('\n'))
        assert.ok(volumes.includes(`${join(folder, 'collector.json')}:/etc/otelcol-contrib/config.yaml:ro`), volumes.join('\n'))
        assert.match(override.services.collector.image, /^otel\/opentelemetry-collector-contrib:\d+\.\d+\.\d+$/)
        assert.equal(override.services.collector.profiles, undefined)
      })
    })
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

  test('the load-caller client with service accounts and the audience mapper is created before the k6 run', () => {
    const run = runLoad()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    const post = run.calls.findIndex((call) => / -X POST .*\/admin\/realms\/load\/clients$/.test(call))
    assert.notEqual(post, -1, run.calls.join('\n'))
    assert.ok(post < k6Index(run))
    const body = run.bodies.split('\n').find((line) => line.startsWith('{') && line.includes('"clientId":"load-caller"'))
    assert.ok(body, run.bodies)
    const client = JSON.parse(body)
    assert.equal(client.serviceAccountsEnabled, true)
    assert.equal(client.publicClient, false)
    assert.ok(client.secret.length > 0)
    assert.ok(client.protocolMappers.some((mapper) => mapper.name === 'roles'))
    assert.ok(client.protocolMappers.some((mapper) => mapper.config['included.custom.audience'] === 'axis-load-endpoints'))
  })

  test('the tenant accepts the endpoint audience before the k6 run', () => {
    const run = runLoad()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.ok(indexOf(run, 'admin tenant oidc set load --endpoint-audience axis-load-endpoints') < k6Index(run))
  })

  test('--keep leaves caller.json with the client id load-caller and a secret', () => {
    const run = runLoad(['--keep'])

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.equal(run.caller.clientId, 'load-caller')
    assert.ok(run.caller.secret.length > 0)
  })

  test('the load-caller role goes to the service account and not to the 50 users', () => {
    const run = runLoad()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    const mappings = run.calls.filter((call) => / -X POST .*\/role-mappings\/clients\/client-1$/.test(call))
    assert.equal(mappings.length, 51)
    assert.equal(mappings.filter((call) => call.includes('/users/service-user-1/')).length, 1)
    const bodies = run.bodies.split('\n').filter((line) => line.startsWith('[{"id":"role-'))
    assert.equal(bodies.length, 51)
    assert.equal(bodies.filter((body) => body.includes('role-4')).length, 1)
    assert.equal(bodies.filter((body) => !body.includes('role-4')).length, 50)
  })

  test('the same seed gives the same secrets and another seed gives different ones', () => {
    assert.deepEqual(loadUsers(1, 'p'), loadUsers(1, 'p'))
    assert.notDeepEqual(
      loadUsers(1, 'p').map((user) => user.secret),
      loadUsers(2, 'p').map((user) => user.secret),
    )
  })
})
