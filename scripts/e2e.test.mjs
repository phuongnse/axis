import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// Runs scripts/e2e.sh in a temporary copy of the files it needs, with docker, curl, dotnet and npm
// replaced by stubs, and with stub build scripts. Passing smoke tests sit beside it, so a run that
// started one would write its results. Each stub logs its call.
const root = fileURLToPath(new URL('..', import.meta.url))

const log = (name) => `echo "${name} $*" >> "$STUB_LOG"\n`

const stubs = {
  docker: `#!/usr/bin/env bash
${log('docker')}case "$1" in
  run) echo stub-container ;;
  port) echo 127.0.0.1:5432 ;;
  inspect) echo true ;;
esac
exit 0
`,
  curl: `#!/usr/bin/env bash
${log('curl')}exit 0
`,
  // The worker and the publisher report every tenant ready, then run until the script stops them.
  dotnet: `#!/usr/bin/env bash
${log('dotnet')}case "$*" in
  *Axis.Worker.dll*) echo 'Tenant default is ready for work'; echo 'Tenant journey is ready for work' ;;
  *Axis.Admin.dll*) echo 'default ready'; echo 'journey ready' ;;
esac
exec sleep 60
`,
  // The theme build exits at once. Playwright waits until the worker and the publisher have reported
  // ready, as the real run does, then writes one passing test to its JUnit file.
  npm: `#!/usr/bin/env bash
${log('npm')}if [ "$1 $2" = "run build:keycloak-theme" ]; then exit 0; fi
for _ in $(seq 1 100); do
  if grep -q 'journey is ready' "$AXIS_E2E_WORKER_LOG" 2>/dev/null &&
    grep -q 'journey ready' "$PWD/artifacts/logs/e2e-publisher.log" 2>/dev/null; then break; fi
  sleep 0.1
done
echo '<testsuites><testsuite name="s" tests="1"><testcase name="t" time="0"/></testsuite></testsuites>' > "$AXIS_E2E_JUNIT"
`,
}

