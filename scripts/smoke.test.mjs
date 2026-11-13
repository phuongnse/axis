import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { productionTargets, stageText } from './lib/images.mjs'

// Runs scripts/smoke.sh in a temporary copy of the files it needs, with each smoke test replaced by a
// test that passes, or fails when it is named in FAILING, git replaced by a stub that lists the
// changed paths in FAKE_CHANGED, and docker replaced by a stub that logs its arguments and writes the file of
// `docker save -o`. `saved` names files to put in the cache folder before the run, with the placeholder HASH for
// the hash of the admin-tools stage.
const root = fileURLToPath(new URL('..', import.meta.url))
// The test files of each suite: ai-eval runs two, one at a time.
const suites = {
  'dev-script': ['dev'],
  compose: ['compose'],
  images: ['images'],
  onprem: ['onprem'],
  'ai-eval': ['ai-eval-stack', 'ai-eval-run'],
}
const hash = createHash('sha256').update(stageText(root, 'admin-tools')).digest('hex').slice(0, 12)

function runSmoke({ changed, failing = [], saved = [], dockerFails = '', env: extra = {}, images = false } = {}) {
  const dir = mkdtempSync(join(tmpdir(), 'axis-smoke-'))
  try {
    for (const file of [
      'scripts/smoke.sh',
      'scripts/lib/test-results.sh',
      'scripts/lib/affected.mjs',
      '.nexkit/config.json',
      'scripts/lib/install-modules.sh',
      'scripts/test-report.mjs',
      'Dockerfile',
      'scripts/lib/images.mjs',
      'scripts/lib/docker-build.mjs',
      'scripts/lib/smoke-images.mjs',
      'scripts/lib/docker-images.mjs',
      'scripts/docker-images.json',
      'scripts/lib/smoke-shard.mjs',
      'scripts/lib/shards.mjs',
    ]) {
      mkdirSync(dirname(join(dir, file)), { recursive: true })
      copyFileSync(join(root, file), join(dir, file))
    }
    writeFileSync(
      join(dir, 'scripts/smoke-durations.json'),
      JSON.stringify({ 'dev-script': 360, compose: 150, images: 150, onprem: 180, 'ai-eval': 120 }),
    )
    mkdirSync(join(dir, 'samples/modules/department-budgets'), { recursive: true })
    writeFileSync(join(dir, 'samples/modules/department-budgets/module.json'), '{"id":"department-budgets","version":"1.2.0"}')
    for (const [suite, files] of Object.entries(suites)) {
      const body = failing.includes(suite) ? 'assert.fail()' : ''
      for (const file of files) {
        writeFileSync(
          join(dir, `scripts/${file}.test.mjs`),
          `import assert from 'node:assert'\nimport { test } from 'node:test'\ntest('${suite}', () => {${body}})\n`,
        )
      }
    }
    const bin = join(dir, 'bin')
    mkdirSync(bin)
    writeFileSync(join(bin, 'git'), `#!/bin/sh\n[ "$1" = diff ] && printf '%s\\n' "$FAKE_CHANGED"\nexit 0\n`, {
      mode: 0o755,
    })

    writeFileSync(
      join(bin, 'docker'),
      [
        '#!/bin/sh',
        'echo "$*" >> "$DOCKER_LOG"',
        'if [ "$1" = "$DOCKER_FAIL" ]; then exit 1; fi',
        'if [ "$1" = load ]; then touch "$DOCKER_LOG.loaded"; fi',
        'if [ "$1 $2" = "image inspect" ] && [ -n "$DOCKER_IMAGES_MISSING" ] && [ ! -e "$DOCKER_LOG.loaded" ]; then exit 1; fi',
        'if [ "$1" = save ] && [ "$2" = -o ]; then echo saved > "$3"; fi',
        'exit 0',
      ].join('\n'),
      { mode: 0o755 },
    )
    const cache = join(dir, 'cache')
    mkdirSync(cache)
    for (const file of saved) writeFileSync(join(cache, file.replace('HASH', hash)), 'old')
    // The archive that setup saved, which Docker lacks until `docker load` runs: `docker image inspect` fails before it.
    if (images) {
      writeFileSync(join(cache, 'images.tar'), 'archive')
      writeFileSync(join(cache, 'images.json'), '["postgres:18-alpine"]')
    }
    const dockerLog = join(dir, 'docker.log')
    writeFileSync(dockerLog, '')

    const env = {
      ...process.env,
      PATH: `${bin}:${process.env.PATH}`,
      FAKE_CHANGED: changed ?? '',
      DOCKER_LOG: dockerLog,
      DOCKER_FAIL: dockerFails,
      DOCKER_IMAGES_MISSING: images ? '1' : '',
      AXIS_DOCKER_CACHE_DIR: cache,
      AXIS_RETRY_DELAY_SECONDS: '0',
    }
    // NODE_TEST_CONTEXT would make each suite's node --test skip its file.
    for (const name of [
      'NODE_TEST_CONTEXT',
      'NEXKIT_BASE_SHA',
      'NEXKIT_VERIFIED_SHA',
      'NEXKIT_CHECK_VERIFIED_SHA',
      'GITHUB_ACTIONS',
      'GITHUB_STEP_SUMMARY',
      'AXIS_TEST_RESULTS_DIR',
      'AXIS_SMOKE_SHARD',
      'NEXKIT_SHARD',
    ]) {
      delete env[name]
    }
    if (changed !== undefined) env.NEXKIT_BASE_SHA = 'abc'
    Object.assign(env, extra)

    const result = spawnSync('bash', ['scripts/smoke.sh'], { cwd: dir, encoding: 'utf8', timeout: 60_000, env })
    const written = Object.keys(suites).filter((suite) => existsSync(join(dir, 'artifacts/test-results', `${suite}.xml`)))
    const modules = existsSync(join(dir, 'artifacts/modules/department-budgets/1.2.0/module.json'))
    const docker = readFileSync(dockerLog, 'utf8').split('\n').filter(Boolean)
    return { ...result, written, modules, docker, cache: readdirSync(cache).sort() }
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
}

const builds = (run) => run.docker.filter((line) => line.startsWith('build --target'))
const targetOf = (line) => line.split(' ')[2]
const production = productionTargets(root)

describe('scripts/smoke.sh', () => {
  test('runs every smoke test and installs the sample modules that the on-premises test reads', () => {
    const run = runSmoke()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.deepEqual(run.written, Object.keys(suites))
    assert.ok(run.modules)
    for (const [suite, files] of Object.entries(suites)) {
      assert.match(run.stdout, new RegExp(`^\\| ${suite} \\| ${files.length} \\| 0 \\| 0 \\| `, 'm'))
    }
  })

  test('loads the saved container images once, before the first suite, and pulls no image', () => {
    const run = runSmoke({ images: true })

    assert.equal(run.status, 0, run.stdout + run.stderr)
    const loads = run.docker.filter((line) => line.startsWith('load -i ') && line.endsWith('images.tar'))
    assert.equal(loads.length, 1, run.docker.join('\n'))
    assert.ok(run.docker.indexOf(loads[0]) < run.docker.findIndex((line) => line.startsWith('build --target')), run.docker.join('\n'))
    assert.ok(!run.docker.some((line) => line.startsWith('pull ')), run.docker.join('\n'))
  })

  test('loads no container image for a docs change', () => {
    const run = runSmoke({ images: true, changed: 'docs/x.md' })

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.deepEqual(run.docker, [])
  })

  test('builds each production image once and the PostgreSQL tools layer once, then removes the images', () => {
    const run = runSmoke()

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.deepEqual(builds(run).map(targetOf).sort(), [...production, 'admin-tools'].sort(), run.docker.join('\n'))
    for (const line of builds(run).filter((line) => production.includes(targetOf(line)))) {
      assert.match(line, /--build-arg AXIS_VERSION=0\.0\.0-smoke\.\d+ .*-t axis-[\w-]+:0\.0\.0-smoke\.\d+ \.$/, line)
    }
    const version = builds(run)[1].match(/AXIS_VERSION=(\S+)/)[1]
    for (const target of production) assert.ok(run.docker.includes(`rmi -f ${target}:${version}`), run.docker.join('\n'))
    assert.deepEqual(run.cache, [`admin-tools-${hash}.tar`])
    assert.ok(run.docker.some((line) => line.startsWith(`save -o `) && line.includes(`axis-admin-tools:${hash} mcr.microsoft.com/dotnet/runtime:10.0`)))
  })

  test('loads the saved PostgreSQL tools layer of the current stage and builds axis-admin from it', () => {
    const run = runSmoke({ saved: ['admin-tools-HASH.tar'] })

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.ok(run.docker.some((line) => line.startsWith('load -i ') && line.endsWith(`admin-tools-${hash}.tar`)))
    assert.deepEqual(builds(run).map(targetOf).sort(), [...production].sort(), run.docker.join('\n'))
    assert.ok(!run.docker.some((line) => line.startsWith('pull ')), 'the base image comes from the saved file')
    const admin = builds(run).find((line) => targetOf(line) === 'axis-admin')
    assert.ok(admin.includes(`--cache-from axis-admin-tools:${hash}`), admin)
    for (const line of builds(run).filter((line) => targetOf(line) !== 'axis-admin')) assert.doesNotMatch(line, /--cache-from/)
  })

  test('builds and saves the PostgreSQL tools layer when the saved file is of an older stage, and deletes that file', () => {
    const run = runSmoke({ saved: ['admin-tools-000000000000.tar'] })

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.ok(!run.docker.some((line) => line.startsWith('load ')))
    assert.deepEqual(builds(run).map(targetOf).sort(), [...production, 'admin-tools'].sort(), run.docker.join('\n'))
    assert.deepEqual(run.cache, [`admin-tools-${hash}.tar`])
  })

  test('fails the images and on-premises tests at once when the production images do not build', () => {
    const run = runSmoke({ dockerFails: 'build' })

    assert.equal(run.status, 1, run.stdout + run.stderr)
    assert.equal(builds(run).length, 3, 'one build, tried three times')
    assert.match(run.stderr, /docker build --target admin-tools failed after 3 attempts/)
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
    assert.deepEqual(run.docker, [], 'a docs change builds nothing')
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

describe('scripts/smoke.sh runs one shard with AXIS_SMOKE_SHARD or NEXKIT_SHARD', () => {
  const allBuilds = [...production, 'admin-tools'].sort()

  test('shard 1 of 3 runs dev-script alone and builds nothing', () => {
    const run = runSmoke({ env: { AXIS_SMOKE_SHARD: '1/3' } })

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.deepEqual(run.written, ['dev-script'])
    assert.doesNotMatch(run.stdout, /^\| (compose|images|onprem) \|/m)
    assert.deepEqual(builds(run), [], run.docker.join('\n'))
  })

  test('shard 2 of 3 runs onprem and ai-eval and builds each production image once, then removes them', () => {
    const run = runSmoke({ env: { AXIS_SMOKE_SHARD: '2/3' } })

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.deepEqual(run.written, ['onprem', 'ai-eval'])
    assert.deepEqual(builds(run).map(targetOf).sort(), allBuilds, run.docker.join('\n'))
    for (const target of production) assert.ok(run.docker.some((line) => line.startsWith(`rmi -f ${target}:`)), run.docker.join('\n'))
  })

  test('shard 3 of 3 runs compose and images and builds each production image once', () => {
    const run = runSmoke({ env: { AXIS_SMOKE_SHARD: '3/3' } })

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.deepEqual(run.written, ['compose', 'images'])
    assert.deepEqual(builds(run).map(targetOf).sort(), allBuilds, run.docker.join('\n'))
  })

  test('a shard whose suites a change cannot affect builds nothing', () => {
    const run = runSmoke({ changed: 'docs/x.md', env: { AXIS_SMOKE_SHARD: '2/3' } })

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.deepEqual(run.written, [])
    assert.deepEqual(run.docker, [], 'a docs change builds nothing')
  })

  test('NEXKIT_SHARD runs a shard, and AXIS_SMOKE_SHARD wins when both are set', () => {
    assert.deepEqual(runSmoke({ env: { NEXKIT_SHARD: '2/3' } }).written, ['onprem', 'ai-eval'])
    assert.deepEqual(runSmoke({ env: { NEXKIT_SHARD: '1/3', AXIS_SMOKE_SHARD: '3/3' } }).written, ['compose', 'images'])
  })

  test('a malformed shard exits with code 2 before any suite runs', () => {
    for (const shard of ['4/3', '0/3', 'abc']) {
      const run = runSmoke({ env: { AXIS_SMOKE_SHARD: shard } })
      assert.equal(run.status, 2, run.stdout + run.stderr)
      assert.match(run.stderr, new RegExp(`AXIS_SMOKE_SHARD must be a shard such as 1/3, not '${shard}'`))
      assert.deepEqual(run.written, [])
    }
    const fromNexkit = runSmoke({ env: { NEXKIT_SHARD: '4/3' } })
    assert.equal(fromNexkit.status, 2, fromNexkit.stdout + fromNexkit.stderr)
    assert.match(fromNexkit.stderr, /NEXKIT_SHARD must be a shard such as 1\/3, not '4\/3'/)
  })

  test('a shard past the smoke suites exits with code 2 and names the fault', () => {
    const run = runSmoke({ env: { AXIS_SMOKE_SHARD: '6/6' } })

    assert.equal(run.status, 2, run.stdout + run.stderr)
    assert.match(run.stderr, /Shard 6\/6 has no smoke suite\. Use fewer shards\./)
    assert.deepEqual(run.written, [])
  })
})
