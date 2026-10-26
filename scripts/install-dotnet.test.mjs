import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// Runs scripts/install-dotnet.sh with curl replaced by a stub. The stub fails the first STUB_FAIL_TIMES
// calls and then writes a fake dotnet-install.sh to the -o path. The fake installer fails its first
// STUB_INSTALLER_FAIL_TIMES runs and then creates the SDK folder. The home, GITHUB_PATH and GITHUB_ENV are temporary.
const root = fileURLToPath(new URL('..', import.meta.url))
const version = JSON.parse(readFileSync(join(root, 'global.json'), 'utf8')).sdk.version

const curlStub = `#!/usr/bin/env bash
echo "$*" >> "$STUB_LOG"
count=$(( $(cat "$STUB_COUNTS/curl" 2>/dev/null || echo 0) + 1 ))
echo "$count" > "$STUB_COUNTS/curl"
if [ "$count" -le "$STUB_FAIL_TIMES" ]; then
  echo "curl: (22) The requested URL returned error: 503" >&2
  exit 22
fi
out=""
while [ $# -gt 0 ]; do
  if [ "$1" = "-o" ]; then out="$2"; fi
  shift
done
cat > "$out" <<'INSTALLER'
#!/usr/bin/env bash
count=$(( $(cat "$STUB_COUNTS/installer" 2>/dev/null || echo 0) + 1 ))
echo "$count" > "$STUB_COUNTS/installer"
if [ "$count" -le "$STUB_INSTALLER_FAIL_TIMES" ]; then exit 1; fi
while [ $# -gt 0 ]; do
  case "$1" in
    --version) sdk="$2" ;;
    --install-dir) dir="$2" ;;
  esac
  shift
done
mkdir -p "$dir/sdk/$sdk"
printf '#!/usr/bin/env bash\\n' > "$dir/dotnet"
chmod +x "$dir/dotnet"
INSTALLER
`

function runInstall({ failTimes = 0, installerFailTimes = 0, installed = false } = {}) {
  const dir = mkdtempSync(join(tmpdir(), 'axis-dotnet-'))
  try {
    const bin = join(dir, 'bin')
    const counts = join(dir, 'counts')
    const home = join(dir, 'home')
    const githubPath = join(dir, 'github-path')
    const githubEnv = join(dir, 'github-env')
    const log = join(dir, 'log')
    mkdirSync(bin)
    mkdirSync(counts)
    writeFileSync(join(bin, 'curl'), curlStub, { mode: 0o755 })
    const cache = join(home, '.cache', 'axis-dotnet')
    const sdkDir = join(cache, version)
    if (installed) mkdirSync(join(sdkDir, 'sdk', version), { recursive: true })

    const result = spawnSync('bash', ['scripts/install-dotnet.sh'], {
      cwd: root,
      encoding: 'utf8',
      env: {
        ...process.env,
        PATH: `${bin}:${process.env.PATH}`,
        HOME: home,
        GITHUB_PATH: githubPath,
        GITHUB_ENV: githubEnv,
        AXIS_RETRY_DELAY_SECONDS: '0',
        STUB_LOG: log,
        STUB_COUNTS: counts,
        STUB_FAIL_TIMES: String(failTimes),
        STUB_INSTALLER_FAIL_TIMES: String(installerFailTimes),
      },
    })
    const read = (file) => (existsSync(file) ? readFileSync(file, 'utf8') : '')
    return {
      ...result,
      sdkDir,
      curlCalls: read(log).trimEnd().split('\n').filter(Boolean),
      installerRuns: Number(read(join(counts, 'installer')) || 0),
      githubPath: read(githubPath),
      githubEnv: read(githubEnv),
      sdkInstalled: existsSync(join(sdkDir, 'sdk', version)),
      cacheEntries: existsSync(cache) ? readdirSync(cache) : [],
    }
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
}

describe('scripts/install-dotnet.sh installs the SDK that global.json names', () => {
  test('a download that fails three times succeeds on its fourth try', () => {
    const result = runInstall({ failTimes: 3 })
    assert.equal(result.status, 0, result.stderr)
    assert.equal(result.curlCalls.length, 4)
    assert.match(result.curlCalls[0], /https:\/\/dot\.net\/v1\/dotnet-install\.sh$/)
    assert.match(result.stderr, /Attempt 1 of 4 failed with status 22/)
    assert.equal(result.sdkInstalled, true)
    assert.deepEqual(result.cacheEntries, [version])
    assert.equal(result.githubPath, `${result.sdkDir}\n`)
    assert.equal(result.githubEnv, `DOTNET_ROOT=${result.sdkDir}\n`)
  })

  test('a download that always fails stops after four tries and leaves nothing behind', () => {
    const result = runInstall({ failTimes: 99 })
    assert.notEqual(result.status, 0)
    assert.equal(result.curlCalls.length, 4)
    assert.match(result.stderr, /Failed after 4 attempts: curl/)
    assert.deepEqual(result.cacheEntries, [])
    assert.equal(result.githubPath, '')
    assert.equal(result.githubEnv, '')
  })

  test('an installer that fails once succeeds on its second run without downloading again', () => {
    const result = runInstall({ installerFailTimes: 1 })
    assert.equal(result.status, 0, result.stderr)
    assert.equal(result.curlCalls.length, 1)
    assert.equal(result.installerRuns, 2)
    assert.match(result.stderr, /Attempt 1 of 4 failed with status 1: bash/)
    assert.equal(result.sdkInstalled, true)
    assert.deepEqual(result.cacheEntries, [version])
  })

  test('an installer that always fails stops after four runs and leaves nothing behind', () => {
    const result = runInstall({ installerFailTimes: 99 })
    assert.notEqual(result.status, 0)
    assert.equal(result.installerRuns, 4)
    assert.deepEqual(result.cacheEntries, [])
  })

  test('an SDK that is already installed makes no download and still sets the path', () => {
    const result = runInstall({ installed: true })
    assert.equal(result.status, 0, result.stderr)
    assert.deepEqual(result.curlCalls, [])
    assert.equal(result.githubPath, `${result.sdkDir}\n`)
    assert.equal(result.githubEnv, `DOTNET_ROOT=${result.sdkDir}\n`)
  })
})
