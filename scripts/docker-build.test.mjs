import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { after, before, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// Runs buildImage and runDocker from scripts/lib/docker-build.mjs with docker replaced by a stub. The stub fails the
// first STUB_FAIL_TIMES calls with STUB_FAIL_STATUS and logs STUB_EXTRA after the arguments when it is set.
const helper = fileURLToPath(new URL('./lib/docker-build.mjs', import.meta.url))

const stub = `#!/usr/bin/env bash
echo "$*\${STUB_EXTRA:+ $STUB_EXTRA}" >> "$STUB_LOG"
count=$(( $(cat "$STUB_COUNT" 2>/dev/null || echo 0) + 1 ))
echo "$count" > "$STUB_COUNT"
if [ "$count" -le "$STUB_FAIL_TIMES" ]; then exit "$STUB_FAIL_STATUS"; fi
exit 0
`

let folder

before(() => {
  folder = mkdtempSync(join(tmpdir(), 'axis-docker-build-'))
  mkdirSync(join(folder, 'bin'))
  writeFileSync(join(folder, 'bin', 'docker'), stub)
  chmodSync(join(folder, 'bin', 'docker'), 0o755)
})

after(() => rmSync(folder, { recursive: true, force: true }))

const run = (failTimes, status, script) => {
  rmSync(join(folder, 'count'), { force: true })
  rmSync(join(folder, 'log'), { force: true })
  const result = spawnSync('node', ['--input-type=module', '-e', script], {
    encoding: 'utf8',
    env: {
      ...process.env,
      PATH: `${join(folder, 'bin')}:${process.env.PATH}`,
      AXIS_RETRY_DELAY_SECONDS: '0',
      STUB_LOG: join(folder, 'log'),
      STUB_COUNT: join(folder, 'count'),
      STUB_FAIL_TIMES: String(failTimes),
      STUB_FAIL_STATUS: String(status),
    },
  })
  const calls = readFileSync(join(folder, 'log'), 'utf8').split('\n').filter(Boolean)
  return { ...result, calls }
}

const build = (failTimes, status = 1) =>
  run(
    failTimes,
    status,
    `import { buildImage } from ${JSON.stringify(helper)}
await buildImage({ target: 'axis-server', version: '1.2.3', tag: 'axis-server:test' })`,
  )

const compose = (failTimes, status = 1) =>
  run(
    failTimes,
    status,
    `import { runDocker } from ${JSON.stringify(helper)}
await runDocker(['compose', '-p', 'axis-smoke', 'build', 'server'], { env: { ...process.env, STUB_EXTRA: 'from-env' } })`,
  )

describe('buildImage', () => {
  test('builds the image on the second attempt and logs the failed one', () => {
    const result = build(1, 3)
    assert.equal(result.status, 0, result.stderr)
    assert.equal(result.calls.length, 2)
    assert.equal(result.calls[0], 'build --target axis-server --build-arg AXIS_VERSION=1.2.3 -t axis-server:test .')
    assert.match(result.stderr, /Attempt 1 of 3 failed with status 3: docker build --target axis-server /)
    assert.doesNotMatch(result.stderr, /Attempt 2 of 3/)
  })

  test('stops after three attempts and names the target and the last status', () => {
    const result = build(99, 7)
    assert.notEqual(result.status, 0)
    assert.equal(result.calls.length, 3)
    assert.match(result.stderr, /Attempt 2 of 3 failed with status 7/)
    assert.match(result.stderr, /docker build --target axis-server failed after 3 attempts with status 7/)
  })

  test('calls docker once and logs no retry when the build succeeds', () => {
    const result = build(0)
    assert.equal(result.status, 0, result.stderr)
    assert.equal(result.calls.length, 1)
    assert.doesNotMatch(result.stderr, /Attempt/)
  })
})

describe('runDocker', () => {
  test('runs a compose command again with its arguments and environment', () => {
    const result = compose(1)
    assert.equal(result.status, 0, result.stderr)
    assert.equal(result.calls.length, 2)
    assert.equal(result.calls[0], 'compose -p axis-smoke build server from-env')
    assert.match(result.stderr, /Attempt 1 of 3 failed with status 1: docker compose -p axis-smoke build server/)
  })

  test('stops after three attempts and names the command and the last status', () => {
    const result = compose(99, 7)
    assert.notEqual(result.status, 0)
    assert.equal(result.calls.length, 3)
    assert.match(result.stderr, /docker compose -p axis-smoke build server failed after 3 attempts with status 7/)
  })
})
