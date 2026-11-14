import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { listedImages } from './lib/docker-images.mjs'

// Runs scripts/setup.sh with dotnet, npm, npx and python3 replaced by stubs. The stubs fail the first
// STUB_FAIL_TIMES calls of each command line. STUB_HANG names one command line whose first call
// sleeps past the step limit. The home cache is filled in, so no tool downloads.
const root = fileURLToPath(new URL('..', import.meta.url))
const toolsSh = readFileSync(join(root, 'scripts/lib/tools.sh'), 'utf8')
const version = (name) => toolsSh.match(new RegExp(`^${name}=(\\S+)$`, 'm'))[1]
const list = (name) => toolsSh.match(new RegExp(`^${name}=\\(([^)]*)\\)$`, 'm'))[1].split(/\s+/)
const kubernetesSchemaFiles = list('KUBERNETES_SCHEMA_FILES')
const crdSchemaFiles = list('CRD_SCHEMA_FILES')

const stub = `#!/usr/bin/env bash
line="$(basename "$0") $*"
echo "$line" >> "$STUB_LOG"
counter="$STUB_COUNTS/$(printf '%s' "$line" | tr -c 'A-Za-z0-9' '_')"
count=$(( $(cat "$counter" 2>/dev/null || echo 0) + 1 ))
echo "$count" > "$counter"
if [ "$line" = "\${STUB_HANG:-}" ] && [ "$count" -eq 1 ]; then exec sleep 30; fi
if [ "$count" -le "$STUB_FAIL_TIMES" ]; then exit "$STUB_FAIL_STATUS"; fi
exit 0
`

// Stands in for docker and logs to DOCKER_LOG, so STUB_LOG keeps the lines of the tools only. Its images are all
// missing when DOCKER_EMPTY is set. Its runtimes are the contents of DOCKER_RUNTIMES_FILE.
const dockerStub = `#!/usr/bin/env bash
echo "$*" >> "$DOCKER_LOG"
if [ "$1 $2" = "image inspect" ] && [ -n "$DOCKER_EMPTY" ]; then exit 1; fi
if [ "$1" = "info" ]; then cat "$DOCKER_RUNTIMES_FILE"; fi
exit 0
`

// Stands in for id, so setup runs as a user that needs sudo.
const idStub = `#!/usr/bin/env bash
echo 1000
`

// Stands in for sudo. It logs its arguments to SUDO_LOG and runs nothing. Where gVisor registers its
// runtime, it adds runsc to DOCKER_RUNTIMES_FILE.
const sudoStub = `#!/usr/bin/env bash
echo "$*" >> "$SUDO_LOG"
if [ "$*" = "/usr/local/bin/runsc install" ]; then echo '{"runc":{},"runsc":{}}' > "$DOCKER_RUNTIMES_FILE"; fi
exit 0
`

