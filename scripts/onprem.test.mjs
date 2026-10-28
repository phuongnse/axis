// Smoke test for the on-premises installation: installs deploy/compose/compose.yaml from the three
// production images exactly as docs/reference/installation.md says. It runs against a test PostgreSQL
// and Keycloak, creates two tenants and their realms in Keycloak, activates the leave request sample and checks each tenant's
// site and sign-in, suspend and resume, an unknown host, and a publish request that the installed
// publisher runs in each tenant. It then starts a second server replica and a TLS proxy, signs requester in
// through the proxy, and restarts the first replica. The servers listen on HTTPS only, with a certificate of the
// test authority for the name servers, and the proxy trusts that authority only. With Studio on, it saves a resource through one replica,
// reads it back through the other, and checks that a stale save answers 409. Requires Docker and openssl, and the
// sample modules in artifacts/modules, which scripts/smoke.sh installs. Run from scripts/smoke.sh.
//
// Everything the test adds lives in an override file in a temporary folder: PostgreSQL over TLS, Keycloak over
// HTTPS with a certificate authority made for the run, the modules folder and a short refresh interval.
// PostgreSQL serves a certificate of the same authority, every connection string sets SSL Mode=VerifyFull with
// that authority as Root Certificate, and the test checks in pg_stat_ssl that every Axis connection uses TLS.
// The production hosts refuse a connection string with a weaker mode.
// Keycloak imports a copy of the development realm that adds the https redirect addresses of the tenants.
// The production server refuses an identity provider over plain http, so Keycloak serves https and only
// the server and the admin tool in this test trust the certificate authority, through SSL_CERT_FILE.
// Keycloak starts with an admin client, which `tenant create` signs in as to create each tenant's realm. A new realm has
// no users, so the test points tenant one back at the development realm before the proxy signs anna in.
import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { createHash, randomBytes } from 'node:crypto'
import { chmodSync, cpSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import http from 'node:http'
import https from 'node:https'
import net from 'node:net'
import { tmpdir } from 'node:os'
import { join, relative, sep } from 'node:path'
import { after, before, describe, test } from 'node:test'
import { connect as tlsConnect } from 'node:tls'
import { fileURLToPath } from 'node:url'
import { buildImage } from './lib/docker-build.mjs'
import { issuePostgresCertificate, postgresTlsScript, serverCertificateMount, verifyFull } from './lib/postgres-tls.mjs'

const root = fileURLToPath(new URL('..', import.meta.url))
const composeFile = join(root, 'deploy/compose/compose.yaml')
const scenariosFile = join(root, 'deploy/compose/compose.scenarios.yaml')
const timeout = 30 * 60 * 1000
const targets = ['axis-server', 'axis-worker', 'axis-admin']
const keycloakAuthority = 'https://keycloak:8443/realms/axis'
const clientSecret = 'axis-development-secret'
const adminClientId = 'axis-admin'
const passThrough = ['AXIS_OIDC_CLIENT_SECRET', 'IdentityProvider__Url', 'IdentityProvider__AdminClientId']
const hosts = { one: 'one.axis.test', two: 'two.axis.test' }

const secretFiles = {
  server: ['ConnectionStrings__Platform', 'ConnectionStrings__PlatformSchema', 'Platform__SecretKey'],
  'server-2': ['ConnectionStrings__Platform', 'ConnectionStrings__PlatformSchema', 'Platform__SecretKey'],
  proxy: [],
  worker: ['ConnectionStrings__Platform', 'Platform__SecretKey'],
  admin: ['Admin__ConnectionString', 'ConnectionStrings__PlatformSchema', 'IdentityProvider__AdminClientSecret', 'Platform__SecretKey'],
  publisher: ['ConnectionStrings__PlatformSchema', 'Platform__SecretKey'],
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

// openToAll: makes every folder under dir, and dir itself, 0777 and every file 0666, so UID 1654 can write them.
const openToAll = (dir) => {
  chmodSync(dir, 0o777)
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name)
    if (entry.isDirectory()) openToAll(path)
    else chmodSync(path, 0o666)
  }
}

