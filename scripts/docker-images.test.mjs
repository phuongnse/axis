import assert from 'node:assert/strict'
import { execFileSync, spawnSync } from 'node:child_process'
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { afterEach, beforeEach, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { listedImage, listedImages, pullTimeoutSeconds } from './lib/docker-images.mjs'
import { findImageReferences } from './lib/images.mjs'

// Runs scripts/lib/docker-images.mjs with docker replaced by a stub that keeps the images Docker has in a file:
// `pull X` adds X, `save -o F A B` writes the names to F, `load -i F` adds the names in F, and `image inspect`
// succeeds only for an image that Docker has. DOCKER_FAIL_PULL names an image whose pull fails, DOCKER_FAIL_LOAD makes
// every load fail and DOCKER_DOWN makes `docker info` fail. DOCKER_HANG_PULL names an image whose pull never ends.
// Every call is logged.
const root = fileURLToPath(new URL('..', import.meta.url))
const listed = listedImages(root)

const dockerStub = `#!/usr/bin/env bash
echo "$*" >> "$DOCKER_LOG"
case "$1" in
  info) [ -z "$DOCKER_DOWN" ] ;;
  image) grep -qxF "$3" "$DOCKER_STATE" ;;
  pull) if [ "$2" = "$DOCKER_HANG_PULL" ]; then exec sleep 60; fi; if [ "$2" = "$DOCKER_FAIL_PULL" ]; then exit 1; fi; echo "$2" >> "$DOCKER_STATE" ;;
  save) file="$3"; shift 3; printf '%s\\n' "$@" > "$file" ;;
  load) if [ -n "$DOCKER_FAIL_LOAD" ]; then exit 1; fi; cat "$3" >> "$DOCKER_STATE" ;;
esac
`

describe('scripts/lib/docker-images.mjs', () => {
  let temp
  let cache
  let state
  let log

  beforeEach(() => {
    temp = mkdtempSync(path.join(tmpdir(), 'docker-images-'))
    cache = path.join(temp, 'cache')
    state = path.join(temp, 'state')
    log = path.join(temp, 'log')
    mkdirSync(cache)
    mkdirSync(path.join(temp, 'bin'))
    writeFileSync(path.join(temp, 'bin/docker'), dockerStub, { mode: 0o755 })
    writeFileSync(state, '')
    writeFileSync(log, '')
  })

  afterEach(() => rmSync(temp, { recursive: true, force: true }))

  // Puts an archive and its index in the folder. The archive holds the names, as the stub writes it.
  const archive = (images) => {
    writeFileSync(path.join(cache, 'images.tar'), images.map((image) => `${image}\n`).join(''))
    writeFileSync(path.join(cache, 'images.json'), JSON.stringify(images))
  }
  const indexed = () => JSON.parse(readFileSync(path.join(cache, 'images.json'), 'utf8'))
  const archived = () => readFileSync(path.join(cache, 'images.tar'), 'utf8').split('\n').filter(Boolean)
  const holds = () => readFileSync(state, 'utf8').split('\n').filter(Boolean)

  function run(command, env = {}) {
    const result = spawnSync('node', [path.join(root, 'scripts/lib/docker-images.mjs'), command], {
      encoding: 'utf8',
      env: {
        ...process.env,
        PATH: `${path.join(temp, 'bin')}:${process.env.PATH}`,
        AXIS_DOCKER_CACHE_DIR: cache,
        AXIS_RETRY_DELAY_SECONDS: '0',
        DOCKER_LOG: log,
        DOCKER_STATE: state,
        ...env,
      },
    })
    const docker = readFileSync(log, 'utf8').split('\n').filter(Boolean)
    return { ...result, docker, pulls: docker.filter((line) => line.startsWith('pull ')) }
  }

  test('save makes no Docker call when the archive holds every listed image', () => {
    archive(listed)
    const result = run('save')

    assert.equal(result.status, 0, result.stderr)
    assert.deepEqual(result.docker, [])
  })

  test('load runs docker load once and pulls nothing when Docker lacks the saved images', () => {
    archive(listed)
    const result = run('load')

    assert.equal(result.status, 0, result.stderr)
    assert.deepEqual(result.docker.filter((line) => !line.startsWith('image inspect ')), [`load -i ${path.join(cache, 'images.tar')}`])
    assert.deepEqual(result.pulls, [])
    assert.deepEqual(holds(), listed)
  })

  test('load makes no load when Docker holds every saved image', () => {
    archive(listed)
    writeFileSync(state, listed.map((image) => `${image}\n`).join(''))
    const result = run('load')

    assert.equal(result.status, 0, result.stderr)
    assert.ok(!result.docker.some((line) => line.startsWith('load ')), result.docker.join('\n'))
    assert.deepEqual(result.pulls, [])
  })

  test('load of a folder with no archive says so, pulls nothing and succeeds', () => {
    const result = run('load')

    assert.equal(result.status, 0, result.stderr)
    assert.match(result.stdout, /holds no saved container images/)
    assert.deepEqual(result.docker, [])
  })

  test('load deletes an archive that does not load, and succeeds', () => {
    archive(listed)
    const result = run('load', { DOCKER_FAIL_LOAD: '1' })

    assert.equal(result.status, 0, result.stderr)
    assert.match(result.stderr, /are deleted/)
    assert.equal(existsSync(path.join(cache, 'images.tar')), false)
    assert.equal(existsSync(path.join(cache, 'images.json')), false)
    assert.deepEqual(result.pulls, [])
  })

  test('save pulls only the image that the index lacks, then saves every listed image', () => {
    const lacking = listed[3]
    archive(listed.filter((image) => image !== lacking))
    const result = run('save')

    assert.equal(result.status, 0, result.stderr)
    assert.deepEqual(result.pulls, [`pull ${lacking}`])
    assert.equal(result.docker.filter((line) => line.startsWith('load ')).length, 1)
    assert.equal(result.docker.filter((line) => line.startsWith('save ')).length, 1)
    assert.deepEqual(indexed(), listed)
    assert.deepEqual(archived(), listed)
  })

  test('save pulls every listed image into an empty folder and saves them in one archive', () => {
    const result = run('save')

    assert.equal(result.status, 0, result.stderr)
    assert.deepEqual(result.pulls, listed.map((image) => `pull ${image}`))
    assert.equal(result.docker.filter((line) => line.startsWith('save ')).length, 1)
    assert.ok(!result.docker.some((line) => line.startsWith('load ')), 'there is no archive to load')
    assert.deepEqual(indexed(), listed)
    assert.deepEqual(archived(), listed)
    assert.equal(existsSync(path.join(cache, 'images.tar.tmp')), false)
  })

  test('save pulls nothing that Docker already holds', () => {
    writeFileSync(state, listed.map((image) => `${image}\n`).join(''))
    const result = run('save')

    assert.equal(result.status, 0, result.stderr)
    assert.deepEqual(result.pulls, [])
    assert.deepEqual(indexed(), listed)
  })

  test('save warns, leaves out an image that never pulls, saves the rest and succeeds', () => {
    const failing = listed[5]
    const result = run('save', { DOCKER_FAIL_PULL: failing })

    assert.equal(result.status, 0, result.stderr)
    assert.match(result.stderr, new RegExp(`Warning: .*pull ${failing.replace(/[.]/g, '\\.')} failed after 3 attempts`))
    assert.equal(result.pulls.filter((line) => line === `pull ${failing}`).length, 3)
    const rest = listed.filter((image) => image !== failing)
    assert.deepEqual(indexed(), rest)
    assert.deepEqual(archived(), rest)
  })

  test('save stops a pull that does not end at the limit, tries it again and leaves the image out', () => {
    const hanging = listed[5]
    const result = run('save', { DOCKER_HANG_PULL: hanging, AXIS_STEP_TIME_LIMIT_SECONDS: '1' })

    assert.equal(result.status, 0, result.stderr)
    assert.equal(result.pulls.filter((line) => line === `pull ${hanging}`).length, 3)
    assert.match(result.stderr, /Attempt 1 of 3 failed with no end in 1 seconds: docker pull /)
    assert.match(result.stderr, /failed after 3 attempts with no end in 1 seconds, so .* is not saved/)
    assert.deepEqual(indexed(), listed.filter((image) => image !== hanging))
  })

  test('the pull limit is 600 seconds when AXIS_STEP_TIME_LIMIT_SECONDS is unset or empty, as in scripts/setup.sh', () => {
    const saved = process.env.AXIS_STEP_TIME_LIMIT_SECONDS
    try {
      delete process.env.AXIS_STEP_TIME_LIMIT_SECONDS
      assert.equal(pullTimeoutSeconds(), 600)
      process.env.AXIS_STEP_TIME_LIMIT_SECONDS = ''
      assert.equal(pullTimeoutSeconds(), 600)
      process.env.AXIS_STEP_TIME_LIMIT_SECONDS = '30'
      assert.equal(pullTimeoutSeconds(), 30)
    } finally {
      if (saved === undefined) delete process.env.AXIS_STEP_TIME_LIMIT_SECONDS
      else process.env.AXIS_STEP_TIME_LIMIT_SECONDS = saved
    }
  })

  test('save pulls every image when an incomplete archive does not load, and writes a new archive', () => {
    archive(listed.slice(1))
    const result = run('save', { DOCKER_FAIL_LOAD: '1' })

    assert.equal(result.status, 0, result.stderr)
    assert.equal(result.docker.filter((line) => line.startsWith('load ')).length, 1)
    assert.deepEqual(result.pulls, listed.map((image) => `pull ${image}`))
    assert.deepEqual(indexed(), listed)
  })

  test('save says so and writes nothing when Docker is not running', () => {
    const result = run('save', { DOCKER_DOWN: '1' })

    assert.equal(result.status, 0, result.stderr)
    assert.match(result.stdout, /Docker is not running/)
    assert.deepEqual(result.docker, ['info'])
    assert.equal(existsSync(path.join(cache, 'images.tar')), false)
  })

  test('a command other than save or load exits with code 2', () => {
    const result = run('pull')

    assert.equal(result.status, 2)
    assert.match(result.stderr, /usage: node scripts\/lib\/docker-images\.mjs save\|load/)
  })

  test('listedImage returns the listed image of a repository and fails for another', () => {
    assert.ok(listed.includes(listedImage(root, 'postgres')))
    assert.match(listedImage(root, 'postgres'), /^postgres:/)
    assert.throws(() => listedImage(root, 'unlisted/image'), /lists no unlisted\/image image/)
  })
})

describe('the repository', () => {
  const files = execFileSync('git', ['ls-files'], { cwd: root, encoding: 'utf8' }).split('\n').filter(Boolean)
  const references = findImageReferences(root, files)

  test('lists every container image that a Dockerfile stage, a Compose file, a fixture or a test script names', () => {
    for (const { file, line, image } of references) {
      assert.ok(listed.includes(image), `${file}:${line} names ${image}, which scripts/docker-images.json lacks`)
    }
  })

  test('finds the references in the files that name an image', () => {
    const found = new Set(references.map((reference) => reference.file))
    for (const file of [
      'Dockerfile',
      'compose.yaml',
      'deploy/compose/compose.yaml',
      'tests/Axis.Integration.Tests/PostgreSqlServer.cs',
      'controlplane/tests/Axis.ControlPlane.Integration.Tests/BindFixture.cs',
      'controlplane/tests/Axis.ControlPlane.Integration.Tests/SignUpTests.cs',
      'scripts/e2e.sh',
      'scripts/integration.sh',
    ]) {
      assert.ok(found.has(file), `${file} has no image reference found`)
    }
  })

  test('names every listed image somewhere', () => {
    const named = new Set(references.map((reference) => reference.image))
    for (const image of listed) assert.ok(named.has(image), `${image} is listed but no file names it`)
  })

  test("keys NexKit's docker cache entry on the image list and the Dockerfile, which pin what it holds", () => {
    const config = JSON.parse(readFileSync(path.join(root, '.nexkit/config.json'), 'utf8'))
    const docker = config.cache.find((entry) => entry.name === 'docker')
    assert.deepEqual([...docker.key_files].sort(), ['Dockerfile', 'scripts/docker-images.json'])
  })

  test('is sorted and free of duplicates', () => {
    const text = JSON.parse(readFileSync(path.join(root, 'scripts/docker-images.json'), 'utf8'))
    assert.deepEqual(text, [...new Set(text)].sort())
  })
})

describe('findImageReferences', () => {
  let temp

  beforeEach(() => {
    temp = mkdtempSync(path.join(tmpdir(), 'image-references-'))
    mkdirSync(path.join(temp, 'scripts'))
    mkdirSync(path.join(temp, 'tests/Axis.Integration.Tests'), { recursive: true })
    writeFileSync(path.join(temp, 'scripts/docker-images.json'), JSON.stringify(['node:24-slim']))
    writeFileSync(path.join(temp, 'Dockerfile'), 'FROM node:24-slim AS web\nFROM web AS other\nFROM --platform=x node:24-slim\n')
    writeFileSync(
      path.join(temp, 'compose.yaml'),
      'services:\n  db:\n    image: example/unlisted:1.0\n  app:\n    image: ${PREFIX:-}app:${VERSION}\n',
    )
    writeFileSync(
      path.join(temp, 'tests/Axis.Integration.Tests/X.cs'),
      'var a = new StringBuilder("\\"");\nvar b = new PostgreSqlBuilder("example/other:2").Build();\nconst string Image = "example/third:3";\n',
    )
    writeFileSync(path.join(temp, 'scripts/e2e.sh'), 'image="postgres:1"\nexport TESTCONTAINERS_RYUK_CONTAINER_IMAGE=r/ryuk:1\n')
    writeFileSync(path.join(temp, 'scripts/integration.sh'), 'export TESTCONTAINERS_RYUK_CONTAINER_IMAGE=r/ryuk:2\n')
  })

  afterEach(() => rmSync(temp, { recursive: true, force: true }))

  test('reports an image that the list lacks, with its file and line', () => {
    const files = ['Dockerfile', 'compose.yaml', 'tests/Axis.Integration.Tests/X.cs', 'scripts/integration.sh', 'missing.txt']
    const unlisted = findImageReferences(temp, files).filter((reference) => !listedImages(temp).includes(reference.image))

    assert.deepEqual(unlisted, [
      { file: 'compose.yaml', line: 3, image: 'example/unlisted:1.0' },
      { file: 'tests/Axis.Integration.Tests/X.cs', line: 2, image: 'example/other:2' },
      { file: 'tests/Axis.Integration.Tests/X.cs', line: 3, image: 'example/third:3' },
      { file: 'scripts/integration.sh', line: 1, image: 'r/ryuk:2' },
    ])
  })

  test('skips a Dockerfile stage name and a Compose value built from a variable', () => {
    const images = findImageReferences(temp, ['Dockerfile', 'compose.yaml']).map((reference) => reference.image)
    assert.deepEqual(images, ['node:24-slim', 'node:24-slim', 'example/unlisted:1.0'])
  })

  test('reads the image variables of the test scripts', () => {
    const images = findImageReferences(temp, ['scripts/e2e.sh']).map((reference) => reference.image)
    assert.deepEqual(images, ['postgres:1'])
  })
})

describe('scripts/integration.sh with arguments', () => {
  // Runs the script in a temporary copy, with docker, dotnet, npm and python3 replaced by stubs that log to one file. Docker
  // lacks every image until `docker load` runs.
  function runIntegration() {
    const dir = mkdtempSync(path.join(tmpdir(), 'axis-integration-'))
    try {
      for (const file of [
        'scripts/integration.sh',
        'scripts/lib/test-results.sh',
        'scripts/lib/docker-images.mjs',
        'scripts/lib/docker-build.mjs',
        'scripts/lib/affected.mjs',
        'scripts/lib/install-sample-packages.sh',
        'scripts/lib/python-wheels.sh',
        'scripts/lib/retry.sh',
        'scripts/docker-images.json',
        'scripts/test-report.mjs',
        '.nexkit/config.json',
      ]) {
        mkdirSync(path.dirname(path.join(dir, file)), { recursive: true })
        copyFileSync(path.join(root, file), path.join(dir, file))
      }
      mkdirSync(path.join(dir, 'samples/apps/x'), { recursive: true })
      writeFileSync(path.join(dir, 'samples/apps/x/application.json'), '{"name":"x"}')
      mkdirSync(path.join(dir, 'samples/packages/p'), { recursive: true })
      writeFileSync(path.join(dir, 'samples/packages/p/axis-package.json'), '{"id":"p","version":"1.0.0","language":"python"}')
      writeFileSync(path.join(dir, 'samples/packages/p/requirements.txt'), 'x==1 --hash=sha256:aa\n')
      const cache = path.join(dir, 'cache')
      mkdirSync(cache)
      writeFileSync(path.join(cache, 'images.tar'), 'archive')
      writeFileSync(path.join(cache, 'images.json'), JSON.stringify(listed))

      const log = path.join(dir, 'log')
      const stub = (name, body) => writeFileSync(path.join(dir, 'bin', name), `#!/usr/bin/env bash\necho "${name} $*" >> "${log}"\n${body}`, { mode: 0o755 })
      mkdirSync(path.join(dir, 'bin'))
      stub(
        'docker',
        `if [ "$1" = load ]; then touch "${dir}/loaded"; fi\nif [ "$1 $2" = "image inspect" ] && [ ! -e "${dir}/loaded" ]; then exit 1; fi\nexit 0\n`,
      )
      stub('npm', 'exit 0\n')
      stub('python3', 'exit 0\n')
      stub(
        'dotnet',
        `while [ $# -gt 0 ]; do if [ "$1" = --results-directory ]; then dir="$2"; fi; shift; done
echo '<testsuites><testsuite name="s" tests="1"><testcase name="t" time="0"/></testsuite></testsuites>' > "$dir/Axis.Integration.Tests.xml"\n`,
      )

      const env = { ...process.env, PATH: `${path.join(dir, 'bin')}:${process.env.PATH}`, HOME: dir, AXIS_DOCKER_CACHE_DIR: cache }
      for (const name of ['NODE_TEST_CONTEXT', 'NEXKIT_BASE_SHA', 'NEXKIT_CHECK_VERIFIED_SHA', 'GITHUB_ACTIONS', 'GITHUB_STEP_SUMMARY', 'AXIS_TEST_RESULTS_DIR', 'AXIS_INTEGRATION_SHARD', 'NEXKIT_SHARD']) {
        delete env[name]
      }
      const result = spawnSync('bash', ['scripts/integration.sh', '--filter-class', 'X'], { cwd: dir, encoding: 'utf8', timeout: 60_000, env })
      const calls = existsSync(log) ? readFileSync(log, 'utf8').trimEnd().split('\n') : []
      return { ...result, calls, cache }
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  }

  test('installs the Python sample packages after the theme build and before dotnet test', () => {
    const run = runIntegration()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    const theme = run.calls.findIndex((call) => call.startsWith('npm run build:keycloak-theme'))
    const install = run.calls.findIndex((call) => call.startsWith('python3 -m pip install '))
    const test = run.calls.findIndex((call) => call.startsWith('dotnet test '))
    assert.ok(install > theme, run.calls.join('\n'))
    assert.ok(test > install, run.calls.join('\n'))
  })

  test('loads the saved images before the theme build and dotnet test, and pulls no image', () => {
    const run = runIntegration()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    const load = run.calls.indexOf(`docker load -i ${path.join(run.cache, 'images.tar')}`)
    const theme = run.calls.findIndex((call) => call.startsWith('npm run build:keycloak-theme'))
    const test = run.calls.findIndex((call) => call.startsWith('dotnet test '))
    assert.ok(load >= 0, run.calls.join('\n'))
    assert.ok(theme > load, run.calls.join('\n'))
    assert.ok(test > theme, run.calls.join('\n'))
    assert.ok(!run.calls.some((call) => call.startsWith('docker pull ')), run.calls.join('\n'))
  })

  test('pins the Testcontainers cleanup helper to the listed image', () => {
    const script = readFileSync(path.join(root, 'scripts/integration.sh'), 'utf8')
    const pin = script.match(/^export TESTCONTAINERS_RYUK_CONTAINER_IMAGE=(\S+)$/m)?.[1]
    assert.ok(listed.includes(pin), `${pin} is not in scripts/docker-images.json`)
  })
})
