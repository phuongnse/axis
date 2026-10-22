import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// Runs install_browser_ci from scripts/lib/browser.sh against stand-in npx, sudo, dpkg-query and
// apt-get commands placed first on PATH, so no network or root is needed. Each stand-in appends
// its arguments to a log file.
const root = fileURLToPath(new URL('..', import.meta.url))
const dryRun =
  'sudo -- sh -c "apt-get update && apt-get install -y --no-install-recommends libnss3 libasound2t64 fonts-liberation"'

const standIns = {
  npx: `#!/usr/bin/env bash
echo "npx $*" >>"$FAKE_LOG"
if [[ "$*" == *"install-deps --dry-run"* ]]; then
  echo 'Playwright would run:'
  echo '${dryRun}'
fi
`,
  sudo: `#!/usr/bin/env bash
if [[ "$1" == "--" ]]; then shift; fi
exec "$@"
`,
  'dpkg-query': `#!/usr/bin/env bash
if [[ "\${FAKE_INSTALLED:-}" == "1" ]]; then printf installed; else exit 1; fi
`,
  'apt-get': `#!/usr/bin/env bash
echo "apt-get $*" >>"$FAKE_LOG"
parts=""
for arg in "$@"; do
  if [[ "$arg" == Dir::Etc::sourceparts=* ]]; then parts="\${arg#Dir::Etc::sourceparts=}"; fi
done
for arg in "$@"; do
  case "$arg" in
    update)
      if [[ -z "$parts" ]] || grep -rq packages.microsoft.com "$parts"; then
        echo "E: The repository 'https://packages.microsoft.com/ubuntu/24.04/prod noble InRelease' is no longer signed." >&2
        exit 100
      fi
      echo "sources: $(ls "$parts" | tr '\\n' ' ')" >>"$FAKE_LOG"
      exit 0
      ;;
    install)
      if [[ "\${FAKE_APT_INSTALL_FAIL:-}" == "1" ]]; then
        echo "E: Unable to locate package libnss3" >&2
        exit 100
      fi
      exit 0
      ;;
  esac
done
`,
}

const ubuntuSource = 'Types: deb\nURIs: http://azure.archive.ubuntu.com/ubuntu/\nSuites: noble\nComponents: main\n'
const microsoftSource = 'deb https://packages.microsoft.com/ubuntu/24.04/prod noble main\n'

const folders = []

afterEach(() => {
  for (const folder of folders.splice(0)) rmSync(folder, { recursive: true, force: true })
})

function setup({ ubuntu = true } = {}) {
  const folder = mkdtempSync(join(tmpdir(), 'axis-setup-'))
  folders.push(folder)
  const bin = join(folder, 'bin')
  const sources = join(folder, 'apt', 'sources.list.d')
  mkdirSync(bin)
  mkdirSync(sources, { recursive: true })
  for (const [name, content] of Object.entries(standIns)) {
    writeFileSync(join(bin, name), content)
    chmodSync(join(bin, name), 0o755)
  }
  if (ubuntu) writeFileSync(join(sources, 'ubuntu.sources'), ubuntuSource)
  writeFileSync(join(sources, 'microsoft-prod.list'), microsoftSource)
  return { bin, apt: join(folder, 'apt'), log: join(folder, 'log') }
}

function run(env = {}, options) {
  const { bin, apt, log } = setup(options)
  writeFileSync(log, '')
  const result = spawnSync('bash', ['-c', 'source scripts/lib/browser.sh && install_browser_ci'], {
    cwd: root,
    encoding: 'utf8',
    env: { ...process.env, PATH: `${bin}:${process.env.PATH}`, AXIS_APT_ETC: apt, FAKE_LOG: log, ...env },
  })
  return { ...result, log: readFileSync(log, 'utf8') }
}

describe('install_browser_ci', () => {
  test('a broken Microsoft source does not stop it: only the Ubuntu source is updated', () => {
    const result = run()
    assert.equal(result.status, 0, result.stderr)
    assert.match(result.log, /npx .*playwright install chromium/)
    assert.match(result.log, /apt-get .* update/)
    assert.match(result.log, /apt-get .* install -y --no-install-recommends libnss3 libasound2t64 fonts-liberation/)
    assert.match(result.log, /^sources: ubuntu\.sources $/m)
  })

  test('it does not call apt when every package is already installed', () => {
    const result = run({ FAKE_INSTALLED: '1' })
    assert.equal(result.status, 0, result.stderr)
    assert.doesNotMatch(result.log, /apt-get/)
    assert.match(result.stdout, /already installed/)
  })

  test('it fails and names the packages when they cannot be installed', () => {
    const result = run({ FAKE_APT_INSTALL_FAIL: '1' })
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /Could not install Chromium's system dependencies/)
    assert.match(result.stderr, /libnss3 libasound2t64 fonts-liberation/)
  })

  test('it fails when no Ubuntu source exists', () => {
    const result = run({}, { ubuntu: false })
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /Found no Ubuntu APT source/)
    assert.doesNotMatch(result.log, /apt-get/)
  })
})
