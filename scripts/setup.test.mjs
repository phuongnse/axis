import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
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

// Stands in for curl. The first three requests for each URL fail with the 403 seen on CI, and the
// fourth writes a small JSON file to the -o path.
const curlStub = `#!/usr/bin/env bash
url="\${@: -1}"
echo "$url" >> "$STUB_LOG"
counter="$STUB_COUNTS/$(printf '%s' "$url" | tr -c 'A-Za-z0-9' '_')"
count=$(( $(cat "$counter" 2>/dev/null || echo 0) + 1 ))
echo "$count" > "$counter"
if [ "$count" -le 3 ]; then
  echo "curl: (22) The requested URL returned error: 403" >&2
  exit 22
fi
out=""
while [ $# -gt 0 ]; do
  if [ "$1" = "-o" ]; then out="$2"; fi
  shift
done
echo '{}' > "$out"
`

const commands = [
  'dotnet restore Axis.slnx',
  'dotnet tool restore',
  'npm ci --prefix web',
  'npm ci --prefix tests/e2e',
  'npx --prefix tests/e2e playwright install chromium',
]

const ciCommands = [
  ...commands.slice(0, 4),
  'npx --prefix tests/e2e playwright install-deps chromium',
  'npx --prefix tests/e2e playwright install chromium',
]

function cacheTools(home, { schemas = true } = {}) {
  const tools = join(home, '.cache', 'axis-tools')
  for (const file of [
    `helm-${version('HELM_VERSION')}/helm`,
    `kubeconform-${version('KUBECONFORM_VERSION')}/kubeconform`,
  ]) {
    mkdirSync(dirname(join(tools, file)), { recursive: true })
    writeFileSync(join(tools, file), '', { mode: 0o755 })
  }
  if (schemas) {
    mkdirSync(join(tools, `kubernetes-schemas-${version('KUBERNETES_VERSION')}`), { recursive: true })
  }
}

function runSetup({ failTimes = 0, failStatus = 1, ci = false } = {}) {
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

    cacheTools(home)

    const result = spawnSync('bash', ['scripts/setup.sh'], {
      cwd: root,
      encoding: 'utf8',
      env: {
        ...process.env,
        PATH: `${bin}:${process.env.PATH}`,
        HOME: home,
        CI: ci ? 'true' : '',
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
    assert.match(result.stderr, /Attempt 1 of 4 failed with status 1: dotnet restore Axis\.slnx/)
  })

  test('an outage of three failures per step still succeeds on the fourth try', () => {
    const result = runSetup({ failTimes: 3 })
    assert.equal(result.status, 0, result.stderr)
    for (const command of commands) {
      assert.equal(result.ran.filter((line) => line === command).length, 4, command)
    }
    assert.equal(result.ran.length, commands.length * 4)
  })

  test('a step that always fails stops setup with its status after four tries', () => {
    const result = runSetup({ failTimes: 99, failStatus: 7 })
    assert.equal(result.status, 7, result.stderr)
    assert.deepEqual(result.ran, [commands[0], commands[0], commands[0], commands[0]])
    assert.match(result.stderr, /Failed after 4 attempts: dotnet restore/)
  })

  test('a step that succeeds the first time runs once and logs no retry', () => {
    const result = runSetup()
    assert.equal(result.status, 0, result.stderr)
    assert.deepEqual(result.ran, commands)
    assert.doesNotMatch(result.stderr, /Attempt/)
  })

  test('on CI the Playwright system packages install as a step of their own', () => {
    const result = runSetup({ ci: true })
    assert.equal(result.status, 0, result.stderr)
    assert.deepEqual(result.ran, ciCommands)
  })
})

describe('scripts/lib/tools.sh retries a download that the host refuses once', () => {
  test('a schema download that fails with 403 three times succeeds on its fourth try', () => {
    const dir = mkdtempSync(join(tmpdir(), 'axis-tools-'))
    try {
      const bin = join(dir, 'bin')
      const counts = join(dir, 'counts')
      const log = join(dir, 'log')
      const home = join(dir, 'home')
      mkdirSync(bin)
      mkdirSync(counts)
      writeFileSync(join(bin, 'curl'), curlStub, { mode: 0o755 })
      cacheTools(home, { schemas: false })

      const result = spawnSync('bash', ['-c', 'source scripts/lib/tools.sh; install_tools'], {
        cwd: root,
        encoding: 'utf8',
        env: {
          ...process.env,
          PATH: `${bin}:${process.env.PATH}`,
          HOME: home,
          AXIS_RETRY_DELAY_SECONDS: '0',
          STUB_LOG: log,
          STUB_COUNTS: counts,
        },
      })
      assert.equal(result.status, 0, result.stderr)
      assert.equal(readFileSync(log, 'utf8').trimEnd().split('\n').length, 12)
      assert.match(result.stderr, /Attempt 1 of 4 failed with status 22/)
      const schemas = join(home, '.cache', 'axis-tools', `kubernetes-schemas-${version('KUBERNETES_VERSION')}`)
      assert.deepEqual(readdirSync(schemas).sort(), [
        'deployment-apps-v1.json',
        'job-batch-v1.json',
        'service-v1.json',
      ])
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })
})

describe('scripts/lib/retry.sh waits longer after each failure', () => {
  test('the waits double from 15 seconds before a fourth try', () => {
    const result = spawnSync(
      'bash',
      ['-c', 'source scripts/lib/retry.sh; sleep() { echo "sleep $1" >&2; }; retry 4 false'],
      { cwd: root, encoding: 'utf8', env: { ...process.env, AXIS_RETRY_DELAY_SECONDS: '15' } },
    )
    assert.equal(result.status, 1, result.stderr)
    const waits = result.stderr.match(/^sleep \d+$/gm)
    assert.deepEqual(waits, ['sleep 15', 'sleep 30', 'sleep 60'])
    assert.match(result.stderr, /Failed after 4 attempts: false/)
  })
})
