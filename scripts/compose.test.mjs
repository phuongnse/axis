// Smoke test for `docker compose up`: builds the image from source, runs the stack on free ports
// and calls it. It uses its own Compose project, so it never touches a developer's stack or data.
// Requires Docker. Run from scripts/e2e.sh.
import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { chmodSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import net from 'node:net'
import { tmpdir } from 'node:os'
import { join, resolve as resolvePath } from 'node:path'
import { after, before, test } from 'node:test'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('..', import.meta.url))
const project = 'axis-smoke'
const timeout = 20 * 60 * 1000

let env
let baseUrl
let keycloakUrl
let purchasingUrl
let studioApps

const freePort = () =>
  new Promise((resolve, reject) => {
    const server = net.createServer()
    server.once('error', reject)
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address()
      server.close(() => resolve(port))
    })
  })

const compose = (args, options = {}) =>
  spawnSync('docker', ['compose', '-p', project, ...args], {
    cwd: root,
    env,
    encoding: 'utf8',
    ...options,
  })

before(async () => {
  const serverPort = await freePort()
  const postgresPort = await freePort()
  const keycloakPort = await freePort()
  const purchasingPort = await freePort()
  studioApps = mkdtempSync(join(tmpdir(), 'axis-studio-apps-'))
  // The server's user, UID 1654 on Linux, writes the folder.
  chmodSync(studioApps, 0o777)
  env = {
    ...process.env,
    AXIS_SERVER_PORT: String(serverPort),
    AXIS_POSTGRES_PORT: String(postgresPort),
    AXIS_KEYCLOAK_PORT: String(keycloakPort),
    AXIS_PURCHASING_PORT: String(purchasingPort),
    AXIS_STUDIO_APPS_DIR: studioApps,
  }
  // The sample's tenant host is localhost.
  baseUrl = `http://localhost:${serverPort}`
  keycloakUrl = `http://localhost:${keycloakPort}`
  purchasingUrl = `http://localhost:${purchasingPort}`
})

after(() => {
  compose(['down', '-v', '--remove-orphans'], { stdio: 'ignore' })
  rmSync(studioApps, { recursive: true, force: true })
})

