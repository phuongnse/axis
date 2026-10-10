// Smoke test for `docker compose up`: builds the image from source, runs the stack on free ports
// and calls it. It uses its own Compose project, so it never touches a developer's stack or data.
// Requires Docker. Run from scripts/e2e.sh.
import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { chmodSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import net from 'node:net'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { after, before, test } from 'node:test'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('..', import.meta.url))
const project = 'axis-smoke'
const timeout = 20 * 60 * 1000

let env
let baseUrl

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
  env = {
    ...process.env,
    AXIS_SERVER_PORT: String(serverPort),
    AXIS_POSTGRES_PORT: String(postgresPort),
  }
  // The sample's tenant host is localhost.
  baseUrl = `http://localhost:${serverPort}`
})

after(() => {
  compose(['down', '-v', '--remove-orphans'], { stdio: 'ignore' })
})

test('the stack runs from source', { timeout }, async () => {
  const up = compose(['up', '-d', '--build', '--wait'], { stdio: 'inherit' })
  if (up.status !== 0) {
    compose(['logs', 'server'], { stdio: 'inherit' })
    compose(['logs', 'worker'], { stdio: 'inherit' })
    assert.fail(`docker compose up exited with ${up.status}`)
  }

  // /health is not mapped: it falls through to the SPA page, so only /health/ready proves the server.
  const health = await fetch(`${baseUrl}/health/ready`)
  assert.equal(health.status, 200)
  assert.equal((await health.json()).status, 'Healthy')

  const page = await fetch(baseUrl)
  assert.equal(page.status, 200)
  assert.match(await page.text(), /<div id="root"><\/div>/)

  const records = await fetch(`${baseUrl}/api/apps/PurchaseRequests/entities/PurchaseRequest/records`)
  assert.equal(records.status, 200)
  assert.ok(Array.isArray((await records.json()).items))

  const services = compose(['ps', '--status', 'running', '--services'])
  assert.match(services.stdout, /^worker$/m, `${services.stdout}${services.stderr}`)
  // The worker polls once a second, so it finds the migrated database soon after the server is up.
  let logs = ''
  for (let i = 0; i < 30 && !logs.includes('Tenant default is ready for work'); i++) {
    if (i > 0) await new Promise((resolve) => setTimeout(resolve, 1000))
    logs = compose(['logs', 'worker']).stdout
  }
  assert.match(logs, /Tenant default is ready for work/, logs)
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

test('the default ports are 5206 and 5432 on 127.0.0.1', () => {
  const { AXIS_SERVER_PORT, AXIS_POSTGRES_PORT, ...rest } = env
  const config = compose(['--env-file', '/dev/null', 'config', '--format', 'json'], { env: rest })
  assert.equal(config.status, 0, config.stderr)
  const { services } = JSON.parse(config.stdout)
  const published = (service) =>
    services[service].ports.map((port) => `${port.host_ip}:${port.published}->${port.target}`)
  assert.deepEqual(published('server'), ['127.0.0.1:5206->8080'])
  assert.deepEqual(published('postgres'), ['127.0.0.1:5432->5432'])
})
