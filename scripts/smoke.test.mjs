import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// Runs scripts/smoke.sh in a temporary copy of the files it needs, with each smoke test replaced by a
// test that passes, or fails when it is named in FAILING, and git replaced by a stub that lists the
// changed paths in FAKE_CHANGED.
const root = fileURLToPath(new URL('..', import.meta.url))
const suites = { 'dev-script': 'dev', compose: 'compose', images: 'images', onprem: 'onprem' }

function runSmoke({ changed, failing = [] } = {}) {
  const dir = mkdtempSync(join(tmpdir(), 'axis-smoke-'))
  try {
    for (const file of [
      'scripts/smoke.sh',
      'scripts/lib/test-results.sh',
      'scripts/lib/affected.mjs',
      'scripts/lib/install-sample-modules.sh',
      'scripts/test-report.mjs',
    ]) {
      mkdirSync(dirname(join(dir, file)), { recursive: true })
      copyFileSync(join(root, file), join(dir, file))
    }
    mkdirSync(join(dir, 'samples/modules/departments'), { recursive: true })
    writeFileSync(join(dir, 'samples/modules/departments/module.json'), '{"id":"departments","version":"1.0.0"}')
    for (const [suite, file] of Object.entries(suites)) {
      const body = failing.includes(suite) ? 'assert.fail()' : ''
      writeFileSync(
        join(dir, `scripts/${file}.test.mjs`),
        `import assert from 'node:assert'\nimport { test } from 'node:test'\ntest('${suite}', () => {${body}})\n`,
      )
    }
    const bin = join(dir, 'bin')
    mkdirSync(bin)
    writeFileSync(join(bin, 'git'), `#!/bin/sh\n[ "$1" = diff ] && printf '%s\\n' "$FAKE_CHANGED"\nexit 0\n`, {
      mode: 0o755,
    })

    const env = { ...process.env, PATH: `${bin}:${process.env.PATH}`, FAKE_CHANGED: changed ?? '' }
    // NODE_TEST_CONTEXT would make each suite's node --test skip its file.
    for (const name of ['NODE_TEST_CONTEXT', 'NEXKIT_BASE_SHA', 'GITHUB_ACTIONS', 'GITHUB_STEP_SUMMARY', 'AXIS_TEST_RESULTS_DIR']) {
      delete env[name]
    }
    if (changed !== undefined) env.NEXKIT_BASE_SHA = 'abc'

    const result = spawnSync('bash', ['scripts/smoke.sh'], { cwd: dir, encoding: 'utf8', timeout: 60_000, env })
    const written = Object.keys(suites).filter((suite) => existsSync(join(dir, 'artifacts/test-results', `${suite}.xml`)))
    const modules = existsSync(join(dir, 'artifacts/modules/departments/1.0.0/module.json'))
    return { ...result, written, modules }
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
}

describe('scripts/smoke.sh', () => {
  test('runs every smoke test and installs the sample modules that the on-premises test reads', () => {
    const run = runSmoke()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.deepEqual(run.written, Object.keys(suites))
    assert.ok(run.modules)
    for (const suite of Object.keys(suites)) assert.match(run.stdout, new RegExp(`^\\| ${suite} \\| 1 \\| 0 \\| 0 \\| `, 'm'))
  })

  test('runs the other smoke tests after one fails, and fails', () => {
    const run = runSmoke({ failing: ['compose'] })

    assert.equal(run.status, 1)
    assert.deepEqual(run.written, Object.keys(suites))
    assert.match(run.stdout, /^\| compose \| 0 \| 1 \| 0 \| /m)
  })

  test('skips every smoke test under NexKit for a docs change', () => {
    const run = runSmoke({ changed: 'docs/x.md' })

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.deepEqual(run.written, [])
    for (const suite of Object.keys(suites)) {
      assert.ok(run.stdout.includes(`${suite} (skipped: not affected by this change)`), suite)
    }
  })

  test('runs every smoke test under NexKit for a server change', () => {
    const run = runSmoke({ changed: 'src/Axis.Server/Program.cs' })

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.deepEqual(run.written, Object.keys(suites))
  })
})
