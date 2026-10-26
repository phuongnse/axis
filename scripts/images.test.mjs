// Smoke test for the production images: builds axis-server, axis-worker and axis-admin, checks what they
// hold, and runs them read-only with their settings in /run/secrets files. Requires Docker.
// Run from scripts/e2e.sh.
import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { chmodSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { after, before, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { buildImage } from './lib/docker-build.mjs'

const root = fileURLToPath(new URL('..', import.meta.url))
const timeout = 20 * 60 * 1000
const targets = ['axis-server', 'axis-worker', 'axis-admin']
const suffix = `images-test-${process.pid}`
const network = `axis-images-${process.pid}`
const postgres = `axis-images-postgres-${process.pid}`
const secretKey = 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA='

let version
let folder
const containers = []

const image = (target) => `${target}:${suffix}`

const docker = (args, options = {}) => spawnSync('docker', args, { cwd: root, encoding: 'utf8', ...options })

const run = (args) => {
  const result = docker(args)
  assert.equal(result.status, 0, `docker ${args.join(' ')}\n${result.stdout}${result.stderr}`)
  return result.stdout.trim()
}

// writeSecrets: a folder the non-root user in the container can read, with one file per setting.
const writeSecrets = (name, settings) => {
  const dir = join(folder, name)
  mkdirSync(dir)
  chmodSync(dir, 0o755)
  for (const [key, value] of Object.entries(settings)) {
    writeFileSync(join(dir, key), value)
    chmodSync(join(dir, key), 0o644)
  }
  return dir
}

before(
  async () => {
    version = `0.0.0-test.${process.pid}`
    folder = mkdtempSync(join(tmpdir(), 'axis-images-'))
    for (const target of targets) await buildImage({ target, version, tag: image(target) })

    run(['network', 'create', network])
    run([
      'run',
      '-d',
      '--name',
      postgres,
      '--network',
      network,
      '-e',
      'POSTGRES_USER=axis',
      '-e',
      'POSTGRES_DB=axis',
      '-e',
      'POSTGRES_PASSWORD=images-test',
      '-v',
      `${join(root, 'samples/postgres/init-roles.sql')}:/docker-entrypoint-initdb.d/init-roles.sql:ro`,
      'postgres:18-alpine',
    ])
    // Check over TCP: the initialization server listens only on the Unix socket, so the check succeeds
    // only after the init scripts have run.
    let ready = false
    let running = true
    for (let i = 0; i < 60 && !ready && running; i++) {
      if (i > 0) await new Promise((resolve) => setTimeout(resolve, 1000))
      ready = docker(['exec', postgres, 'pg_isready', '-h', '127.0.0.1', '-U', 'axis', '-d', 'axis']).status === 0
      if (!ready) running = docker(['inspect', '-f', '{{.State.Running}}', postgres]).stdout.trim() === 'true'
    }
    if (!ready) {
      const logs = docker(['logs', postgres])
      assert.fail(
        `PostgreSQL ${running ? 'was not ready after 60 seconds' : 'stopped'}\n${logs.stdout}${logs.stderr}`,
      )
    }
  },
  { timeout },
)

after(() => {
  for (const container of [...containers, postgres]) docker(['rm', '-f', container], { stdio: 'ignore' })
  docker(['network', 'rm', network], { stdio: 'ignore' })
  if (folder) rmSync(folder, { recursive: true, force: true })
  for (const target of targets) docker(['rmi', '-f', image(target)], { stdio: 'ignore' })
})

test('no image holds samples or development settings', { timeout }, () => {
  for (const target of targets) {
    const id = run(['create', image(target)])
    let paths
    try {
      const exported = spawnSync('sh', ['-c', `docker export ${id} | tar -t`], {
        encoding: 'utf8',
        maxBuffer: 1024 * 1024 * 1024,
      })
      assert.equal(exported.status, 0, exported.stderr)
      paths = exported.stdout.split('\n').filter(Boolean)
    } finally {
      docker(['rm', id], { stdio: 'ignore' })
    }

    assert.deepEqual(
      paths.filter((path) => path.startsWith('samples/')),
      [],
      `${target} holds a sample`,
    )
    assert.deepEqual(
      paths.filter((path) => path.endsWith('appsettings.Development.json')),
      [],
      `${target} holds development settings`,
    )
    assert.deepEqual(
      paths.filter((path) => /^(extensions|modules)\/./.test(path)),
      [],
      `${target} holds a package or module`,
    )
    assert.ok(paths.includes('extensions/'), `${target} has no /extensions`)
    assert.ok(paths.includes('modules/'), `${target} has no /modules`)
    if (target === 'axis-server') assert.ok(paths.includes('app/wwwroot/index.html'), 'the server holds no SPA')

    const config = JSON.parse(run(['image', 'inspect', image(target)]))[0].Config
    assert.ok(config.User && config.User !== '0' && config.User !== 'root', `${target} runs as ${config.User}`)
    assert.equal(config.Labels['org.opencontainers.image.version'], version)
  }
})

// startReadOnly: starts a host read-only with its settings in a /run/secrets folder and returns its health URL.
const startReadOnly = (target, secrets, port, extra) => {
  const id = run([
    'run',
    '-d',
    '--read-only',
    '--network',
    network,
    '-p',
    `127.0.0.1::${port}`,
    '-v',
    `${secrets}:/run/secrets:ro`,
    '-e',
    'Tenancy__Source=directory',
    '-e',
    'Extensions__Path=/extensions',
    '-e',
    'Modules__Path=/modules',
    ...extra,
    image(target),
  ])
  containers.push(id)
  const mapped = run(['port', id, `${port}/tcp`]).split('\n')[0]
  return { id, url: `http://127.0.0.1:${mapped.split(':').pop()}/health/live` }
}

const connection = (user) => `Host=${postgres};Database=axis;Username=${user};Password=axis`

const waitLive = async ({ id, url }) => {
  let status = 0
  for (let i = 0; i < 120 && status !== 200; i++) {
    if (i > 0) await new Promise((resolve) => setTimeout(resolve, 1000))
    if (run(['inspect', '-f', '{{.State.Running}}', id]) !== 'true') break
    status = await fetch(url).then(
      (response) => response.status,
      () => 0,
    )
  }
  if (status !== 200) docker(['logs', id], { stdio: 'inherit' })
  assert.equal(status, 200, `${url} did not answer 200`)
  assert.notEqual(run(['exec', id, 'id', '-u']), '0')
}

test('the server starts read-only with its settings in /run/secrets', { timeout }, async () => {
  const secrets = writeSecrets('server', {
    ConnectionStrings__Platform: connection('axis_runtime'),
    ConnectionStrings__PlatformSchema: connection('axis_schema'),
    Platform__SecretKey: secretKey,
  })
  await waitLive(startReadOnly('axis-server', secrets, 8080, []))
})

test('the worker starts read-only with its settings in /run/secrets', { timeout }, async () => {
  const secrets = writeSecrets('worker', {
    ConnectionStrings__Platform: connection('axis_runtime'),
    Platform__SecretKey: secretKey,
  })
  await waitLive(startReadOnly('axis-worker', secrets, 5208, ['-e', 'Health__Urls=http://0.0.0.0:5208']))
})

test('the admin tool prints its version and has the PostgreSQL 18 tools', { timeout }, () => {
  assert.equal(run(['run', '--rm', '--read-only', image('axis-admin'), 'version']), version)
  const dump = run(['run', '--rm', '--read-only', '--entrypoint', 'pg_dump', image('axis-admin'), '--version'])
  assert.match(dump, /\(PostgreSQL\) 18\./)
  assert.notEqual(run(['run', '--rm', '--entrypoint', 'id', image('axis-admin'), '-u']), '0')
})