// Stands in for curl when setup installs gVisor. It writes the bytes GVISOR_BYTES to the -o path for
// the runsc URL, and prints the SHA-512 sum in GVISOR_SUM for the .sha512 URL.
const gvisorCurlStub = `#!/usr/bin/env bash
url="\${@: -1}"
echo "$url" >> "$STUB_LOG"
out=""
while [ $# -gt 0 ]; do
  if [ "$1" = "-o" ]; then out="$2"; fi
  shift
done
case "$url" in
  *.sha512) echo "$GVISOR_SUM  runsc" ;;
  */runsc) printf '%s' "$GVISOR_BYTES" > "$out" ;;
  *) exit 22 ;;
esac
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

// Fills the home cache. With schemas, every schema file is cached except the ones in missing.
function cacheTools(home, { schemas = true, missing = [] } = {}) {
  const tools = join(home, '.cache', 'axis-tools')
  for (const file of [
    `helm-${version('HELM_VERSION')}/helm`,
    `kubeconform-${version('KUBECONFORM_VERSION')}/kubeconform`,
  ]) {
    mkdirSync(dirname(join(tools, file)), { recursive: true })
    writeFileSync(join(tools, file), '', { mode: 0o755 })
  }
  if (schemas) {
    const kubernetes = join(tools, `kubernetes-schemas-${version('KUBERNETES_VERSION')}`)
    const crd = join(tools, `crd-schemas-${version('CRDS_CATALOG_COMMIT')}`)
    mkdirSync(kubernetes, { recursive: true })
    mkdirSync(crd, { recursive: true })
    for (const [folder, file] of [
      ...kubernetesSchemaFiles.map((file) => [kubernetes, file]),
      ...crdSchemaFiles.map((file) => [crd, file]),
    ]) {
      if (missing.includes(file)) continue
      mkdirSync(dirname(join(folder, `${file}.json`)), { recursive: true })
      writeFileSync(join(folder, `${file}.json`), '{}')
    }
  }
}

const defaultRuntimes = '{"runc":{},"runsc":{}}'

// Stands in for uname -s, so the gVisor step runs as on Linux whatever the machine.
const unameStub = `#!/usr/bin/env bash
if [ "$1" = "-s" ]; then echo Linux; else exec /usr/bin/uname "$@"; fi
`

// A folder that holds the stubs, the home cache and Docker's runtimes, so two runs share them. The runtimes
// default to a Docker that already lists runsc, so setup installs no gVisor.
function newSession({ savedImages = false, runtimes = defaultRuntimes } = {}) {
  const dir = mkdtempSync(join(tmpdir(), 'axis-setup-'))
  const bin = join(dir, 'bin')
  const home = join(dir, 'home')
  // The folder that holds the images setup saves: complete when savedImages is set, empty otherwise.
  const images = join(dir, 'images')
  mkdirSync(bin)
  mkdirSync(images)
  for (const name of ['dotnet', 'npm', 'npx', 'python3']) {
    writeFileSync(join(bin, name), stub, { mode: 0o755 })
  }
  writeFileSync(join(bin, 'docker'), dockerStub, { mode: 0o755 })
  writeFileSync(join(bin, 'id'), idStub, { mode: 0o755 })
  writeFileSync(join(bin, 'sudo'), sudoStub, { mode: 0o755 })
  writeFileSync(join(bin, 'uname'), unameStub, { mode: 0o755 })
  if (savedImages) {
    writeFileSync(join(images, 'images.tar'), 'archive')
    writeFileSync(join(images, 'images.json'), JSON.stringify(listedImages(root)))
  }
  cacheTools(home)
  const runtimesFile = join(dir, 'runtimes.json')
  writeFileSync(runtimesFile, `${runtimes}\n`)
  return { dir, bin, home, images, runtimesFile, savedImages, tools: join(home, '.cache', 'axis-tools') }
}

function runSetup(
  { failTimes = 0, failStatus = 1, ci = false, savedImages = false, hang = '', gvisor = null, session = null } = {},
) {
  const own = session === null
  const { dir, bin, home, images, runtimesFile } = own ? newSession({ savedImages }) : session
  try {
    // Each run starts with empty logs and counters, as a new run of the script does.
    const counts = join(dir, 'counts')
    const log = join(dir, 'log')
    const dockerLog = join(dir, 'docker-log')
    const sudoLog = join(dir, 'sudo-log')
    for (const path of [log, dockerLog, sudoLog]) rmSync(path, { force: true })
    rmSync(counts, { recursive: true, force: true })
    mkdirSync(counts)
    if (gvisor !== null) writeFileSync(join(bin, 'curl'), gvisorCurlStub, { mode: 0o755 })

    const result = spawnSync('bash', ['scripts/setup.sh'], {
      cwd: root,
      encoding: 'utf8',
      env: {
        ...process.env,
        PATH: `${bin}:${process.env.PATH}`,
        HOME: home,
        CI: ci ? 'true' : '',
        AXIS_RETRY_DELAY_SECONDS: '0',
        AXIS_DOCKER_CACHE_DIR: images,
        DOCKER_LOG: dockerLog,
        DOCKER_EMPTY: (session?.savedImages ?? savedImages) ? '' : '1',
        DOCKER_RUNTIMES_FILE: runtimesFile,
        SUDO_LOG: sudoLog,
        GVISOR_BYTES: gvisor?.bytes ?? '',
        GVISOR_SUM: gvisor?.sum ?? '',
        AXIS_STEP_TIME_LIMIT_SECONDS: hang ? '1' : '',
        STUB_LOG: log,
        STUB_COUNTS: counts,
        STUB_FAIL_TIMES: String(failTimes),
        STUB_FAIL_STATUS: String(failStatus),
        STUB_HANG: hang,
      },
    })
    const lines = (path) => (existsSync(path) ? readFileSync(path, 'utf8').trimEnd().split('\n') : [])
    const all = lines(log)
    // The wheel downloads have tests of their own, so ran keeps the other tools' lines. The gVisor
    // downloads are the curl lines.
    const ran = all.filter((line) => !line.startsWith('python3 ') && !line.startsWith('https://'))
    const wheels = all.filter((line) => line.startsWith('python3 '))
    const downloads = all.filter((line) => line.startsWith('https://'))
    return { ...result, ran, wheels, downloads, docker: lines(dockerLog), sudo: lines(sudoLog) }
  } finally {
    if (own) rmSync(dir, { recursive: true, force: true })
  }
}

// One requirements file for each Python sample package, whose wheel setup fetches.
const requirementsFiles = readdirSync(join(root, 'samples/packages')).filter((name) =>
  existsSync(join(root, 'samples/packages', name, 'requirements.txt')),
)

describe('scripts/setup.sh retries its network steps', () => {
  test('each step that fails once succeeds on its second try', () => {
    const result = runSetup({ failTimes: 1 })
    assert.equal(result.status, 0, result.stderr)
    for (const command of commands) {
      assert.equal(result.ran.filter((line) => line === command).length, 2, command)
    }
    assert.equal(result.ran.length, commands.length * 2)
    assert.match(result.stderr, /Attempt 1 of 4 failed with status 1: limit dotnet restore Axis\.slnx/)
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
    assert.match(result.stderr, /Failed after 4 attempts: limit dotnet restore/)
  })

  test('a step that succeeds the first time runs once and logs no retry', () => {
    const result = runSetup()
    assert.equal(result.status, 0, result.stderr)
    assert.deepEqual(result.ran, commands)
    assert.doesNotMatch(result.stderr, /Attempt/)
  })

  test('setup fetches the wheel of each Python sample package once, offline, when the cache holds it', () => {
    const result = runSetup()
    assert.equal(result.status, 0, result.stderr)
    assert.ok(requirementsFiles.length > 0)
    assert.equal(result.wheels.length, requirementsFiles.length)
    for (const [index, name] of requirementsFiles.entries()) {
      assert.match(result.wheels[index], /^python3 -m pip download --require-hashes --no-deps --only-binary=:all: /)
      assert.match(result.wheels[index], / --no-index --find-links \S+\/\.cache\/axis-tools\/wheels /)
      assert.match(result.wheels[index], new RegExp(` -r samples/packages/${name}/requirements\\.txt$`))
    }
  })

  test('setup downloads the wheel with retries when the cache lacks it', () => {
    const result = runSetup({ failTimes: 1 })
    assert.equal(result.status, 0, result.stderr)
    const offline = result.wheels.filter((line) => line.includes(' --no-index '))
    const online = result.wheels.filter((line) => !line.includes(' --no-index '))
    assert.equal(offline.length, requirementsFiles.length)
    assert.equal(online.length, requirementsFiles.length * 2)
    assert.match(online[0], / --timeout 30 /)
    assert.match(result.stderr, /Attempt 1 of 4 failed with status 1: python3 -m pip download /)
  })

  test('on CI setup runs the same steps as elsewhere', () => {
    const result = runSetup({ ci: true })
    assert.equal(result.status, 0, result.stderr)
    assert.deepEqual(result.ran, commands)
  })

  test('a step that hangs fails its try at the limit, and the next try succeeds', () => {
    const result = runSetup({ hang: commands[0] })
    assert.equal(result.status, 0, result.stderr)
    assert.equal(result.ran.filter((line) => line === commands[0]).length, 2)
    assert.equal(result.ran.length, commands.length + 1)
    assert.match(result.stderr, /Attempt 1 of 4 failed with status 124: limit dotnet restore Axis\.slnx/)
  })
})

describe('scripts/lib/tools.sh limits every download', () => {
  test('each curl has a connect and a total time limit, and no retry of its own', () => {
    // Reads the two scripts that hold the downloads. Joins the lines that continue a command, so a curl
    // that spans lines is read whole.
    const downloads = ['scripts/lib/tools.sh', 'scripts/lib/trivy.sh'].map((file) => readFileSync(join(root, file), 'utf8'))
    const lines = downloads.join('\n').replace(/\\\n\s*/g, ' ').split('\n')
    const curls = lines.filter((line) => /(^|\$\()retry 4 curl /.test(line.trim()))
    assert.equal(curls.length, 9)
    for (const line of curls) {
      assert.match(line, /--connect-timeout \d+/, line)
      assert.match(line, /--max-time \d+/, line)
      assert.doesNotMatch(line, /--retry /, line)
    }
  })
})

// The one Docker call that setup makes when it has no image to save: the runtimes the gVisor step reads.
const runtimesQuery = 'info --format {{json .Runtimes}}'

describe('scripts/setup.sh keeps the container images of the checks', () => {
  test('on CI with every listed image saved it only asks Docker for its runtimes', () => {
    const result = runSetup({ ci: true, savedImages: true })
    assert.equal(result.status, 0, result.stderr)
    assert.deepEqual(result.docker, [runtimesQuery])
  })

  test('on CI it pulls the listed images that neither Docker nor the saved archive holds', () => {
    const result = runSetup({ ci: true })
    assert.equal(result.status, 0, result.stderr)
    assert.deepEqual(
      result.docker.filter((line) => line.startsWith('pull ')),
      listedImages(root).map((image) => `pull ${image}`),
    )
  })

  test('off CI it only asks Docker for its runtimes', () => {
    const result = runSetup({ savedImages: false })
    assert.equal(result.status, 0, result.stderr)
    assert.deepEqual(result.docker, [runtimesQuery])
  })
})

describe('scripts/setup.sh installs gVisor once', () => {
  const bytes = 'runsc binary of the test'
  const sum = createHash('sha512').update(bytes).digest('hex')
  const baseUrl = `https://storage.googleapis.com/gvisor/releases/release/${version('GVISOR_VERSION')}`
  const sudoLines = (result) => result.sudo.map((line) => line.replace(/ \S+\/\.cache\/axis-tools\/\S+/, ' <cache>'))

  test('the first run downloads, checks and registers runsc, and the second run does nothing', () => {
    const session = newSession({ runtimes: '{"runc":{}}' })
    try {
      const first = runSetup({ gvisor: { bytes, sum }, session })
      assert.equal(first.status, 0, first.stderr)
      assert.match(first.downloads[0], /\/(x86_64|aarch64)\/runsc$/)
      assert.deepEqual(
        first.downloads.map((url) => url.replace(/\/(x86_64|aarch64)\//, '/<arch>/')),
        [`${baseUrl}/<arch>/runsc`, `${baseUrl}/<arch>/runsc.sha512`],
      )
      assert.equal(readFileSync(join(session.tools, `gvisor-${version('GVISOR_VERSION')}`, 'runsc'), 'utf8'), bytes)
      assert.deepEqual(sudoLines(first), [
        'install -m 0755 <cache> /usr/local/bin/runsc',
        '/usr/local/bin/runsc install',
        'systemctl reload docker',
      ])
      const runtimes = readFileSync(session.runtimesFile, 'utf8')
      assert.match(runtimes, /"runsc"/)

      const second = runSetup({ gvisor: { bytes, sum }, session })
      assert.equal(second.status, 0, second.stderr)
      assert.deepEqual(second.downloads, [])
      assert.deepEqual(second.sudo, [])
      assert.equal(readFileSync(session.runtimesFile, 'utf8'), runtimes)
    } finally {
      rmSync(session.dir, { recursive: true, force: true })
    }
  })

  test('a download that does not match its published SHA-512 stops setup and installs nothing', () => {
    const session = newSession({ runtimes: '{"runc":{}}' })
    try {
      const result = runSetup({ gvisor: { bytes, sum: createHash('sha512').update('other bytes').digest('hex') }, session })
      assert.notEqual(result.status, 0)
      assert.match(result.stderr, /Checksum mismatch/)
      assert.deepEqual(result.sudo, [])
      assert.deepEqual(
        readdirSync(session.tools).filter((name) => name.startsWith('gvisor-') || name.startsWith('.install.')),
        [],
      )
      assert.equal(readFileSync(session.runtimesFile, 'utf8').includes('runsc'), false)
    } finally {
      rmSync(session.dir, { recursive: true, force: true })
    }
  })

  test('a Docker that does not reload leaves setup with a warning that runsc is missing', () => {
    const session = newSession({ runtimes: '{"runc":{}}' })
    try {
      writeFileSync(
        join(session.bin, 'sudo'),
        '#!/usr/bin/env bash\necho "$*" >> "$SUDO_LOG"\nif [ "$1 $2" = "systemctl reload" ]; then exit 5; fi\nexit 0\n',
        { mode: 0o755 },
      )
      const result = runSetup({ gvisor: { bytes, sum }, session })
      assert.equal(result.status, 0, result.stderr)
      assert.deepEqual(sudoLines(result), [
        'install -m 0755 <cache> /usr/local/bin/runsc',
        '/usr/local/bin/runsc install',
        'systemctl reload docker',
      ])
      assert.match(result.stderr, /Docker did not reload/)
      assert.match(result.stderr, /Docker does not list the runsc runtime/)
    } finally {
      rmSync(session.dir, { recursive: true, force: true })
    }
  })

  test('a Docker that is not reachable skips gVisor with a note', () => {
    const session = newSession()
    try {
      writeFileSync(join(session.bin, 'docker'), '#!/usr/bin/env bash\nexit 1\n', { mode: 0o755 })
      const result = runSetup({ gvisor: { bytes, sum }, session })
      assert.equal(result.status, 0, result.stderr)
      assert.match(result.stderr, /Skipping gVisor: Docker is not reachable/)
      assert.deepEqual(result.downloads, [])
      assert.deepEqual(result.sudo, [])
    } finally {
      rmSync(session.dir, { recursive: true, force: true })
    }
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
      assert.equal(readFileSync(log, 'utf8').trimEnd().split('\n').length, 44)
      assert.match(result.stderr, /Attempt 1 of 4 failed with status 22/)
      const schemas = join(home, '.cache', 'axis-tools', `kubernetes-schemas-${version('KUBERNETES_VERSION')}`)
      assert.deepEqual(readdirSync(schemas).sort(), [
        'configmap-v1.json',
        'cronjob-batch-v1.json',
        'deployment-apps-v1.json',
        'job-batch-v1.json',
        'networkpolicy-networking-v1.json',
        'role-rbac-v1.json',
        'rolebinding-rbac-v1.json',
        'service-v1.json',
        'serviceaccount-v1.json',
      ])
      const crd = join(home, '.cache', 'axis-tools', `crd-schemas-${version('CRDS_CATALOG_COMMIT')}`)
      assert.deepEqual(readdirSync(join(crd, 'cert-manager.io')).sort(), ['certificate_v1.json', 'issuer_v1.json'])
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })
})

