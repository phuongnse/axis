// Smoke test for the on-premises installation: installs deploy/compose/compose.yaml from the three
// production images exactly as docs/reference/installation.md says. It runs against a test PostgreSQL
// and Keycloak, creates two tenants, activates the leave request sample and checks each tenant's
// site and sign-in, suspend and resume, and an unknown host. Requires Docker and openssl, and the
// sample modules in artifacts/modules (scripts/build.sh). Run from scripts/e2e.sh.
//
// Everything the test adds lives in an override file in a temporary folder: PostgreSQL, Keycloak over
// HTTPS with a certificate authority made for the run, the modules folder and a short refresh interval.
// The production server refuses an identity provider over plain http, so Keycloak serves https and only
// the server in this test trusts the certificate authority, through SSL_CERT_FILE.
import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { randomBytes } from 'node:crypto'
import { chmodSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import http from 'node:http'
import https from 'node:https'
import net from 'node:net'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { after, before, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('..', import.meta.url))
const composeFile = join(root, 'deploy/compose/compose.yaml')
const timeout = 30 * 60 * 1000
const targets = ['axis-server', 'axis-worker', 'axis-admin']
const keycloakAuthority = 'https://keycloak:8443/realms/axis'
const clientSecret = 'axis-development-secret'
const hosts = { one: 'one.axis.test', two: 'two.axis.test' }

const secretFiles = {
  server: ['ConnectionStrings__Platform', 'ConnectionStrings__PlatformSchema', 'Platform__SecretKey'],
  worker: ['ConnectionStrings__Platform', 'Platform__SecretKey'],
  admin: ['Admin__ConnectionString', 'ConnectionStrings__PlatformSchema', 'Platform__SecretKey'],
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

const freePort = () =>
  new Promise((resolve, reject) => {
    const server = net.createServer()
    server.once('error', reject)
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address()
      server.close(() => resolve(port))
    })
  })

const password = () => randomBytes(18).toString('hex')

const files = (dir) =>
  readdirSync(dir, { withFileTypes: true }).flatMap((entry) =>
    entry.isDirectory() ? files(join(dir, entry.name)) : [join(dir, entry.name)],
  )

describe('the example holds no secret', () => {
  test('no file under deploy/compose holds a password, a client secret or a key', () => {
    const found = files(join(root, 'deploy/compose'))
    assert.ok(found.length > 0)
    for (const file of found) {
      const text = readFileSync(file, 'utf8')
      assert.doesNotMatch(text, /Password\s*=/i, `${file} holds a password`)
      assert.doesNotMatch(text, /[A-Z ]*PRIVATE KEY/, `${file} holds a private key`)
      assert.doesNotMatch(text, /[A-Za-z0-9+/]{40,}={0,2}/, `${file} holds a long secret-like value`)
    }
  })

  test('the rendered config takes every connection string and the installation key only from secrets files', () => {
    const rendered = spawnSync('docker', ['compose', '-f', composeFile, '--profile', 'admin', 'config', '--format', 'json'], {
      cwd: root,
      encoding: 'utf8',
      env: { ...process.env, AXIS_VERSION: 'x', AXIS_OIDC_CLIENT_SECRET: '' },
    })
    assert.equal(rendered.status, 0, rendered.stderr)
    const config = JSON.parse(rendered.stdout)
    assert.deepEqual(Object.keys(config.services).sort(), ['admin', 'server', 'worker'])

    for (const [name, service] of Object.entries(config.services)) {
      for (const [key, value] of Object.entries(service.environment ?? {})) {
        if (key === 'AXIS_OIDC_CLIENT_SECRET') {
          assert.ok(value === null || value === undefined || value === '', `${name} sets ${key}`)
        } else {
          assert.doesNotMatch(key, /ConnectionString|SecretKey|Password/, `${name} sets ${key} in its environment`)
        }
      }
      const secrets = (service.secrets ?? []).map((secret) => secret.source).sort()
      assert.deepEqual(secrets, [...secretFiles[name]].sort(), `${name} secrets`)
      for (const secret of service.secrets ?? []) assert.equal(secret.target, `/run/secrets/${secret.source}`)
    }
    assert.equal(
      config.services.worker.secrets.some((secret) => secret.source === 'ConnectionStrings__PlatformSchema'),
      false,
      'the worker holds the schema connection string',
    )
    assert.deepEqual(config.services.admin.profiles, ['admin'])
    assert.equal(config.services.server.profiles, undefined)
    assert.equal(config.services.worker.profiles, undefined)
    for (const secret of Object.values(config.secrets)) assert.ok(secret.file, 'a secret has no file')
  })
})