function runE2e(shard, args = []) {
  const dir = mkdtempSync(join(tmpdir(), 'axis-e2e-'))
  try {
    for (const file of [
      'scripts/e2e.sh',
      'scripts/lib/test-results.sh',
      'scripts/lib/affected.mjs',
      'scripts/lib/e2e-shard.mjs',
      'scripts/test-report.mjs',
    ]) {
      mkdirSync(dirname(join(dir, file)), { recursive: true })
      copyFileSync(join(root, file), join(dir, file))
    }
    mkdirSync(join(dir, 'samples/apps/purchase-requests'), { recursive: true })
    // A long serial journey and two short specs: the split gives the journey the first shard.
    mkdirSync(join(dir, 'tests/e2e/specs'), { recursive: true })
    writeFileSync(join(dir, 'tests/e2e/specs/journey.spec.ts'), "test.describe.configure({ mode: 'serial' })\n")
    writeFileSync(join(dir, 'tests/e2e/specs/a.spec.ts'), '')
    writeFileSync(join(dir, 'tests/e2e/specs/b.spec.ts'), '')
    writeFileSync(
      join(dir, 'tests/e2e/spec-durations.json'),
      JSON.stringify({ 'specs/journey.spec.ts': 300, 'specs/a.spec.ts': 10, 'specs/b.spec.ts': 10 }),
    )
    for (const file of ['scripts/build.sh', 'scripts/design-system.sh']) {
      writeFileSync(join(dir, file), '#!/usr/bin/env bash\nexit 0\n', { mode: 0o755 })
    }
    for (const suite of ['compose', 'images', 'onprem']) {
      writeFileSync(
        join(dir, `scripts/${suite}.test.mjs`),
        `import { test } from 'node:test'\ntest('${suite}', () => {})\n`,
      )
    }
    const bin = join(dir, 'bin')
    mkdirSync(bin)
    for (const [name, body] of Object.entries(stubs)) {
      writeFileSync(join(bin, name), body, { mode: 0o755 })
    }

    const env = { ...process.env, PATH: `${bin}:${process.env.PATH}`, STUB_LOG: join(dir, 'log') }
    // NODE_TEST_CONTEXT would make a smoke suite's node --test skip its file.
    for (const name of [
      'NODE_TEST_CONTEXT',
      'NEXKIT_BASE_SHA',
      'NEXKIT_VERIFIED_SHA',
      'NEXKIT_CHECK_VERIFIED_SHA',
      'GITHUB_ACTIONS',
      'GITHUB_STEP_SUMMARY',
      'CI',
      'AXIS_TEST_RESULTS_DIR',
      'AXIS_E2E_SHARD',
    ]) {
      delete env[name]
    }
    if (shard !== undefined) env.AXIS_E2E_SHARD = shard

    const result = spawnSync('bash', ['scripts/e2e.sh', ...args], { cwd: dir, encoding: 'utf8', timeout: 60_000, env })
    const calls = existsSync(env.STUB_LOG) ? readFileSync(env.STUB_LOG, 'utf8').trimEnd().split('\n') : []
    const written = new Set(
      ['playwright', 'playwright-1', 'playwright-2', 'compose', 'images', 'onprem'].filter((name) =>
        existsSync(join(dir, 'artifacts/test-results', `${name}.xml`)),
      ),
    )
    return { ...result, calls, results: (name) => written.has(name) }
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
}

const playwrightCall = (run) => {
  const calls = run.calls.filter((call) => call.startsWith('npm test '))
  assert.equal(calls.length, 1, run.calls.join('\n'))
  return calls[0]
}

const smokeSuites = ['compose', 'images', 'onprem']

describe('scripts/e2e.sh runs Playwright, or one shard of it with AXIS_E2E_SHARD', () => {
  test('a shard runs the specs that the split by duration gives it, and no smoke test', () => {
    const run = runE2e('2/3')

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.match(playwrightCall(run), / -- specs\/a\.spec\.ts$/)
    assert.ok(run.results('playwright-2'))
    assert.ok(!run.results('playwright'))
    for (const suite of smokeSuites) assert.ok(!run.results(suite), suite)
    assert.match(run.stdout, /^\| playwright-2 \| 1 \| 0 \| 0 \| /m)
  })

  test('the first shard runs no smoke test either, because scripts/smoke.sh runs them', () => {
    const run = runE2e('1/3')

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.match(playwrightCall(run), / -- specs\/journey\.spec\.ts$/)
    assert.ok(run.results('playwright-1'))
    for (const suite of smokeSuites) assert.ok(!run.results(suite), suite)
    assert.match(run.stdout, /^\| playwright-1 \| 1 \| 0 \| 0 \| /m)
  })

  test("a shard with arguments leaves the split to Playwright's --shard over the matching tests", () => {
    const run = runE2e('2/3', ['tests/e2e/specs/a.spec.ts'])

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.match(playwrightCall(run), / -- --shard=2\/3 specs\/a\.spec\.ts$/)
  })

  test('without a shard the whole of Playwright runs, and no smoke test', () => {
    const run = runE2e()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.doesNotMatch(playwrightCall(run), /--shard/)
    assert.ok(run.results('playwright'))
    for (const suite of smokeSuites) assert.ok(!run.results(suite), suite)
  })

  test('the theme is built before Keycloak starts, and Keycloak mounts it read-only', () => {
    const run = runE2e()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    const build = run.calls.findIndex((call) => call.startsWith('npm run build:keycloak-theme '))
    const keycloak = run.calls.findIndex(
      (call) => call.startsWith('docker run ') && call.includes('/opt/keycloak/themes:ro'),
    )
    assert.ok(build >= 0, run.calls.join('\n'))
    assert.ok(keycloak > build, run.calls.join('\n'))
  })

  for (const shard of ['4/3', '0/3', 'abc']) {
    test(`a shard of ${shard} stops the script before any container starts`, () => {
      const run = runE2e(shard)

      assert.equal(run.status, 2)
      assert.match(run.stderr, /AXIS_E2E_SHARD must be a shard such as 1\/3/)
      assert.ok(!run.calls.some((call) => call.startsWith('docker ')), run.calls.join('\n'))
    })
  }
})