describe('scripts/lib/tools.sh fetches the schema files that a cached folder lacks', () => {
  function installTools({ missing }) {
    const dir = mkdtempSync(join(tmpdir(), 'axis-tools-'))
    try {
      const bin = join(dir, 'bin')
      const counts = join(dir, 'counts')
      const log = join(dir, 'log')
      const home = join(dir, 'home')
      mkdirSync(bin)
      mkdirSync(counts)
      writeFileSync(join(bin, 'curl'), curlStub, { mode: 0o755 })
      cacheTools(home, { missing })

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
      const tools = join(home, '.cache', 'axis-tools')
      return {
        ...result,
        requested: [...new Set(existsSync(log) ? readFileSync(log, 'utf8').trimEnd().split('\n') : [])],
        schemas: readdirSync(join(tools, `kubernetes-schemas-${version('KUBERNETES_VERSION')}`)).sort(),
        leftovers: readdirSync(tools).filter((name) => name.startsWith('.install.')),
      }
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  }

  test('a cached folder that lacks one file gets only that file downloaded', () => {
    const result = installTools({ missing: ['configmap-v1'] })
    assert.equal(result.status, 0, result.stderr)
    assert.equal(result.requested.length, 1)
    assert.match(result.requested[0], /\/v1\.33\.0-standalone-strict\/configmap-v1\.json$/)
    assert.deepEqual(result.schemas, [
      'configmap-v1.json',
      'cronjob-batch-v1.json',
      'deployment-apps-v1.json',
      'job-batch-v1.json',
      'networkpolicy-networking-v1.json',
      'role-rbac-v1.json',
      'rolebinding-rbac-v1.json',
      'service-v1.json',
      'serviceaccount-v1.json',
    ])
    assert.deepEqual(result.leftovers, [])
  })

  test('a complete cache downloads nothing', () => {
    const result = installTools({ missing: [] })
    assert.equal(result.status, 0, result.stderr)
    assert.deepEqual(result.requested, [])
  })

  test('a fresh cache gets configmap-v1.json', () => {
    const result = installTools({ missing: [...kubernetesSchemaFiles, ...crdSchemaFiles] })
    assert.equal(result.status, 0, result.stderr)
    assert.ok(result.schemas.includes('configmap-v1.json'))
    assert.equal(result.requested.length, kubernetesSchemaFiles.length + crdSchemaFiles.length)
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
