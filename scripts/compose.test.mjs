// Smoke test for `docker compose up`: builds the image from source, runs the stack on free ports
// and calls it. It uses its own Compose project, so it never touches a developer's stack or data.
// Requires Docker. Run from scripts/smoke.sh.
import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { chmodSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import net from 'node:net'
import { tmpdir } from 'node:os'
import { join, resolve as resolvePath } from 'node:path'
import { after, before, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { describeNotReady, parsePs } from './lib/compose-status.mjs'
import { runDocker } from './lib/docker-build.mjs'

const root = fileURLToPath(new URL('..', import.meta.url))
const project = 'axis-smoke'
const timeout = 20 * 60 * 1000

let env
let baseUrl
let keycloakUrl
let purchasingUrl
let mailpitUrl
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

// tail: the last characters of a command's output, for an assertion message.
const tail = (text, length = 8000) => (text.length > length ? `…${text.slice(-length)}` : text)

const compose = (args, options = {}) =>
  spawnSync('docker', ['compose', '-p', project, ...args], {
    cwd: root,
    env,
    encoding: 'utf8',
    ...options,
  })

// The ports are picked right before the stack starts. A port picked earlier, while the image builds, can be taken
// by another process, and then Docker cannot bind it.
const pickPorts = async () => {
  const serverPort = await freePort()
  const postgresPort = await freePort()
  const keycloakPort = await freePort()
  const purchasingPort = await freePort()
  const mailpitSmtpPort = await freePort()
  const mailpitWebPort = await freePort()
  env = {
    ...process.env,
    AXIS_SERVER_PORT: String(serverPort),
    AXIS_POSTGRES_PORT: String(postgresPort),
    AXIS_KEYCLOAK_PORT: String(keycloakPort),
    AXIS_PURCHASING_PORT: String(purchasingPort),
    AXIS_MAILPIT_SMTP_PORT: String(mailpitSmtpPort),
    AXIS_MAILPIT_WEB_PORT: String(mailpitWebPort),
    AXIS_STUDIO_APPS_DIR: studioApps,
  }
  // The sample's tenant host is localhost.
  baseUrl = `http://localhost:${serverPort}`
  keycloakUrl = `http://localhost:${keycloakPort}`
  purchasingUrl = `http://localhost:${purchasingPort}`
  mailpitUrl = `http://localhost:${mailpitWebPort}`
}

before(() => {
  studioApps = mkdtempSync(join(tmpdir(), 'axis-studio-apps-'))
  // The server's user, UID 1654 on Linux, writes the folder.
  chmodSync(studioApps, 0o777)
})

after(() => {
  compose(['down', '-v', '--remove-orphans'], { stdio: 'ignore' })
  rmSync(studioApps, { recursive: true, force: true })
})

test('the stack runs from source', { timeout }, async () => {
  // Keycloak mounts the Axis theme from artifacts/keycloak-theme, so it is built first.
  const theme = spawnSync('npm', ['run', 'build:keycloak-theme', '--prefix', 'web'], { cwd: root, encoding: 'utf8' })
  assert.equal(theme.status, 0, `the Keycloak theme build exited with ${theme.status}\n${tail(`${theme.stdout}${theme.stderr}`)}`)

  // The build runs on its own, so a failed build is not reported as a failed start.
  await runDocker(['compose', '-p', project, 'build'], { env })

  // A port that was free when it was picked can be taken before Docker binds it. Docker then fails the start with a
  // bind error, so the start is tried again on new ports. Any other failure is reported at once.
  let up
  for (let attempt = 1; attempt <= 3; attempt++) {
    await pickPorts()
    up = compose(['up', '-d', '--wait'])
    process.stdout.write(`${up.stdout ?? ''}${up.stderr ?? ''}`)
    if (up.status === 0 || !/port is already allocated|address already in use|ports are not available/i.test(up.stderr ?? '')) break
    compose(['down', '-v', '--remove-orphans'], { stdio: 'ignore' })
  }
  if (up.status !== 0) {
    const ps = compose(['ps', '--all', '--format', 'json'])
    const containers = parsePs(ps.stdout ?? '')
    const lastChecks = {}
    for (const { Service: service, Name: name, Health: health } of containers) {
      if (health !== 'unhealthy' && health !== 'starting') continue
      const inspect = spawnSync('docker', ['inspect', '--format', '{{json .State.Health}}', name], { encoding: 'utf8' })
      const log = inspect.status === 0 ? (JSON.parse(inspect.stdout).Log ?? []) : []
      lastChecks[service] = log.at(-1)?.Output ?? ''
    }
    for (const service of ['server', 'worker', 'publisher', 'keycloak', 'purchasing', 'mailpit', 'postgres']) {
      compose(['logs', service], { stdio: 'inherit' })
    }
    assert.fail(`docker compose up exited with ${up.status}\n${tail(up.stderr ?? '', 2000)}\n${describeNotReady(containers, lastChecks)}`)
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
  // SecurityHeadersTests pins the exact Content-Type of this problem.
  assert.equal((await records.json()).status, 401)

  // Keycloak serves the development realm. Sign-in through Compose is not wired yet.
  const discovery = await fetch(`${keycloakUrl}/realms/axis/.well-known/openid-configuration`)
  assert.equal(discovery.status, 200)
  assert.match((await discovery.json()).issuer, /\/realms\/axis$/)

  // The purchasing system stub answers.
  const purchasing = await fetch(`${purchasingUrl}/health`)
  assert.equal(purchasing.status, 200)

  // Mailpit runs and its web page answers. The worker sends its emails there.
  assert.match(compose(['ps', '--services', '--status', 'running']).stdout, /^mailpit$/m)
  const mailpit = await fetch(`${mailpitUrl}/readyz`)
  assert.equal(mailpit.status, 200)

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
  for (const application of ['PurchaseRequests', 'LeaveRequests', 'ServiceDesk', 'AssetManagement']) {
    assert.match(serverLogs, new RegExp(`Activated application ${application} .* for tenant default-preview\\.`), serverLogs)
  }
  assert.match(serverLogs, /AXC0100 in application folder .* for tenant default-preview/, serverLogs)

  // Both images hold the sample modules departments and department-budgets, and the server loads them.
  for (const service of ['server', 'worker']) {
    for (const version of ['departments/1.1.0', 'department-budgets/1.1.0']) {
      const module = compose(['exec', '-T', service, 'test', '-f', `/modules/${version}/module.json`])
      assert.equal(module.status, 0, `${service} ${version}: ${module.stderr}`)
    }
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
  assert.deepEqual(
    parsePs(ps.stdout).map((container) => container.Health),
    ['healthy'],
    ps.stdout,
  )
})

test('a broken application folder stops the server', { timeout }, async () => {
  // The run below must not build: a build log in its output hid the server's output.
  await runDocker(['compose', '-p', project, 'build', 'server'], { env })

  // No id and no name: the folder does not compile.
  const folder = mkdtempSync(join(tmpdir(), 'axis-broken-'))
  try {
    writeFileSync(join(folder, 'application.json'), JSON.stringify({ kind: 'application', formatVersion: 1 }))
    // The container runs as a non-root user.
    chmodSync(folder, 0o755)
    chmodSync(join(folder, 'application.json'), 0o644)

    const run = compose(
      ['run', '--rm', '--no-TTY', '-v', `${folder}:/broken:ro`, '-e', 'ActivateOnStartup__0=/broken', 'server'],
      { maxBuffer: 64 * 1024 * 1024 },
    )
    const output = `${tail(run.stdout ?? '')}\n--- stderr ---\n${tail(run.stderr ?? '')}`
    assert.equal(run.error, undefined, `${run.error}\n${output}`)
    assert.equal(run.signal, null, `killed by ${run.signal}\n${output}`)
    assert.ok(typeof run.status === 'number' && run.status !== 0, `exited with ${run.status}\n${output}`)
    assert.match(`${run.stdout}${run.stderr}`, /in application folder \/broken for tenant default/, output)
  } finally {
    rmSync(folder, { recursive: true, force: true })
  }
})

test('the default ports are 5206, 5432, 8080, 5207, 1025 and 8025 on 127.0.0.1', () => {
  const {
    AXIS_SERVER_PORT,
    AXIS_POSTGRES_PORT,
    AXIS_KEYCLOAK_PORT,
    AXIS_PURCHASING_PORT,
    AXIS_MAILPIT_SMTP_PORT,
    AXIS_MAILPIT_WEB_PORT,
    ...rest
  } = env
  const config = compose(['--env-file', '/dev/null', 'config', '--format', 'json'], { env: rest })
  assert.equal(config.status, 0, config.stderr)
  const { services } = JSON.parse(config.stdout)
  const published = (service) =>
    services[service].ports.map((port) => `${port.host_ip}:${port.published}->${port.target}`)
  assert.deepEqual(published('server'), ['127.0.0.1:5206->8080'])
  assert.deepEqual(published('postgres'), ['127.0.0.1:5432->5432'])
  assert.deepEqual(published('keycloak'), ['127.0.0.1:8080->8080'])
  assert.deepEqual(published('purchasing'), ['127.0.0.1:5207->8080'])
  assert.deepEqual(published('mailpit'), ['127.0.0.1:1025->1025', '127.0.0.1:8025->8025'])
})

test('the worker sends email to mailpit', () => {
  const config = compose(['config', '--format', 'json'])
  assert.equal(config.status, 0, config.stderr)
  const { services } = JSON.parse(config.stdout)
  const { environment } = services.worker
  assert.equal(environment.Email__Smtp__Host, 'mailpit')
  assert.equal(environment.Email__Smtp__Port, '1025')
  assert.equal(environment.Email__Smtp__UseTls, 'false')
  assert.ok(environment.Email__From)
  assert.ok(services.worker.depends_on.mailpit)
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
  // The publisher reads no appsettings file, so it gets the development tenant keys itself.
  for (const tenant of ['default', 'default-preview']) {
    assert.equal(Buffer.from(publisher[`Tenants__${tenant}__DataKeys__1`], 'base64').length, 32)
    assert.equal(Buffer.from(publisher[`Tenants__${tenant}__LookupKeys__1`], 'base64').length, 32)
  }
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

// Compose prints a duration such as `5m0s`.
const seconds = (duration) =>
  [...duration.matchAll(/(\d+(?:\.\d+)?)(h|m|s)/g)].reduce(
    (total, [, value, unit]) => total + Number(value) * { h: 3600, m: 60, s: 1 }[unit],
    0,
  )

test('the server has at least 5 minutes to become healthy and the worker more', () => {
  const config = compose(['config', '--format', 'json'])
  assert.equal(config.status, 0, config.stderr)
  const { services } = JSON.parse(config.stdout)
  const server = seconds(services.server.healthcheck.start_period)
  const worker = seconds(services.worker.healthcheck.start_period)
  assert.ok(server >= 300, `the server's start_period is ${server} s`)
  assert.ok(worker > server, `the worker's start_period is ${worker} s, the server's ${server} s`)
})