test('the stack runs from source', { timeout }, async () => {
  const up = compose(['up', '-d', '--build', '--wait'], { stdio: 'inherit' })
  if (up.status !== 0) {
    compose(['logs', 'server'], { stdio: 'inherit' })
    compose(['logs', 'worker'], { stdio: 'inherit' })
    compose(['logs', 'publisher'], { stdio: 'inherit' })
    compose(['logs', 'keycloak'], { stdio: 'inherit' })
    compose(['logs', 'purchasing'], { stdio: 'inherit' })
    assert.fail(`docker compose up exited with ${up.status}`)
  }

  // /health is not mapped: it falls through to the SPA page, so only /health/ready proves the server.
  const health = await fetch(`${baseUrl}/health/ready`)
  assert.equal(health.status, 200)
  assert.equal((await health.json()).status, 'Healthy')

  const page = await fetch(baseUrl)
  assert.equal(page.status, 200)
  assert.match(await page.text(), /<div id="root"><\/div>/)

  // The record API needs a session. Sign-in through Compose is not wired yet, so the server
  // answers the purchase request list with a 401 problem.
  const records = await fetch(`${baseUrl}/api/apps/PurchaseRequests/entities/PurchaseRequest/records`)
  assert.equal(records.status, 401)
  assert.equal(records.headers.get('content-type'), 'application/problem+json')
  assert.equal((await records.json()).status, 401)

  // Keycloak serves the development realm. Sign-in through Compose is not wired yet.
  const discovery = await fetch(`${keycloakUrl}/realms/axis/.well-known/openid-configuration`)
  assert.equal(discovery.status, 200)
  assert.match((await discovery.json()).issuer, /\/realms\/axis$/)

  // The purchasing system stub answers.
  const purchasing = await fetch(`${purchasingUrl}/health`)
  assert.equal(purchasing.status, 200)

  // Activation ran as axis_schema, so that role owns the database and the schemas it created.
  const psql = (sql) =>
    compose(['exec', '-T', 'postgres', 'psql', '-U', 'axis', '-d', 'axis', '-tAc', sql]).stdout.trim()
  assert.equal(psql("select datdba::regrole from pg_database where datname = 'axis'"), 'axis_schema')
  assert.equal(
    psql(
      "select string_agg(nspname || '=' || nspowner::regrole, ',' order by nspname) from pg_namespace where nspname in ('axis','entities')",
    ),
    'axis=axis_schema,entities=axis_schema',
  )

  // The preview tenant default-preview has its own database and roles. The server activated both samples on
  // it, and it warns about the purchasing system bindings that the preview does not have.
  const previewPsql = (sql) =>
    compose(['exec', '-T', 'postgres', 'psql', '-U', 'axis', '-d', 'axis_preview', '-tAc', sql]).stdout.trim()
  assert.equal(
    previewPsql("select datdba::regrole from pg_database where datname = 'axis_preview'"),
    'axis_preview_schema',
  )
  assert.equal(
    previewPsql(
      "select string_agg(nspname || '=' || nspowner::regrole, ',' order by nspname) from pg_namespace where nspname in ('axis','entities')",
    ),
    'axis=axis_preview_schema,entities=axis_preview_schema',
  )
  const serverLogs = compose(['logs', 'server']).stdout
  for (const application of ['PurchaseRequests', 'LeaveRequests', 'ServiceDesk']) {
    assert.match(serverLogs, new RegExp(`Activated application ${application} .* for tenant default-preview\\.`), serverLogs)
  }
  assert.match(serverLogs, /AXC0100 in application folder .* for tenant default-preview/, serverLogs)

  // Both images hold the sample module departments, and the server loads it.
  for (const service of ['server', 'worker']) {
    const module = compose(['exec', '-T', service, 'test', '-f', '/modules/departments/1.0.0/module.json'])
    assert.equal(module.status, 0, `${service}: ${module.stderr}`)
  }
  assert.doesNotMatch(compose(['logs', 'server']).stdout, /The module version is not loaded/)

  // The server's applications folder is the bind mount of the temporary folder.
  const applicationsFolder = compose(['exec', '-T', 'server', 'test', '-d', '/studio-apps'])
  assert.equal(applicationsFolder.status, 0, applicationsFolder.stderr)

  const services = compose(['ps', '--status', 'running', '--services'])
  assert.match(services.stdout, /^worker$/m, `${services.stdout}${services.stderr}`)
  assert.match(services.stdout, /^publisher$/m, `${services.stdout}${services.stderr}`)
  // The worker polls once a second, so it finds the migrated database soon after the server is up.
  let logs = ''
  for (let i = 0; i < 30 && !logs.includes('Tenant default is ready for work'); i++) {
    if (i > 0) await new Promise((resolve) => setTimeout(resolve, 1000))
    logs = compose(['logs', 'worker']).stdout
  }
  assert.match(logs, /Tenant default is ready for work/, logs)
  // The publisher waits for the server's migration the same way, then writes one ready line.
  let publisherLogs = ''
  for (let i = 0; i < 30 && !publisherLogs.includes('default ready'); i++) {
    if (i > 0) await new Promise((resolve) => setTimeout(resolve, 1000))
    publisherLogs = compose(['logs', 'publisher']).stdout
  }
  assert.match(publisherLogs, /default ready/, publisherLogs)

  // `up --wait` returned, so the worker's health check on /health/ready passed.
  const ps = compose(['ps', '--format', 'json', 'worker'])
  assert.equal(ps.status, 0, ps.stderr)
  // Newer Compose versions print one JSON object per line, older ones an array.
  const text = ps.stdout.trim()
  const containers = text.startsWith('[') ? JSON.parse(text) : text.split('\n').map((line) => JSON.parse(line))
  assert.deepEqual(
    containers.map((container) => container.Health),
    ['healthy'],
    ps.stdout,
  )
})

test('a broken application folder stops the server', { timeout }, () => {
  // No id and no name: the folder does not compile.
  const folder = mkdtempSync(join(tmpdir(), 'axis-broken-'))
  try {
    writeFileSync(join(folder, 'application.json'), JSON.stringify({ kind: 'application', formatVersion: 1 }))
    // The container runs as a non-root user.
    chmodSync(folder, 0o755)
    chmodSync(join(folder, 'application.json'), 0o644)

    const run = compose([
      'run',
      '--rm',
      '--no-TTY',
      '-v',
      `${folder}:/broken:ro`,
      '-e',
      'ActivateOnStartup__0=/broken',
      'server',
    ])
    const output = `${run.stdout}${run.stderr}`
    assert.notEqual(run.status, 0, output)
    assert.match(output, /in application folder \/broken for tenant default/, output)
  } finally {
    rmSync(folder, { recursive: true, force: true })
  }
})

