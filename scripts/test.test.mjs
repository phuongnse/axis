import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import {
  copyFileSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// Runs scripts/test.sh in a temporary copy of the files it needs. dotnet and npm are stubs that write
// passing JUnit files. node logs `node --test` calls and runs everything else, so no nested suite runs.
const root = fileURLToPath(new URL('..', import.meta.url))

const log = (name) => `echo "${name} $*" >> "$STUB_LOG"\n`
const passing = '<testsuites><testsuite name="s" tests="1"><testcase name="t" time="0"/></testsuite></testsuites>'

const stubs = {
  dotnet: `#!/usr/bin/env bash
${log('dotnet')}if [ "$1" = test ]; then
  name='' dir=''
  while [ $# -gt 0 ]; do
    case "$1" in
      --report-xunit-junit-filename) name="$2"; shift ;;
      --results-directory) dir="$2"; shift ;;
    esac
    shift
  done
  mkdir -p "$dir"
  echo '${passing}' > "$dir/$name"
fi
exit 0
`,
  npm: `#!/usr/bin/env bash
${log('npm')}for argument in "$@"; do
  case "$argument" in
    --outputFile.junit=*) echo '${passing}' > "\${argument#--outputFile.junit=}" ;;
  esac
done
`,
  node: `#!/usr/bin/env bash
${log('node')}if [ "$1" = --test ]; then exit 0; fi
exec "${process.execPath}" "$@"
`,
}

function runTest(extraEnv = {}) {
  const dir = mkdtempSync(join(tmpdir(), 'axis-test-'))
  try {
    for (const file of [
      'scripts/test.sh',
      'scripts/lib/test-results.sh',
      'scripts/lib/tools.sh',
      'scripts/lib/retry.sh',
      'scripts/lib/test-shard.mjs',
      'scripts/lib/shards.mjs',
      'scripts/test-report.mjs',
    ]) {
      mkdirSync(dirname(join(dir, file)), { recursive: true })
      copyFileSync(join(root, file), join(dir, file))
    }
    for (const script of ['install-modules.sh', 'install-sample-templates.sh']) {
      writeFileSync(join(dir, 'scripts/lib', script), '#!/usr/bin/env bash\nexit 0\n')
    }
    mkdirSync(join(dir, 'web/src'), { recursive: true })
    writeFileSync(join(dir, 'web/src/a.test.ts'), '')
    writeFileSync(join(dir, 'web/src/b.test.ts'), '')
    writeFileSync(join(dir, 'web/vitest-durations.json'), JSON.stringify({ 'src/a.test.ts': 10, 'src/b.test.ts': 20 }))

    const bin = join(dir, 'bin')
    mkdirSync(bin)
    for (const [name, body] of Object.entries(stubs)) {
      writeFileSync(join(bin, name), body, { mode: 0o755 })
    }

    const env = { ...process.env, PATH: `${bin}:${process.env.PATH}`, STUB_LOG: join(dir, 'log') }
    for (const name of [
      'AXIS_TEST_SHARD',
      'NEXKIT_SHARD',
      'AXIS_TEST_RESULTS_DIR',
      'NODE_TEST_CONTEXT',
      'NEXKIT_BASE_SHA',
      'NEXKIT_VERIFIED_SHA',
      'NEXKIT_CHECK_VERIFIED_SHA',
      'GITHUB_ACTIONS',
      'GITHUB_STEP_SUMMARY',
      'CI',
    ]) {
      delete env[name]
    }
    Object.assign(env, extraEnv)

    const result = spawnSync('bash', ['scripts/test.sh'], { cwd: dir, encoding: 'utf8', timeout: 60_000, env })
    const resultsDir = join(dir, 'artifacts/test-results')
    const calls = existsSync(env.STUB_LOG) ? readFileSync(env.STUB_LOG, 'utf8').trimEnd().split('\n') : []
    const files = existsSync(resultsDir) ? readdirSync(resultsDir) : []
    return { ...result, calls, files }
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
}

const vitestCalls = (run) => run.calls.filter((call) => call.startsWith('npm test '))
const vitestFiles = (call) => call.split(' ').filter((token) => token.startsWith('src/'))
const dotnetCalls = (run) => run.calls.filter((call) => call.startsWith('dotnet '))
const nodeTestCalls = (run) => run.calls.filter((call) => call.startsWith('node --test '))

describe('scripts/test.sh runs the whole suite, or one shard of it with AXIS_TEST_SHARD', () => {
  test('shard 1 of 3 runs the .NET and script tests and no Vitest file', () => {
    const run = runTest({ AXIS_TEST_SHARD: '1/3' })

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.ok(dotnetCalls(run).length > 0)
    assert.ok(nodeTestCalls(run).some((call) => call.includes('scripts/test-shard.test.mjs')))
    assert.deepEqual(vitestCalls(run), [])
    assert.ok(run.files.includes('Axis.Server.Tests.xml'))
    assert.ok(!run.files.some((name) => name.startsWith('web')))
  })

  test('shards 2 and 3 of 3 each run one Vitest file, and no .NET or script test', () => {
    const second = runTest({ AXIS_TEST_SHARD: '2/3' })
    assert.equal(second.status, 0, second.stdout + second.stderr)
    assert.deepEqual(vitestCalls(second).map(vitestFiles), [['src/b.test.ts']])
    assert.deepEqual(dotnetCalls(second), [])
    assert.deepEqual(nodeTestCalls(second), [])
    assert.deepEqual(second.files, ['web-2.xml'])

    const third = runTest({ AXIS_TEST_SHARD: '3/3' })
    assert.equal(third.status, 0, third.stdout + third.stderr)
    assert.deepEqual(vitestCalls(third).map(vitestFiles), [['src/a.test.ts']])
    assert.deepEqual(third.files, ['web-3.xml'])
  })

  test('without a shard, or with 1 of 1, the whole suite runs in one Vitest call', () => {
    for (const env of [{}, { AXIS_TEST_SHARD: '1/1' }]) {
      const run = runTest(env)
      assert.equal(run.status, 0, run.stdout + run.stderr)
      assert.ok(dotnetCalls(run).length > 0)
      assert.ok(nodeTestCalls(run).length > 0)
      assert.deepEqual(vitestCalls(run).map(vitestFiles), [[]])
      assert.ok(run.files.includes('web.xml'))
    }
  })

  test('NEXKIT_SHARD runs a shard like AXIS_TEST_SHARD, and AXIS_TEST_SHARD wins when both are set', () => {
    const fromNexkit = runTest({ NEXKIT_SHARD: '2/3' })
    assert.equal(fromNexkit.status, 0, fromNexkit.stdout + fromNexkit.stderr)
    assert.deepEqual(vitestCalls(fromNexkit).map(vitestFiles), [['src/b.test.ts']])
    assert.deepEqual(fromNexkit.files, ['web-2.xml'])

    const both = runTest({ AXIS_TEST_SHARD: '3/3', NEXKIT_SHARD: '2/3' })
    assert.equal(both.status, 0, both.stdout + both.stderr)
    assert.deepEqual(vitestCalls(both).map(vitestFiles), [['src/a.test.ts']])
    assert.deepEqual(both.files, ['web-3.xml'])
  })

  test('a malformed shard exits with code 2 before any test runs', () => {
    for (const shard of ['4/3', '0/3', 'abc']) {
      const run = runTest({ AXIS_TEST_SHARD: shard })
      assert.equal(run.status, 2, run.stdout + run.stderr)
      assert.match(run.stderr, new RegExp(`AXIS_TEST_SHARD must be a shard such as 1/3, not '${shard}'`))
      assert.deepEqual(run.calls, [])
    }
    const fromNexkit = runTest({ NEXKIT_SHARD: '4/3' })
    assert.equal(fromNexkit.status, 2)
    assert.match(fromNexkit.stderr, /NEXKIT_SHARD must be a shard such as 1\/3, not '4\/3'/)
    assert.deepEqual(fromNexkit.calls, [])
  })

  test('a shard past the Vitest files exits with code 2 and names the fault', () => {
    const run = runTest({ AXIS_TEST_SHARD: '5/5' })
    assert.equal(run.status, 2, run.stdout + run.stderr)
    assert.match(run.stderr, /Shard 5\/5 has no Vitest file\. Use fewer shards\./)
    assert.deepEqual(dotnetCalls(run), [])
  })
})