// render: the config of the example with every profile on and no variable set but the version. `extraFiles` are
// added after compose.yaml with -f.
const render = (extraFiles = []) => {
  const env = { ...process.env, AXIS_VERSION: 'x', ...Object.fromEntries(passThrough.map((name) => [name, ''])) }
  for (const name of Object.keys(env)) if (name.startsWith('AXIS_') && !['AXIS_VERSION', 'AXIS_OIDC_CLIENT_SECRET'].includes(name)) delete env[name]
  for (const name of Object.keys(env)) if (name.startsWith('Studio__')) delete env[name]
  const rendered = spawnSync(
    'docker',
    ['compose', '-f', composeFile, ...extraFiles.flatMap((file) => ['-f', file]), '--profile', 'admin', '--profile', 'proxy', '--profile', 'replicas', 'config', '--format', 'json'],
    { cwd: root, encoding: 'utf8', env },
  )
  assert.equal(rendered.status, 0, rendered.stderr)
  return JSON.parse(rendered.stdout)
}

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
    const config = render()
    assert.deepEqual(Object.keys(config.services).sort(), ['admin', 'proxy', 'publisher', 'server', 'server-2', 'worker'])

    for (const [name, service] of Object.entries(config.services)) {
      for (const [key, value] of Object.entries(service.environment ?? {})) {
        if (passThrough.includes(key)) {
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
    assert.deepEqual(config.services.proxy.profiles, ['proxy'])
    assert.deepEqual(config.services['server-2'].profiles, ['replicas'])
    assert.equal(config.services.worker.profiles, undefined)
    const publisher = config.services.publisher
    assert.equal(publisher.profiles, undefined)
    assert.ok((publisher.ports ?? []).length === 0, 'the publisher publishes a port')
    assert.deepEqual(publisher.command, ['publisher'])
    assert.match(publisher.image, /axis-admin:/)
    for (const secret of Object.values(config.secrets)) assert.ok(secret.file, 'a secret has no file')
  })

  test('the scenario secret and Studio__Tests come only with compose.scenarios.yaml, in the publisher and the servers', () => {
    const scenario = 'Scenarios__ServerConnectionString'
    for (const [name, service] of Object.entries(render().services)) {
      assert.equal(
        (service.secrets ?? []).some((secret) => secret.source === scenario),
        false,
        `${name} has the scenario secret without compose.scenarios.yaml`,
      )
      assert.equal(service.environment?.Studio__Tests, undefined, `${name} sets Studio__Tests without compose.scenarios.yaml`)
    }

    const { services, secrets } = render([scenariosFile])
    const publisherSecrets = (services.publisher.secrets ?? []).map((secret) => secret.source).sort()
    assert.deepEqual(publisherSecrets, [...secretFiles.publisher, scenario].sort())
    for (const secret of services.publisher.secrets) assert.equal(secret.target, `/run/secrets/${secret.source}`)
    assert.ok(secrets[scenario].file, 'the scenario secret has no file')
    for (const name of ['server', 'server-2']) {
      assert.equal(services[name].environment.Studio__Tests, 'true', `${name} Studio__Tests`)
      assert.equal((services[name].secrets ?? []).some((secret) => secret.source === scenario), false, `${name} has the scenario secret`)
    }
    for (const name of ['worker', 'admin', 'proxy']) {
      assert.equal((services[name].secrets ?? []).some((secret) => secret.source === scenario), false, `${name} has the scenario secret`)
      assert.equal(services[name].environment?.Studio__Tests, undefined, `${name} sets Studio__Tests`)
    }
    assert.deepEqual(services['server-2'].profiles, ['replicas'])
    assert.equal(services.publisher.environment?.Studio__Tests, undefined)
  })

  test('the servers publish only on 127.0.0.1, and the proxy takes its certificate from a mounted folder', () => {
    const { server, 'server-2': second, proxy } = render().services
    for (const [name, service] of Object.entries({ server, 'server-2': second })) {
      assert.ok(service.ports.length > 0, `${name} publishes no port`)
      for (const port of service.ports) assert.equal(port.host_ip, '127.0.0.1', `${name} publishes ${port.published} beyond 127.0.0.1`)
    }

    const certs = proxy.volumes.filter((volume) => volume.target === '/certs')
    assert.equal(certs.length, 1)
    assert.equal(certs[0].type, 'bind')
    assert.equal(certs[0].read_only, true)
    assert.equal(certs[0].source, join(root, 'deploy/compose/tls'))
    assert.deepEqual(proxy.secrets ?? [], [])
    assert.deepEqual(
      proxy.ports.map((port) => port.target),
      [443],
    )
    for (const [name, service] of Object.entries({ server, 'server-2': second })) {
      assert.deepEqual(
        service.ports.map((port) => port.target),
        [8443],
        `${name} publishes a port other than 8443`,
      )
    }

    // Both servers trust the forwarded headers of the proxy's fixed address and of no other.
    const address = proxy.networks.default.ipv4_address
    assert.match(address, /^\d+\.\d+\.\d+\.\d+$/)
    for (const [name, service] of Object.entries({ server, 'server-2': second })) {
      assert.equal(service.environment.ForwardedHeaders__KnownProxies__0, address, `${name} proxy address`)
      assert.equal(service.environment.ForwardedHeaders__KnownProxies__1, undefined)
      assert.equal(service.environment.ForwardedHeaders__KnownNetworks__0, undefined)
    }
  })
})

describe('the server certificate', () => {
  test('both servers listen on HTTPS only, and only they mount the certificate folder, read-only', () => {
    const { services } = render()
    for (const name of ['server', 'server-2']) {
      const environment = services[name].environment
      assert.match(environment.Kestrel__Endpoints__Https__Url, /^https:\/\/\+:8443$/, `${name} endpoint URL`)
      assert.equal(environment.Kestrel__Endpoints__Https__Certificate__Path, '/server-tls/tls.crt')
      assert.equal(environment.Kestrel__Endpoints__Https__Certificate__KeyPath, '/server-tls/tls.key')
      assert.equal(
        Object.keys(environment).some((key) => /^Kestrel__Endpoints__(?!Https__)/.test(key)),
        false,
        `${name} sets another Kestrel endpoint`,
      )
      const mounts = services[name].volumes.filter((volume) => volume.target === '/server-tls')
      assert.equal(mounts.length, 1, `${name} mounts /server-tls ${mounts.length} times`)
      assert.equal(mounts[0].type, 'bind')
      assert.equal(mounts[0].read_only, true, `${name} mounts /server-tls read-write`)
      assert.equal(mounts[0].source, join(root, 'deploy/compose/server-tls'))
    }
    for (const name of ['proxy', 'worker', 'publisher', 'admin']) {
      const service = services[name]
      assert.equal(
        (service.volumes ?? []).some((volume) => volume.target === '/server-tls' || volume.source === join(root, 'deploy/compose/server-tls')),
        false,
        `${name} mounts the server certificate folder`,
      )
      assert.equal(
        Object.keys(service.environment ?? {}).some((key) => key.startsWith('Kestrel__')),
        false,
        `${name} sets a Kestrel setting`,
      )
    }
  })
})