test('the default ports are 5206, 5432, 8080 and 5207 on 127.0.0.1', () => {
  const { AXIS_SERVER_PORT, AXIS_POSTGRES_PORT, AXIS_KEYCLOAK_PORT, AXIS_PURCHASING_PORT, ...rest } = env
  const config = compose(['--env-file', '/dev/null', 'config', '--format', 'json'], { env: rest })
  assert.equal(config.status, 0, config.stderr)
  const { services } = JSON.parse(config.stdout)
  const published = (service) =>
    services[service].ports.map((port) => `${port.host_ip}:${port.published}->${port.target}`)
  assert.deepEqual(published('server'), ['127.0.0.1:5206->8080'])
  assert.deepEqual(published('postgres'), ['127.0.0.1:5432->5432'])
  assert.deepEqual(published('keycloak'), ['127.0.0.1:8080->8080'])
  assert.deepEqual(published('purchasing'), ['127.0.0.1:5207->8080'])
})

test('the server creates its applications in artifacts/studio-apps, which git ignores', () => {
  const { AXIS_STUDIO_APPS_DIR, ...rest } = env
  const config = compose(['--env-file', '/dev/null', 'config', '--format', 'json'], { env: rest })
  assert.equal(config.status, 0, config.stderr)
  const { services } = JSON.parse(config.stdout)
  assert.equal(services.server.environment.Studio__ApplicationsPath, '/studio-apps')
  const folder = resolvePath(root, 'artifacts/studio-apps')
  assert.ok(
    services.server.volumes.some(
      (volume) => volume.type === 'bind' && volume.target === '/studio-apps' && resolvePath(volume.source) === folder,
    ),
    JSON.stringify(services.server.volumes),
  )
  const ignored = spawnSync('git', ['check-ignore', '-q', 'artifacts/studio-apps/new-app/application.json'], { cwd: root })
  assert.equal(ignored.status, 0, 'artifacts/studio-apps is not ignored by git')
})

test('the server and the publisher run on both database roles and the worker only on the runtime role', () => {
  const config = compose(['config', '--format', 'json'])
  assert.equal(config.status, 0, config.stderr)
  const { services } = JSON.parse(config.stdout)
  const server = services.server.environment
  assert.match(server.ConnectionStrings__Platform, /Username=axis_runtime;/)
  assert.match(server.ConnectionStrings__PlatformSchema, /Username=axis_schema;/)
  assert.match(server.Tenants__default__ConnectionString, /Username=axis_runtime;/)
  assert.match(server.Tenants__default__SchemaConnectionString, /Username=axis_schema;/)
  assert.match(server['Tenants__default-preview__ConnectionString'], /Database=axis_preview;Username=axis_preview_runtime;/)
  assert.match(
    server['Tenants__default-preview__SchemaConnectionString'],
    /Database=axis_preview;Username=axis_preview_schema;/,
  )
  const worker = services.worker.environment
  assert.deepEqual(
    Object.keys(worker).filter((key) => /SchemaConnectionString/.test(key)),
    [],
  )
  assert.deepEqual(
    Object.keys(worker).filter((key) => /PlatformSchema/.test(key)),
    [],
  )
  assert.match(worker.Tenants__default__ConnectionString, /Username=axis_runtime;/)
  const publisher = services.publisher.environment
  assert.match(publisher.Tenants__default__ConnectionString, /Username=axis_runtime;/)
  assert.match(publisher.Tenants__default__SchemaConnectionString, /Username=axis_schema;/)
  assert.equal(publisher['Tenants__default-preview__PreviewOf'], 'default')
  assert.match(publisher['Tenants__default-preview__ConnectionString'], /Database=axis_preview;Username=axis_preview_runtime;/)
  assert.match(publisher['Tenants__default-preview__SchemaConnectionString'], /Database=axis_preview;Username=axis_preview_schema;/)
  assert.match(worker['Tenants__default-preview__ConnectionString'], /Database=axis_preview;Username=axis_preview_runtime;/)
  // Only the publisher gets the role that creates the databases of test scenarios.
  assert.match(publisher.Scenarios__ServerConnectionString, /Database=postgres;Username=axis_scenario;/)
  for (const [name, environment] of Object.entries({ server, worker })) {
    assert.deepEqual(
      Object.keys(environment).filter((key) => /^Scenarios__/.test(key)),
      [],
      `${name} has a scenario setting`,
    )
  }
})

test('the worker checks /health/ready and publishes no port', () => {
  const config = compose(['config', '--format', 'json'])
  assert.equal(config.status, 0, config.stderr)
  const { services } = JSON.parse(config.stdout)
  assert.equal(services.worker.ports, undefined)
  assert.match(services.worker.healthcheck.test.join(' '), /\/health\/ready/)
})