describe('the on-premises installation', () => {
  const project = `axis-onprem-${process.pid}`
  const version = `0.0.0-onprem.${process.pid}`
  let folder
  let env
  let serverPort
  let workerPort
  let keycloakPort
  let overrideFile

  const docker = (args, options = {}) => spawnSync('docker', args, { cwd: root, encoding: 'utf8', ...options })

  const compose = (args, options = {}) =>
    spawnSync('docker', ['compose', '-p', project, '-f', composeFile, '-f', overrideFile, ...args], {
      cwd: root,
      env,
      encoding: 'utf8',
      ...options,
    })

  const run = (args, options) => {
    const result = compose(args, options)
    assert.equal(result.status, 0, `docker compose ${args.join(' ')}\n${result.stdout}${result.stderr}`)
    return result.stdout.trim()
  }

  const dumpLogs = () => {
    for (const service of ['server', 'worker']) {
      console.log(`--- docker compose logs ${service}`)
      console.log(compose(['logs', '--no-color', service]).stdout)
    }
  }

  // writeFile: readable by the non-root user in the containers, written with no trailing newline.
  const writeFile = (path, content) => {
    writeFileSync(path, content)
    chmodSync(path, 0o644)
  }

  const openssl = (...args) => {
    const result = spawnSync('openssl', args, { cwd: folder, encoding: 'utf8' })
    assert.equal(result.status, 0, `openssl ${args.join(' ')}\n${result.stdout}${result.stderr}`)
  }

  // request: sends a request to the server or the worker with a Host header, which fetch cannot set.
  const request = (port, host, path) =>
    new Promise((resolve) => {
      const req = http.request({ host: '127.0.0.1', port, path, method: 'GET', headers: { host } }, (response) => {
        const chunks = []
        response.on('data', (chunk) => chunks.push(chunk))
        response.on('end', () =>
          resolve({ status: response.statusCode, headers: response.headers, body: Buffer.concat(chunks).toString() }),
        )
      })
      req.setTimeout(10000, () => req.destroy())
      req.on('error', () => resolve({ status: 0, headers: {}, body: '' }))
      req.end()
    })

  const server = (host, path) => request(serverPort, host, path)

  // waitFor: polls until the status is the expected one and returns the last response.
  const waitFor = async (get, status, seconds) => {
    let response
    for (let i = 0; i < seconds; i++) {
      if (i > 0) await sleep(1000)
      response = await get()
      if (response.status === status) break
    }
    return response
  }

  const admin = (args, extraEnv = {}) =>
    compose(['run', '--rm', '--no-TTY', 'admin', ...args], { env: { ...env, ...extraEnv } })

  before(
    async () => {
      folder = mkdtempSync(join(tmpdir(), 'axis-onprem-'))
      chmodSync(folder, 0o755)

      // Step 1, the images. The three targets are tagged with the version that compose.yaml requires.
      for (const target of targets) {
        const build = docker(
          ['build', '--target', target, '--build-arg', `AXIS_VERSION=${version}`, '-t', `${target}:${version}`, '.'],
          { stdio: 'inherit' },
        )
        assert.equal(build.status, 0, `docker build --target ${target} exited with ${build.status}`)
      }

      // Step 2, the secret files. The folder and the files are readable by UID 1654.
      const secrets = join(folder, 'secrets')
      mkdirSync(secrets)
      chmodSync(secrets, 0o755)
      const passwords = { schema: password(), platform: password(), admin: password(), postgres: password() }
      const connection = (user, pass) => `Host=postgres;Port=5432;Database=axis_platform;Username=${user};Password=${pass}`
      writeFile(join(secrets, 'ConnectionStrings__PlatformSchema'), connection('axis_platform_schema', passwords.schema))
      writeFile(join(secrets, 'ConnectionStrings__Platform'), connection('axis_platform', passwords.platform))
      writeFile(
        join(secrets, 'Admin__ConnectionString'),
        `Host=postgres;Port=5432;Database=postgres;Username=axis_admin;Password=${passwords.admin}`,
      )
      writeFile(join(secrets, 'Platform__SecretKey'), randomBytes(32).toString('base64'))

      // Step 3, the database roles. This is the SQL of the installation guide.
      const initSql = join(folder, 'init-roles.sql')
      writeFile(
        initSql,
        [
          `CREATE ROLE axis_platform_schema LOGIN PASSWORD '${passwords.schema}';`,
          `CREATE ROLE axis_platform LOGIN PASSWORD '${passwords.platform}';`,
          `CREATE ROLE axis_admin LOGIN PASSWORD '${passwords.admin}' CREATEDB CREATEROLE;`,
          'CREATE DATABASE axis_platform OWNER axis_platform_schema;',
          '',
        ].join('\n'),
      )

      // Step 4, the certificates: a certificate authority and a certificate of Keycloak for the name keycloak.
      const certs = join(folder, 'certs')
      mkdirSync(certs)
      chmodSync(certs, 0o755)
      const at = (name) => join('certs', name)
      writeFile(join(folder, 'leaf.ext'), 'subjectAltName=DNS:keycloak\nbasicConstraints=CA:FALSE\n')
      openssl('req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '2', '-subj', '/CN=Axis onprem test CA', '-keyout', at('ca.key'), '-out', at('ca.crt'), '-addext', 'basicConstraints=critical,CA:TRUE', '-addext', 'keyUsage=critical,keyCertSign,cRLSign')
      openssl('req', '-newkey', 'rsa:2048', '-nodes', '-subj', '/CN=keycloak', '-keyout', at('tls.key'), '-out', at('tls.csr'))
      openssl('x509', '-req', '-in', at('tls.csr'), '-CA', at('ca.crt'), '-CAkey', at('ca.key'), '-CAcreateserial', '-days', '2', '-extfile', 'leaf.ext', '-out', at('tls.crt'))
      for (const name of readdirSync(certs)) chmodSync(join(certs, name), 0o644)

      serverPort = await freePort()
      workerPort = await freePort()

      // The override file. JSON is valid YAML, so the test writes it as JSON.
      const refresh = { Tenancy__RefreshInterval: '00:00:02' }
      overrideFile = join(folder, 'override.json')
      writeFile(
        overrideFile,
        JSON.stringify({
          services: {
            postgres: {
              image: 'postgres:18-alpine',
              environment: { POSTGRES_USER: 'postgres', POSTGRES_PASSWORD: passwords.postgres },
              volumes: [`${initSql}:/docker-entrypoint-initdb.d/init-roles.sql:ro`],
            },
            keycloak: {
              image: 'quay.io/keycloak/keycloak:26.4',
              command: [
                'start-dev',
                '--import-realm',
                '--https-port=8443',
                '--https-certificate-file=/certs/tls.crt',
                '--https-certificate-key-file=/certs/tls.key',
              ],
              volumes: [`${join(root, 'samples/keycloak')}:/opt/keycloak/data/import:ro`, `${certs}:/certs:ro`],
              ports: ['127.0.0.1::8443'],
            },
            server: {
              environment: { ...refresh, SSL_CERT_FILE: '/run/test-ca/ca.crt' },
              volumes: [`${certs}:/run/test-ca:ro`],
            },
            worker: { environment: refresh },
          },
        }),
      )

      env = {
        ...process.env,
        AXIS_VERSION: version,
        AXIS_SECRETS_DIR: secrets,
        AXIS_MODULES_DIR: join(root, 'artifacts/modules'),
        AXIS_APPS_DIR: join(root, 'samples/apps'),
        AXIS_SERVER_PORT: String(serverPort),
        AXIS_WORKER_HEALTH_PORT: String(workerPort),
      }
      delete env.AXIS_OIDC_CLIENT_SECRET
      assert.ok(statSync(env.AXIS_MODULES_DIR).isDirectory(), 'run scripts/build.sh first: artifacts/modules is missing')
    },
    { timeout },
  )

  after(() => {
    compose(['down', '-v', '--remove-orphans'], { stdio: 'ignore' })
    if (folder) rmSync(folder, { recursive: true, force: true })
    for (const target of targets) docker(['rmi', '-f', `${target}:${version}`], { stdio: 'ignore' })
  })

  test('the guide installs two tenants and serves them', { timeout }, async () => {
    try {
      // The database server and the identity provider run outside the example.
      run(['up', '-d', 'postgres', 'keycloak'])

      // Check over TCP: the initialization server listens only on the Unix socket, so the check
      // succeeds only after the init script has run.
      let ready = false
      for (let i = 0; i < 60 && !ready; i++) {
        if (i > 0) await sleep(1000)
        ready = compose(['exec', '-T', 'postgres', 'pg_isready', '-h', '127.0.0.1', '-U', 'postgres']).status === 0
      }
      assert.ok(ready, `PostgreSQL was not ready after 60 seconds\n${compose(['logs', 'postgres']).stdout}`)

      keycloakPort = run(['port', 'keycloak', '8443']).split('\n')[0].split(':').pop()
      let discovery = 0
      for (let i = 0; i < 180 && discovery !== 200; i++) {
        if (i > 0) await sleep(1000)
        discovery = await new Promise((resolve) => {
          const req = https.get(
            `https://127.0.0.1:${keycloakPort}/realms/axis/.well-known/openid-configuration`,
            { rejectUnauthorized: false },
            (response) => {
              response.resume()
              resolve(response.statusCode)
            },
          )
          req.setTimeout(5000, () => req.destroy())
          req.on('error', () => resolve(0))
        })
      }
      assert.equal(discovery, 200, `Keycloak did not serve the axis realm\n${compose(['logs', 'keycloak']).stdout}`)

      // The guide: step 2, migrate. It runs before the first server start and does nothing then.
      const migrate = admin(['migrate'])
      assert.equal(migrate.status, 0, `migrate\n${migrate.stdout}${migrate.stderr}`)

      // Step 3, start the server and the worker, and wait until the server is ready.
      run(['up', '-d', 'server', 'worker'])
      const ready200 = await waitFor(() => server('localhost', '/health/ready'), 200, 120)
      assert.equal(ready200.status, 200, 'the server never answered /health/ready with 200')

      // Step 4, create two tenants. The client secret passes through from the shell.
      for (const [id, host] of Object.entries(hosts)) {
        const create = admin(
          ['tenant', 'create', id, '--host', host, '--oidc-authority', keycloakAuthority, '--oidc-client-id', 'axis'],
          { AXIS_OIDC_CLIENT_SECRET: clientSecret },
        )
        assert.equal(create.status, 0, `tenant create ${id}\n${create.stdout}${create.stderr}`)
        assert.match(create.stdout, new RegExp(`^${id} version ok$`, 'm'))
        assert.doesNotMatch(create.stdout + create.stderr, new RegExp(clientSecret))
      }

      // Step 5, activate the leave request sample for every active tenant.
      const activate = admin(['activate', '/apps/leave-requests'])
      assert.equal(activate.status, 0, `activate\n${activate.stdout}${activate.stderr}`)
      for (const id of Object.keys(hosts)) assert.match(activate.stdout, new RegExp(`^${id} activate ok$`, 'm'))

      // The hosts serve a tenant after their next refresh, here two seconds.
      for (const host of Object.values(hosts)) {
        const site = await waitFor(() => server(host, '/leave'), 200, 60)
        assert.equal(site.status, 200, `${host} did not serve /leave`)
      }

      // Both hosts are ready.
      assert.equal((await server('localhost', '/health/ready')).status, 200)
      assert.equal((await request(workerPort, 'localhost', '/health/ready')).status, 200)

      // Each tenant's database holds the active site.
      for (const id of Object.keys(hosts)) {
        const sites = compose(['exec', '-T', 'postgres', 'psql', '-U', 'postgres', '-d', `axis_${id}`, '-tAc', 'select path from axis.active_sites'])
        assert.equal(sites.status, 0, sites.stderr)
        assert.equal(sites.stdout.trim(), 'leave', `axis_${id} active sites`)
      }

      // Each tenant has its own sign-in: a redirect to the identity provider that returns to its own host.
      for (const host of Object.values(hosts)) {
        let signIn
        // The server fetches the OIDC metadata when it first needs it, so the first try can fail.
        for (let i = 0; i < 15; i++) {
          if (i > 0) await sleep(1000)
          signIn = await server(host, '/api/auth/sign-in?returnUrl=%2Fleave')
          if (signIn.status === 302) break
        }
        assert.equal(signIn.status, 302, `${host} sign-in: ${signIn.body}`)
        const location = new URL(signIn.headers.location)
        assert.equal(`${location.origin}${location.pathname}`, `${keycloakAuthority}/protocol/openid-connect/auth`)
        assert.equal(location.searchParams.get('client_id'), 'axis')
        assert.equal(location.searchParams.get('redirect_uri'), `http://${host}/api/auth/callback`)
      }

      // A host that no tenant holds answers 404.
      const unknown = await server('unknown.axis.test', '/leave')
      assert.equal(unknown.status, 404)
      assert.match(unknown.headers['content-type'], /^application\/problem\+json/)

      // Suspend the second tenant. It answers 503 and the first keeps serving.
      const suspend = admin(['tenant', 'suspend', 'two'])
      assert.equal(suspend.status, 0, `tenant suspend\n${suspend.stdout}${suspend.stderr}`)
      const suspended = await waitFor(() => server(hosts.two, '/leave'), 503, 60)
      assert.equal(suspended.status, 503, `${hosts.two} did not answer 503`)
      assert.equal((await server(hosts.one, '/leave')).status, 200)

      // Resume it, and it serves again.
      const resume = admin(['tenant', 'resume', 'two'])
      assert.equal(resume.status, 0, `tenant resume\n${resume.stdout}${resume.stderr}`)
      const resumed = await waitFor(() => server(hosts.two, '/leave'), 200, 60)
      assert.equal(resumed.status, 200, `${hosts.two} did not serve /leave after resume`)
    } catch (error) {
      dumpLogs()
      throw error
    }
  })
})
