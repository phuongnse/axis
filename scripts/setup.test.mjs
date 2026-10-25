import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// Runs scripts/setup.sh with dotnet, npm and npx replaced by stubs. The stubs fail the first
// STUB_FAIL_TIMES calls of each command line. The home cache is filled in, so no tool downloads.
const root = fileURLToPath(new URL('..', import.meta.url))
const toolsSh = readFileSync(join(root, 'scripts/lib/tools.sh'), 'utf8')
const version = (name) => toolsSh.match(new RegExp(`^${name}=(\\S+)$`, 'm'))[1]

const stub = `#!/usr/bin/env bash
line="$(basename "$0") $*"
echo "$line" >> "$STUB_LOG"
counter="$STUB_COUNTS/$(printf '%s' "$line" | tr -c 'A-Za-z0-9' '_')"
count=$(( $(cat "$counter" 2>/dev/null || echo 0) + 1 ))
echo "$count" > "$counter"
if [ "$count" -le "$STUB_FAIL_TIMES" ]; then exit "$STUB_FAIL_STATUS"; fi
exit 0
`

const commands = [
  'dotnet restore Axis.slnx',
  'dotnet tool restore',
  'npm ci --prefix web',
  'npm ci --prefix tests/e2e',
  'npx --prefix tests/e2e playwright install chromium',
]

function runSetup({ failTimes = 0, failStatus = 1 } = {}) {
  const dir = mkdtempSync(join(tmpdir(), 'axis-setup-'))
  try {
    const bin = join(dir, 'bin')
    const counts = join(dir, 'counts')
    const log = join(dir, 'log')
    const home = join(dir, 'home')
    mkdirSync(bin)
    mkdirSync(counts)
    for (const name of ['dotnet', 'npm', 'npx']) {
      writeFileSync(join(bin, name), stub, { mode: 0o755 })
    }

    const tools = join(home, '.cache', 'axis-tools')
    for (const file of [
      `helm-${version('HELM_VERSION')}/helm`,
      `kubeconform-${version('KUBECONFORM_VERSION')}/kubeconform`,
    ]) {
      mkdirSync(dirname(join(tools, file)), { recursive: true })
      writeFileSync(join(tools, file), '', { mode: 0o755 })
    }
    mkdirSync(join(tools, `kubernetes-schemas-${version('KUBERNETES_VERSION')}`), { recursive: true })

    const result = spawnSync('bash', ['scripts/setup.sh'], {
      cwd: root,
      encoding: 'utf8',
      env: {
        ...process.env,
        PATH: `${bin}:${process.env.PATH}`,
        HOME: home,
        CI: '',
        AXIS_RETRY_DELAY_SECONDS: '0',
        STUB_LOG: log,
        STUB_COUNTS: counts,
        STUB_FAIL_TIMES: String(failTimes),
        STUB_FAIL_STATUS: String(failStatus),
      },
    })
    const ran = existsSync(log) ? readFileSync(log, 'utf8').trimEnd().split('\n') : []
    return { ...result, ran }
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
}

describe('scripts/setup.sh retries its network steps', () => {
  test('each step that fails once succeeds on its second try', () => {
    const result = runSetup({ failTimes: 1 })
    assert.equal(result.status, 0, result.stderr)
    for (const command of commands) {
      assert.equal(result.ran.filter((line) => line === command).length, 2, command)
    }
    assert.equal(result.ran.length, commands.length * 2)
    assert.match(result.stderr, /Attempt 1 of 3 failed with status 1: dotnet restore Axis\.slnx/)
  })

  test('a step that always fails stops setup with its status after three tries', () => {
    const result = runSetup({ failTimes: 99, failStatus: 7 })
    assert.equal(result.status, 7, result.stderr)
    assert.deepEqual(result.ran, [commands[0], commands[0], commands[0]])
    assert.match(result.stderr, /Failed after 3 attempts: dotnet restore/)
  })

  test('a step that succeeds the first time runs once and logs no retry', () => {
    const result = runSetup()
    assert.equal(result.status, 0, result.stderr)
    assert.deepEqual(result.ran, commands)
    assert.doesNotMatch(result.stderr, /Attempt/)
  })
})