describe('the TLS proxy', () => {
  // The Caddyfile without its comments, one trimmed line each.
  const lines = readFileSync(join(root, 'deploy/compose/Caddyfile'), 'utf8')
    .split('\n')
    .map((line) => line.replace(/#.*$/, ''))
    .filter((line) => line.trim() !== '')

  test('the Caddyfile accepts only TLS 1.2 and 1.3, and on 1.2 only ECDHE ciphers with AEAD', () => {
    assert.ok(lines.some((line) => line.trim() === 'protocols tls1.2 tls1.3'), 'protocols is not tls1.2 tls1.3')
    const ciphers = lines.filter((line) => /^\s*ciphers\s/.test(line))
    assert.equal(ciphers.length, 1, 'the Caddyfile does not set ciphers exactly once')
    const names = ciphers[0].trim().split(/\s+/).slice(1)
    assert.ok(names.length > 0, 'the cipher list is empty')
    for (const name of names) {
      assert.match(name, /^TLS_ECDHE_(ECDSA|RSA)_WITH_(AES_128_GCM_SHA256|AES_256_GCM_SHA384|CHACHA20_POLY1305_SHA256)$/, `${name} is not an ECDHE AEAD cipher`)
    }
  })

  test('the proxy forwards to the servers on port 8443 over TLS, checks the name servers and trusts only the server authority', () => {
    assert.ok(lines.some((line) => line.trim() === 'dynamic a servers 8443 {'), 'the upstream is not servers on port 8443')
    assert.ok(lines.some((line) => line.trim() === 'transport http {'), 'the Caddyfile has no transport http block')
    for (const directive of ['tls', 'tls_server_name servers', 'tls_trust_pool file /certs/server-ca.crt']) {
      assert.equal(lines.filter((line) => line.trim() === directive).length, 1, `the Caddyfile does not hold "${directive}" exactly once`)
    }
    assert.ok(!lines.some((line) => /tls_insecure_skip_verify/.test(line)), 'the Caddyfile skips the verification of the server certificate')
    assert.ok(!lines.some((line) => line.includes('8080')), 'the Caddyfile names port 8080')
  })

  test(':443 is the only site address and nothing serves plain HTTP', () => {
    const sites = lines.filter((line) => /^\S/.test(line) && line.trimEnd().endsWith('{') && line.trim() !== '{').map((line) => line.trim())
    assert.deepEqual(sites, [':443 {'])
    assert.ok(!lines.some((line) => line.includes('http://')), 'the Caddyfile names an http:// address')
  })
})

describe('the Studio folder', () => {
  test('only the servers mount the Studio folder, read-write, and Studio is off without shell values', () => {
    const { services } = render()
    const sources = []
    for (const name of ['server', 'server-2']) {
      const mounts = services[name].volumes.filter((volume) => volume.target === '/studio')
      assert.equal(mounts.length, 1, `${name} mounts /studio ${mounts.length} times`)
      assert.equal(mounts[0].type, 'bind')
      assert.notEqual(mounts[0].read_only, true, `${name} mounts /studio read-only`)
      sources.push(mounts[0].source)
      for (const key of ['Studio__Folders__0', 'Studio__AuthorRole', 'Studio__PublisherRole']) {
        const value = services[name].environment[key]
        assert.ok(value === null || value === undefined || value === '', `${name} sets ${key}`)
      }
    }
    assert.equal(sources[0], sources[1])
    assert.equal(sources[0], join(root, 'deploy/compose/studio'))
    for (const name of ['worker', 'publisher', 'admin', 'proxy']) {
      assert.equal(
        (services[name].volumes ?? []).some((volume) => volume.target === '/studio'),
        false,
        `${name} mounts /studio`,
      )
    }
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
  let httpsPort
  let server2Port
  let overrideFile
  let caCertificate
  let adminClientSecret

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
    for (const service of ['server', 'server-2', 'proxy', 'worker', 'publisher']) {
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
  const request = (port, host, path, headers = {}, { method = 'GET', body } = {}) =>
    new Promise((resolve) => {
      const sent = body === undefined ? headers : { ...headers, 'content-length': String(Buffer.byteLength(body)) }
      const req = http.request({ host: '127.0.0.1', port, path, method, headers: { host, ...sent } }, (response) => {
        const chunks = []
        response.on('data', (chunk) => chunks.push(chunk))
        response.on('end', () =>
          resolve({ status: response.statusCode, headers: response.headers, body: Buffer.concat(chunks).toString() }),
        )
      })
      req.setTimeout(10000, () => req.destroy())
      req.on('error', () => resolve({ status: 0, headers: {}, body: '' }))
      req.end(body)
    })

  // tlsRequest: sends a request over HTTPS to a port on this machine. It sets the Host header and the name
  // the certificate is checked against, which is the host unless `servername` says otherwise, and trusts only
  // the test certificate authority.
  const tlsRequest = (port, host, path, { method = 'GET', headers = {}, body, servername = host.split(':')[0] } = {}) =>
    new Promise((resolve) => {
      const req = https.request(
        { host: '127.0.0.1', port, path, method, servername, ca: caCertificate, headers: { host, ...headers } },
        (response) => {
          const chunks = []
          response.on('data', (chunk) => chunks.push(chunk))
          response.on('end', () =>
            resolve({
              status: response.statusCode,
              headers: response.headers,
              setCookie: response.headers['set-cookie'] ?? [],
              body: Buffer.concat(chunks).toString(),
            }),
          )
        },
      )
      req.setTimeout(10000, () => req.destroy())
      req.on('error', () => resolve({ status: 0, headers: {}, setCookie: [], body: '' }))
      req.end(body)
    })

  // serverRequest: sends a request to a server replica's own port over HTTPS. The servers' certificate is for
  // the name servers, whatever the Host header says.
  const serverRequest = (port, host, path, headers = {}, { method = 'GET', body } = {}) =>
    tlsRequest(port, host, path, { method, headers, body, servername: 'servers' })

  const server = (host, path) => serverRequest(serverPort, host, path)

  // handshake: opens a TLS connection to the proxy with the given options and reports the protocol and cipher it
  // negotiated, or the error that refused it.
  const handshake = (options) =>
    new Promise((resolve) => {
      const socket = tlsConnect({ host: '127.0.0.1', port: httpsPort, servername: hosts.one, ca: caCertificate, ...options })
      socket.setTimeout(10000, () => {
        socket.destroy()
        resolve({ error: new Error('timeout') })
      })
      socket.on('secureConnect', () => {
        resolve({ protocol: socket.getProtocol(), cipher: socket.getCipher().name })
        socket.end()
      })
      socket.on('error', (error) => resolve({ error }))
    })

  // A cookie jar is a map from a cookie's name to its value.
  const remember = (jar, response) => {
    for (const line of response.setCookie) {
      const [pair] = line.split(';')
      const at = pair.indexOf('=')
      const name = pair.slice(0, at).trim()
      const value = pair.slice(at + 1).trim()
      if (value === '') jar.delete(name)
      else jar.set(name, value)
    }
  }

  const cookieHeader = (jar) => ({ cookie: [...jar].map(([name, value]) => `${name}=${value}`).join('; ') })

  const viaProxy = (path, jar, headers = {}) =>
    tlsRequest(httpsPort, hosts.one, path, { headers: { ...cookieHeader(jar), ...headers } })

  // keycloak: sends a request to Keycloak for a URL on its own origin, which only the containers can resolve.
  const keycloak = (url, jar, { method = 'GET', form } = {}) => {
    const target = new URL(url)
    assert.equal(target.origin, 'https://keycloak:8443', `unexpected Keycloak address ${url}`)
    const body = form ? new URLSearchParams(form).toString() : undefined
    const headers = { ...cookieHeader(jar) }
    if (body !== undefined) {
      headers['content-type'] = 'application/x-www-form-urlencoded'
      headers['content-length'] = String(Buffer.byteLength(body))
    }
    return tlsRequest(keycloakPort, target.host, `${target.pathname}${target.search}`, { method, headers, body })
  }

  // signInViaProxy: signs a Keycloak development user in through the proxy over HTTPS and returns the cookie jar
  // with the sign-in and callback responses. Every assertion about the redirect and the cookies stays here.
  const signInViaProxy = async (username, extraHeaders = {}) => {
    const jar = new Map()
    let signIn
    for (let i = 0; i < 15; i++) {
      if (i > 0) await sleep(1000)
      signIn = await viaProxy('/api/auth/sign-in?returnUrl=%2Fleave', jar, extraHeaders)
      if (signIn.status === 302) break
    }
    assert.equal(signIn.status, 302, `sign-in through the proxy: ${signIn.body}`)
    remember(jar, signIn)
    const authorize = new URL(signIn.headers.location)
    assert.equal(authorize.searchParams.get('redirect_uri'), `https://${hosts.one}/api/auth/callback`)

    // Sign the user in at Keycloak: the login page, then its form.
    const keycloakJar = new Map()
    const loginPage = await keycloak(authorize.href, keycloakJar)
    assert.equal(loginPage.status, 200, `the Keycloak login page: ${loginPage.body}`)
    remember(keycloakJar, loginPage)
    const form = loginPage.body.match(/<form[^>]*id="kc-form-login"[^>]*>/)
    assert.ok(form, 'the Keycloak login page has no kc-form-login form')
    const action = form[0].match(/action="([^"]+)"/)[1].replaceAll('&amp;', '&')
    const login = await keycloak(action, keycloakJar, { method: 'POST', form: { username, password: 'axis' } })
    assert.equal(login.status, 302, `the Keycloak login: ${login.body}`)
    const callback = new URL(login.headers.location)
    assert.equal(`${callback.origin}${callback.pathname}`, `https://${hosts.one}/api/auth/callback`)

    // The callback creates the session.
    const done = await viaProxy(`${callback.pathname}${callback.search}`, jar)
    assert.equal(done.status, 302, `the callback: ${done.body}`)
    remember(jar, done)
    return { jar, signIn, done }
  }

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
      for (const target of targets) await buildImage({ target, version, tag: `${target}:${version}` })

      // Step 2, the secret files. The folder and the files are readable by UID 1654.
      const secrets = join(folder, 'secrets')
      mkdirSync(secrets)
      chmodSync(secrets, 0o755)
      const passwords = { schema: password(), platform: password(), admin: password(), postgres: password() }
      const trust = verifyFull('/run/test-ca/ca.crt')
      const connection = (user, pass) =>
        `Host=postgres;Port=5432;Database=axis_platform;Username=${user};Password=${pass}${trust}`
      writeFile(join(secrets, 'ConnectionStrings__PlatformSchema'), connection('axis_platform_schema', passwords.schema))
      writeFile(join(secrets, 'ConnectionStrings__Platform'), connection('axis_platform', passwords.platform))
      writeFile(
        join(secrets, 'Admin__ConnectionString'),
        `Host=postgres;Port=5432;Database=postgres;Username=axis_admin;Password=${passwords.admin}${trust}`,
      )
      writeFile(join(secrets, 'Platform__SecretKey'), randomBytes(32).toString('base64'))
      adminClientSecret = password()
      writeFile(join(secrets, 'IdentityProvider__AdminClientSecret'), adminClientSecret)

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

      // Step 4, the certificates: a certificate authority, a certificate of Keycloak for the name keycloak,
      // a certificate of PostgreSQL for the name postgres and a certificate of the proxy for the two tenant hosts.
      const certs = join(folder, 'certs')
      mkdirSync(certs)
      chmodSync(certs, 0o755)
      const at = (name) => join('certs', name)
      writeFile(join(folder, 'leaf.ext'), 'subjectAltName=DNS:keycloak\nbasicConstraints=CA:FALSE\n')
      openssl('req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '2', '-subj', '/CN=Axis onprem test CA', '-keyout', at('ca.key'), '-out', at('ca.crt'), '-addext', 'basicConstraints=critical,CA:TRUE', '-addext', 'keyUsage=critical,keyCertSign,cRLSign')
      openssl('req', '-newkey', 'rsa:2048', '-nodes', '-subj', '/CN=keycloak', '-keyout', at('tls.key'), '-out', at('tls.csr'))
      openssl('x509', '-req', '-in', at('tls.csr'), '-CA', at('ca.crt'), '-CAkey', at('ca.key'), '-CAcreateserial', '-days', '2', '-extfile', 'leaf.ext', '-out', at('tls.crt'))
      for (const name of readdirSync(certs)) chmodSync(join(certs, name), 0o644)
      caCertificate = readFileSync(join(certs, 'ca.crt'))
      const { serverDir } = issuePostgresCertificate({
        folder,
        openssl,
        name: 'postgres',
        ca: { certificate: join(certs, 'ca.crt'), key: join(certs, 'ca.key') },
      })

      // The proxy's folder holds tls.crt and tls.key, which is what AXIS_TLS_DIR names.
      const tls = join(folder, 'tls')
      mkdirSync(tls)
      chmodSync(tls, 0o755)
      writeFile(join(folder, 'proxy.ext'), `subjectAltName=DNS:${hosts.one},DNS:${hosts.two}\nbasicConstraints=CA:FALSE\n`)
      openssl('req', '-newkey', 'rsa:2048', '-nodes', '-subj', `/CN=${hosts.one}`, '-keyout', join('tls', 'tls.key'), '-out', join('certs', 'proxy.csr'))
      openssl('x509', '-req', '-in', join('certs', 'proxy.csr'), '-CA', at('ca.crt'), '-CAkey', at('ca.key'), '-CAcreateserial', '-days', '2', '-extfile', 'proxy.ext', '-out', join('tls', 'tls.crt'))
      for (const name of readdirSync(tls)) chmodSync(join(tls, name), 0o644)

      // The servers' folder holds tls.crt and tls.key for the name servers, which is what AXIS_SERVER_TLS_DIR names.
      // The proxy's folder also holds the authority certificate that is the only one it trusts for the servers.
      const serverTls = join(folder, 'server-tls')
      mkdirSync(serverTls)
      chmodSync(serverTls, 0o755)
      writeFile(join(folder, 'server.ext'), 'subjectAltName=DNS:servers\nbasicConstraints=CA:FALSE\n')
      openssl('req', '-newkey', 'rsa:2048', '-nodes', '-subj', '/CN=servers', '-keyout', join('server-tls', 'tls.key'), '-out', join('certs', 'server.csr'))
      openssl('x509', '-req', '-in', join('certs', 'server.csr'), '-CA', at('ca.crt'), '-CAkey', at('ca.key'), '-CAcreateserial', '-days', '2', '-extfile', 'server.ext', '-out', join('server-tls', 'tls.crt'))
      for (const name of readdirSync(serverTls)) chmodSync(join(serverTls, name), 0o644)
      cpSync(join(certs, 'ca.crt'), join(tls, 'server-ca.crt'))
      chmodSync(join(tls, 'server-ca.crt'), 0o644)

      // A copy of the development realm that also accepts the https addresses of the two tenants.
      const realm = join(folder, 'realm')
      mkdirSync(realm)
      chmodSync(realm, 0o755)
      const realmJson = JSON.parse(readFileSync(join(root, 'samples/keycloak/axis-realm.json'), 'utf8'))
      const client = realmJson.clients.find((candidate) => candidate.clientId === 'axis')
      assert.ok(client, 'the development realm has no axis client')
      const origins = Object.values(hosts).map((host) => `https://${host}/*`)
      client.redirectUris.push(...origins)
      client.attributes['post.logout.redirect.uris'] += `##${origins.join('##')}`
      writeFile(join(realm, 'axis-realm.json'), JSON.stringify(realmJson))

      // A copy of the leave request sample that the servers may write: UID 1654 is not the owner, so the
      // folders are 0777 and the files 0666.
      const studio = join(folder, 'studio')
      cpSync(join(root, 'samples/apps/leave-requests'), join(studio, 'leave-requests'), { recursive: true })
      openToAll(studio)

      serverPort = await freePort()
      workerPort = await freePort()
      httpsPort = await freePort()
      server2Port = await freePort()

      // The override file. JSON is valid YAML, so the test writes it as JSON.
      const refresh = { Tenancy__RefreshInterval: '00:00:02' }
      const serverOverride = {
        environment: { ...refresh, SSL_CERT_FILE: '/run/test-ca/ca.crt' },
        volumes: [`${certs}:/run/test-ca:ro`],
      }
      // Every host reads the authority certificate that its connection strings name.
      const trustOverride = { volumes: [`${certs}:/run/test-ca:ro`] }
      overrideFile = join(folder, 'override.json')
      writeFile(
        overrideFile,
        JSON.stringify({
          services: {
            postgres: {
              image: 'postgres:18-alpine',
              environment: { POSTGRES_USER: 'postgres', POSTGRES_PASSWORD: passwords.postgres },
              entrypoint: ['sh', '-c', postgresTlsScript],
              volumes: [
                `${initSql}:/docker-entrypoint-initdb.d/init-roles.sql:ro`,
                `${serverDir}:${serverCertificateMount}:ro`,
              ],
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
              environment: { KC_BOOTSTRAP_ADMIN_CLIENT_ID: adminClientId, KC_BOOTSTRAP_ADMIN_CLIENT_SECRET: adminClientSecret },
              volumes: [`${realm}:/opt/keycloak/data/import:ro`, `${certs}:/certs:ro`],
              ports: ['127.0.0.1::8443'],
            },
            server: serverOverride,
            'server-2': serverOverride,
            worker: { environment: refresh, ...trustOverride },
            publisher: { environment: refresh, ...trustOverride },
            admin: { environment: { SSL_CERT_FILE: '/run/test-ca/ca.crt' }, ...trustOverride },
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
        AXIS_SERVER_2_PORT: String(server2Port),
        AXIS_HTTPS_PORT: String(httpsPort),
        AXIS_TLS_DIR: tls,
        AXIS_SERVER_TLS_DIR: serverTls,
        AXIS_WORKER_HEALTH_PORT: String(workerPort),
        AXIS_STUDIO_DIR: studio,
        Studio__Folders__0: '/studio/leave-requests',
        Studio__AuthorRole: 'author',
        IdentityProvider__Url: 'https://keycloak:8443/',
        IdentityProvider__AdminClientId: adminClientId,
      }
      delete env.AXIS_OIDC_CLIENT_SECRET
      assert.ok(statSync(env.AXIS_MODULES_DIR).isDirectory(), 'run scripts/smoke.sh, which installs the sample modules: artifacts/modules is missing')
    },
    { timeout },
  )

  after(() => {
    compose(['down', '-v', '--remove-orphans'], { stdio: 'ignore', env: { ...env, COMPOSE_PROFILES: 'admin,proxy,replicas' } })
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

      // The server listens on HTTPS only: a plain HTTP request to its port gets no HTTP response.
      const plain = await request(serverPort, 'localhost', '/health/ready')
      assert.equal(plain.status, 0, `the server answered plain HTTP on its port with ${plain.status}`)

      // The publisher exits until the server has created the platform tables, so it starts now.
      run(['up', '-d', 'publisher'])

      // Step 4, create two tenants. Each gets a realm of its own in Keycloak. The identity provider address and
      // admin client id pass through from the shell and the admin client secret is a secret file.
      for (const [id, host] of Object.entries(hosts)) {
        const create = admin(['tenant', 'create', id, '--host', host])
        assert.equal(create.status, 0, `tenant create ${id}\n${create.stdout}${create.stderr}`)
        assert.match(create.stdout, new RegExp(`^${id} realm ok$`, 'm'))
        assert.match(create.stdout, new RegExp(`^${id} version ok$`, 'm'))
        assert.doesNotMatch(create.stdout + create.stderr, new RegExp(adminClientSecret))
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

      // Every connection that an Axis host holds uses TLS. The check itself connects as postgres over the Unix socket,
      // so it leaves out that user.
      const connections = compose(
        [
          'exec', '-T', 'postgres', 'psql', '-U', 'postgres', '-tA', '-F', ',', '-c',
          `select count(*) filter (where s.ssl), count(*) filter (where not s.ssl)
             from pg_stat_activity a join pg_stat_ssl s using (pid)
            where a.backend_type = 'client backend' and a.usename <> 'postgres'`,
        ],
      )
      assert.equal(connections.status, 0, connections.stderr)
      const [withTls, withoutTls] = connections.stdout.trim().split(',').map(Number)
      assert.ok(withTls > 0, `no Axis connection uses TLS\n${connections.stdout}`)
      assert.equal(withoutTls, 0, `${withoutTls} Axis connections do not use TLS`)

      // Each tenant's database holds the active site.
      for (const id of Object.keys(hosts)) {
        const sites = compose(['exec', '-T', 'postgres', 'psql', '-U', 'postgres', '-d', `axis_${id}`, '-tAc', 'select path from axis.active_sites'])
        assert.equal(sites.status, 0, sites.stderr)
        assert.equal(sites.stdout.trim(), 'leave', `axis_${id} active sites`)
      }

      // Each tenant has its own sign-in: a redirect to the authorization endpoint of its own realm that returns to its own host.
      for (const [id, host] of Object.entries(hosts)) {
        let signIn
        // The server fetches the OIDC metadata when it first needs it, so the first try can fail.
        for (let i = 0; i < 15; i++) {
          if (i > 0) await sleep(1000)
          signIn = await server(host, '/api/auth/sign-in?returnUrl=%2Fleave')
          if (signIn.status === 302) break
        }
        assert.equal(signIn.status, 302, `${host} sign-in: ${signIn.body}`)
        const location = new URL(signIn.headers.location)
        assert.equal(`${location.origin}${location.pathname}`, `https://keycloak:8443/realms/${id}/protocol/openid-connect/auth`)
        assert.equal(location.searchParams.get('client_id'), 'axis')
        assert.equal(location.searchParams.get('redirect_uri'), `https://${host}/api/auth/callback`)
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

      // Queue one publish request of the leave request sample in each tenant. The installed publisher runs both.
      const application = join(root, 'samples/apps/leave-requests')
      const applicationId = JSON.parse(readFileSync(join(application, 'application.json'), 'utf8')).id
      const snapshot = JSON.stringify(
        files(application)
          .filter((file) => file.endsWith('.json'))
          .map((file) => ({ file: relative(application, file).split(sep).join('/'), bytes: readFileSync(file) }))
          .sort((a, b) => (a.file < b.file ? -1 : a.file > b.file ? 1 : 0))
          .map(({ file, bytes }) => ({
            file,
            version: createHash('sha256').update(bytes).digest('hex'),
            content: bytes.toString('utf8'),
          })),
      )
      assert.ok(!snapshot.includes('$snap$'))
      const psql = (id, sql) =>
        compose(['exec', '-T', 'postgres', 'psql', '-U', 'postgres', '-d', `axis_${id}`, '-v', 'ON_ERROR_STOP=1', '-tA'], {
          input: sql,
        })
      const queued = {}
      for (const id of Object.keys(hosts)) {
        const insert = psql(
          id,
          `INSERT INTO axis.publish_requests (id, application_id, kind, status, snapshot, requested_by, requested_at)
VALUES (gen_random_uuid(), '${applicationId}', 'publish', 'queued', $snap$${snapshot}$snap$::jsonb, 'onprem-test', clock_timestamp())
RETURNING id;`,
        )
        assert.equal(insert.status, 0, `queue a publish request for ${id}\n${insert.stdout}${insert.stderr}`)
        queued[id] = insert.stdout.trim().split('\n')[0]
        assert.match(queued[id], /^[0-9a-f-]{36}$/)
      }
      for (const [id, request] of Object.entries(queued)) {
        let status = ''
        for (let i = 0; i < 60 && status !== 'succeeded'; i++) {
          if (i > 0) await sleep(1000)
          const result = psql(id, `select status from axis.publish_requests where id = '${request}'`)
          assert.equal(result.status, 0, result.stderr)
          status = result.stdout.trim()
          if (status === 'failed') break
        }
        assert.equal(status, 'succeeded', `the publish request of ${id} did not succeed`)
      }
      const publisherLog = compose(['logs', '--no-color', '--no-log-prefix', 'publisher']).stdout
      for (const id of Object.keys(hosts)) assert.match(publisherLog, new RegExp(`^${id} claim ok$`, 'm'))
      assert.match(run(['ps', '--status', 'running', '--services']), /^publisher$/m)

      // A new realm has no users, so tenant one goes back to the development realm, where anna and hoa can sign in.
      const point = admin(
        ['tenant', 'oidc', 'set', 'one', '--authority', keycloakAuthority, '--client-id', 'axis', '--client-secret'],
        { AXIS_OIDC_CLIENT_SECRET: clientSecret },
      )
      assert.equal(point.status, 0, `tenant oidc set one\n${point.stdout}${point.stderr}`)
      assert.doesNotMatch(point.stdout + point.stderr, new RegExp(clientSecret))
      for (let i = 0; i < 30; i++) {
        if (i > 0) await sleep(1000)
        const pointed = await server(hosts.one, '/api/auth/sign-in?returnUrl=%2Fleave')
        if (pointed.status === 302 && pointed.headers.location.startsWith(`${keycloakAuthority}/`)) break
      }

      // Step 8, a second replica and the TLS proxy. The second replica starts once the first is ready.
      run(['up', '-d', 'server-2'])
      const ready2 = await waitFor(() => serverRequest(server2Port, 'localhost', '/health/ready'), 200, 120)
      assert.equal(ready2.status, 200, 'the second server never answered /health/ready with 200')
      const site2 = await waitFor(() => serverRequest(server2Port, hosts.one, '/leave'), 200, 60)
      assert.equal(site2.status, 200, 'the second server did not serve /leave')

      run(['up', '-d', 'proxy'])
      const proxied = await waitFor(() => viaProxy('/leave', new Map()), 200, 60)
      assert.equal(proxied.status, 200, 'the proxy did not serve /leave')

      // A default handshake negotiates TLS 1.3. TLS 1.2 works with an ECDHE AEAD cipher, and is refused when the
      // client offers only CBC ciphers.
      assert.equal((await handshake({})).protocol, 'TLSv1.3')
      const tls12 = await handshake({ maxVersion: 'TLSv1.2' })
      assert.equal(tls12.protocol, 'TLSv1.2', `TLS 1.2 handshake: ${tls12.error}`)
      assert.match(tls12.cipher, /^ECDHE-.*(GCM|CHACHA20)/)
      const cbc = await handshake({
        minVersion: 'TLSv1.2',
        maxVersion: 'TLSv1.2',
        ciphers: 'ECDHE-RSA-AES128-SHA:ECDHE-RSA-AES256-SHA:ECDHE-RSA-AES128-SHA256',
      })
      assert.ok(cbc.error, 'the proxy accepted a CBC cipher on TLS 1.2')
      assert.equal(cbc.protocol, undefined)

      // The sign-in redirect names the tenant host over https, even when the client sends its own
      // forwarded headers, which the proxy replaces.
      const { jar: proxyJar, done } = await signInViaProxy('requester', {
        'x-forwarded-proto': 'http',
        'x-forwarded-host': hosts.two,
        'x-forwarded-for': '203.0.113.9',
      })

      // The stack runs in Production, so every session cookie the callback sets is a __Host- cookie: Secure,
      // Path=/ and no domain.
      const sessionCookies = done.setCookie.filter((line) => line.startsWith('__Host-Axis.Session='))
      assert.ok(sessionCookies.length > 0, 'the callback set no __Host-Axis.Session cookie')
      for (const line of sessionCookies) {
        assert.match(line, /;\s*secure/i, `not Secure: ${line}`)
        assert.match(line, /;\s*path=\/(;|$)/i, `not Path=/: ${line}`)
        assert.doesNotMatch(line, /;\s*domain=/i, `has a domain: ${line}`)
      }
      const me = await viaProxy('/api/me', proxyJar)
      assert.equal(me.status, 200, `GET /api/me through the proxy: ${me.body}`)

      // Restart the first replica. The same cookie works through the proxy and on each replica's own port.
      run(['restart', 'server'])
      const restarted = await waitFor(() => server('localhost', '/health/ready'), 200, 120)
      assert.equal(restarted.status, 200, 'the server never answered /health/ready after the restart')
      const back = await waitFor(() => server(hosts.one, '/leave'), 200, 60)
      assert.equal(back.status, 200, 'the server did not serve /leave after the restart')
      const meAfter = await waitFor(() => viaProxy('/api/me', proxyJar), 200, 30)
      assert.equal(meAfter.status, 200, `GET /api/me through the proxy after the restart: ${meAfter.body}`)
      for (const port of [serverPort, server2Port]) {
        const direct = await serverRequest(port, hosts.one, '/api/me', cookieHeader(proxyJar))
        assert.equal(direct.status, 200, `GET /api/me on port ${port} after the restart: ${direct.body}`)
      }

      // Step 9, Studio on a shared folder. hoa saves through server, reads through server-2 and a stale save
      // through server-2 answers 409.
      const { jar: hoaJar } = await signInViaProxy('hoa')
      const resource = '/api/studio/apps/LeaveRequests/resources/bb5c622e-a7f8-41d4-8a6f-15fe463455b8'
      const patch = (port, version, value) =>
        serverRequest(
          port,
          hosts.one,
          resource,
          {
            ...cookieHeader(hoaJar),
            'x-axis-csrf': '1',
            'if-match': version,
            'content-type': 'application/json-patch+json',
          },
          { method: 'PATCH', body: JSON.stringify([{ op: 'replace', path: '/texts/site.title', value }]) },
        )
      const original = await serverRequest(serverPort, hosts.one, resource, cookieHeader(hoaJar))
      assert.equal(original.status, 200, `GET the resource through server: ${original.body}`)
      const etag0 = original.headers.etag
      assert.ok(etag0, 'the resource has no ETag')

      const saved = await patch(serverPort, etag0, 'Leave (on premises)')
      assert.equal(saved.status, 200, `PATCH through server: ${saved.body}`)
      const etag1 = saved.headers.etag
      assert.ok(etag1 && etag1 !== etag0, 'the save did not change the ETag')

      const read = await serverRequest(server2Port, hosts.one, resource, cookieHeader(hoaJar))
      assert.equal(read.status, 200, `GET through server-2: ${read.body}`)
      assert.equal(read.headers.etag, etag1)
      assert.equal(JSON.parse(read.body).content.texts['site.title'], 'Leave (on premises)')

      const stale = await patch(server2Port, etag0, 'Leave (stale)')
      assert.equal(stale.status, 409, `a stale PATCH through server-2: ${stale.body}`)
    } catch (error) {
      dumpLogs()
      throw error
    }
  })
})
