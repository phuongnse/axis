// The local stack of the authoring eval: PostgreSQL and Keycloak in Docker, and the built server, publisher and agent
// host on free ports, with tenants read from the tenant directory. `createTenant` gives a case its own tenant with a
// signed-in author and the case's starting application, see docs/reference/model-bindings.md#the-authoring-eval and
// docs/reference/authoring-requests.md#the-agent-host. The agent host uses a named model binding, or replays an
// answers file through the scripted adapter, which no model provider sees. Importing this file starts nothing.
//
// The stack carries an authoring request from send to plan through the agent host and the MCP server, as Studio does.
// The server also serves https, with a certificate for `*.eval.localhost` from an authority made for the run. The agent
// host trusts that authority and reaches each tenant's MCP server at `https://<host>/mcp`, with no port, through a
// local proxy that opens its tunnels to the server's https port, so the address and the token's audience are the ones
// the realm writes. The stack's own client keeps plain http, so `createTenant` and its sign-in work on the http address.
//
// The hosts run as one self-hosted installation, not as a cell, so every tenant shares one Studio folder. Each case's
// application gets a name of its own, see applicationName. The server and the agent host run in the Testing
// environment, which the scripted adapter needs, and the admin tool and the publisher in Development, as
// tests/e2e/admin-tool.ts runs them.
import { spawn, spawnSync } from 'node:child_process'
import { randomBytes } from 'node:crypto'
import { closeSync, mkdirSync, mkdtempSync, openSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import http from 'node:http'
import https from 'node:https'
import net from 'node:net'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { listedImage } from './docker-images.mjs'
import { addAccount, cookieHeader, newOtpSecret, otpCredential, preparePlatformClient, rolesMapper, signIn } from './keycloak-sign-in.mjs'
import { issuePostgresCertificate } from './postgres-tls.mjs'

const root = fileURLToPath(new URL('../..', import.meta.url))
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))
const secret = () => randomBytes(18).toString('hex')
const dll = (project) => join(root, 'src', project, 'bin/Release/net10.0', `${project}.dll`)

/**
 * The agent host's `Ai` settings. With `answersFile`, every binding replays the answers of that file, as the scripted
 * adapter does. With `binding`, the host's default binding is that name and the `Ai__Bindings__<binding>__*` variables
 * of `environment` come with it, and no other binding does. Exactly one of the two is given.
 */
export function agentEnvironment({ binding, answersFile, environment = process.env } = {}) {
  if ((binding === undefined) === (answersFile === undefined)) {
    throw new Error('Give exactly one of binding and answersFile.')
  }
  if (answersFile !== undefined) {
    return {
      Ai__Bindings__scripted__Kind: 'anthropic',
      Ai__Bindings__scripted__Model: 'scripted',
      Ai__DefaultBinding: 'scripted',
      Ai__ScriptedAnswersFile: answersFile,
    }
  }
  const prefix = `Ai__Bindings__${binding}__`
  return {
    Ai__DefaultBinding: binding,
    ...Object.fromEntries(Object.entries(environment).filter(([name]) => name.startsWith(prefix))),
  }
}

/** The id of the installed template that a case starts from. A case that starts from a sample has none, because a sample is not a template. */
export function startingTemplate(start) {
  if (start.from === 'application' || start.from === 'template') return start.name
  throw new Error(`A case cannot start from a ${start.from} yet: only an application or a template that is installed.`)
}

const pascal = (text) =>
  text
    .split(/[^A-Za-z0-9]+/)
    .filter(Boolean)
    .map((word) => word[0].toUpperCase() + word.slice(1))
    .join('')

/** The name of the case's application in the Studio folder that all tenants share, such as `ApprovalsEvalSmoke` for the start `approvals` and the tenant `eval-smoke`. */
export const applicationName = (start, tenantId) => `${pascal(start.name)}${pascal(tenantId)}`

/** The domain of the tenants' hosts. The stack's one certificate is made for `*.eval.localhost`, because OpenSSL refuses a wildcard with one label after it, such as `*.localhost`. */
export const tenantDomain = 'eval.localhost'

/** The host of the tenant `id`, such as `eval-smoke.eval.localhost`. */
export const tenantHost = (id) => `${id}.${tenantDomain}`

/** Where the proxy opens a tunnel for `authority` (`host:port` of a CONNECT): a tenant host on 443 goes to the server's https port, anything else to itself. */
export function tunnelTarget(authority, httpsPort) {
  const split = authority.lastIndexOf(':')
  const host = authority.slice(0, split)
  const port = Number(authority.slice(split + 1))
  if (host.endsWith(`.${tenantDomain}`) && port === 443) return { host: '127.0.0.1', port: httpsPort }
  return { host, port }
}

/** Runs `command` in the repository and throws with its output when it fails. */
function run(command, args) {
  const result = spawnSync(command, args, { cwd: root, encoding: 'utf8' })
  if (result.status !== 0) throw new Error(`${command} ${args.join(' ')}\n${result.stdout}${result.stderr}`)
  return result.stdout
}

/** Builds what the stack runs: the three hosts in Release, the Keycloak theme, the installed modules and the installed templates. Every build is incremental. */
export function buildStack() {
  for (const project of ['Axis.Server', 'Axis.Admin', 'Axis.Agent']) {
    run('dotnet', ['build', `src/${project}/${project}.csproj`, '-c', 'Release', '--nologo', '-v', 'quiet'])
  }
  run('npm', ['run', 'build:keycloak-theme', '--prefix', 'web'])
  run('bash', ['scripts/lib/install-modules.sh'])
  run('bash', ['scripts/lib/install-sample-templates.sh'])
}

const freePort = () =>
  new Promise((resolve, reject) => {
    const server = net.createServer()
    server.once('error', reject)
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address()
      server.close(() => resolve(port))
    })
  })

/**
 * Sends one http request, or one https request when the URL starts with `https:`, where `tls` adds TLS options such as `ca`
 * and `servername`. It resolves with `{ status, headers, setCookie, body }`. A failed connection gives status 0 and the error as the body.
 */
function send(url, { method = 'GET', headers = {}, body, tls } = {}) {
  return new Promise((resolve) => {
    const transport = url.startsWith('https:') ? https : http
    const request = transport.request(url, { method, headers, ...tls }, (response) => {
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
    })
    request.setTimeout(30000, () => request.destroy(new Error('timeout')))
    request.on('error', (error) => resolve({ status: 0, headers: {}, setCookie: [], body: String(error) }))
    request.end(body)
  })
}

/** Polls until `check` gives a value that is not undefined, for `seconds`, and throws `message` when it never does. */
async function waitUntil(check, seconds, message) {
  for (let i = 0; i < seconds; i++) {
    if (i > 0) await sleep(1000)
    const value = await check()
    if (value !== undefined) return value
  }
  throw new Error(message)
}

// The names of the settings that the hosts read, which the caller's environment must not leak into the stack.
const settingPrefixes = ['Ai__', 'Studio__', 'Tenants__', 'Tenancy__', 'ConnectionStrings__', 'Platform__', 'Admin__', 'IdentityProvider__', 'Scenarios__', 'Health__', 'Modules__', 'Templates__', 'Extensions__', 'Cell__', 'ASPNETCORE_', 'SSL_CERT_', 'HTTPS_PROXY', 'https_proxy', 'ALL_PROXY', 'all_proxy', 'NO_PROXY', 'no_proxy']

function hostEnvironment(settings) {
  const base = Object.fromEntries(
    Object.entries(process.env).filter(([name]) => name !== 'DOTNET_ENVIRONMENT' && !settingPrefixes.some((prefix) => name.startsWith(prefix))),
  )
  return { ...base, ...settings }
}

/** The folder of the system's trusted certificates, which `SSL_CERT_DIR` replaces for OpenSSL, so the agent host keeps the public roots. */
function systemCertDir() {
  if (process.env.SSL_CERT_DIR) return process.env.SSL_CERT_DIR
  const directory = /OPENSSLDIR: "(.*)"/.exec(run('openssl', ['version', '-d']))?.[1] ?? '/usr/lib/ssl'
  return join(directory, 'certs')
}

/**
 * Starts the stack and returns `{ serverPort, httpsPort, keycloakUrl, agentHealthUrl, request, secureRequest, logs, stop }`. `request(host, path, { method, headers, body })`
 * sends an http request to the server with the Host header `host:serverPort`, and answers `{ status, headers, setCookie, body }`.
 * `secureRequest(host, path, { method, headers, body })` does the same over https on `httpsPort`, with the Host header `host` and the run's authority as the only root.
 * `logs()` is the output of the hosts, and `stop()` ends everything and removes the folder, and may be called twice.
 * Give `binding` or `answersFile` for the agent host, see agentEnvironment. The tests of the stack take what createTenant needs from the other fields.
 */
export async function startStack({ binding, answersFile } = {}) {
  const agent = agentEnvironment({ binding, answersFile })
  buildStack()

  const folder = mkdtempSync(join(tmpdir(), 'axis-ai-eval-'))
  const logFiles = []
  const children = []
  const containers = []
  let stopped = false

  const proxySockets = new Set()
  let proxy

  const stop = async () => {
    if (stopped) return
    stopped = true
    proxy?.close()
    for (const socket of proxySockets) socket.destroy()
    await Promise.all(
      children.map(async ({ child, exited }) => {
        if (child.exitCode !== null || child.signalCode !== null) return
        child.kill('SIGTERM')
        // A host that ignores SIGTERM must not hang the run.
        const killer = setTimeout(() => child.kill('SIGKILL'), 15_000)
        await exited
        clearTimeout(killer)
      }),
    )
    for (const container of containers) spawnSync('docker', ['rm', '-f', container], { stdio: 'ignore' })
    rmSync(folder, { recursive: true, force: true })
  }

  const logs = () =>
    logFiles
      .map(({ name, file }) => {
        let text = ''
        try {
          text = readFileSync(file, 'utf8')
        } catch {
          // The folder is gone after stop.
        }
        return `--- ${name} ---\n${text}`
      })
      .join('\n')

  // Starts a host from its built dll and writes its output to a log file in the folder.
  const start = (name, command, args, { cwd, settings }) => {
    const file = join(folder, `${name}.log`)
    logFiles.push({ name, file })
    const output = openSync(file, 'a')
    const child = spawn(command, args, { cwd, env: hostEnvironment(settings), stdio: ['ignore', output, output] })
    closeSync(output)
    const entry = { child, exited: new Promise((resolve) => child.once('close', resolve)) }
    children.push(entry)
    return entry
  }

  const exitedBefore = (entry, name) => {
    if (entry.child.exitCode !== null || entry.child.signalCode !== null) {
      throw new Error(`${name} exited with ${entry.child.exitCode ?? entry.child.signalCode} before it was ready.`)
    }
  }

  try {
    const passwords = { postgres: secret(), schema: secret(), platform: secret(), admin: secret(), scenario: secret() }
    const adminSecret = secret()
    const secretKey = randomBytes(32).toString('base64')
    const suffix = `${process.pid}-${randomBytes(3).toString('hex')}`

    // 1. PostgreSQL. The init SQL makes the roles of the installation guide and the platform database.
    const initSql = join(folder, 'init-roles.sql')
    writeFileSync(
      initSql,
      [
        `CREATE ROLE axis_platform_schema LOGIN PASSWORD '${passwords.schema}';`,
        `CREATE ROLE axis_platform LOGIN PASSWORD '${passwords.platform}';`,
        `CREATE ROLE axis_admin LOGIN PASSWORD '${passwords.admin}' CREATEDB CREATEROLE;`,
        `CREATE ROLE axis_scenario LOGIN PASSWORD '${passwords.scenario}' CREATEDB;`,
        'CREATE DATABASE axis_platform OWNER axis_platform_schema;',
        '',
      ].join('\n'),
      { mode: 0o644 },
    )
    const postgres = `axis-ai-eval-postgres-${suffix}`
    containers.push(postgres)
    run('docker', [
      'run', '-d', '--name', postgres,
      '-e', 'POSTGRES_USER=postgres', '-e', `POSTGRES_PASSWORD=${passwords.postgres}`,
      '-p', '127.0.0.1::5432',
      '-v', `${initSql}:/docker-entrypoint-initdb.d/init-roles.sql:ro`,
      listedImage(root, 'postgres'),
    ])
    const postgresPort = run('docker', ['port', postgres, '5432/tcp']).split('\n')[0].split(':').pop().trim()
    const connection = (database, user, password) => `Host=127.0.0.1;Port=${postgresPort};Database=${database};Username=${user};Password=${password}`

    // 2. Keycloak, which starts while PostgreSQL gets ready. It starts with an admin client, which `tenant create` signs in as.
    const keycloakPort = await freePort()
    const keycloakUrl = `http://127.0.0.1:${keycloakPort}`
    const keycloak = `axis-ai-eval-keycloak-${suffix}`
    containers.push(keycloak)
    run('docker', [
      'run', '-d', '--name', keycloak,
      '-e', 'KC_BOOTSTRAP_ADMIN_USERNAME=admin', '-e', `KC_BOOTSTRAP_ADMIN_PASSWORD=${secret()}`,
      '-e', 'KC_BOOTSTRAP_ADMIN_CLIENT_ID=axis-admin', '-e', `KC_BOOTSTRAP_ADMIN_CLIENT_SECRET=${adminSecret}`,
      '-v', `${join(root, 'deploy/keycloak/password-blacklists')}:/opt/keycloak/data/password-blacklists:ro`,
      '-v', `${join(root, 'artifacts/keycloak-theme')}:/opt/keycloak/themes:ro`,
      '-p', `127.0.0.1:${keycloakPort}:${keycloakPort}`,
      listedImage(root, 'quay.io/keycloak/keycloak'), 'start-dev', `--http-port=${keycloakPort}`,
    ])

    // Check over TCP: the image's initialization server listens only on the Unix socket, so the check
    // succeeds only after the init script has run.
    await waitUntil(
      () => (spawnSync('docker', ['exec', postgres, 'pg_isready', '-h', '127.0.0.1', '-U', 'postgres']).status === 0 ? true : undefined),
      60,
      `PostgreSQL was not ready after 60 seconds\n${spawnSync('docker', ['logs', postgres], { encoding: 'utf8' }).stderr}`,
    )
    await waitUntil(
      async () => ((await send(`${keycloakUrl}/realms/master/.well-known/openid-configuration`)).status === 200 ? true : undefined),
      240,
      `Keycloak did not answer after 240 seconds\n${spawnSync('docker', ['logs', keycloak], { encoding: 'utf8' }).stdout}`,
    )

    // 3. The server, which reads tenants from the tenant directory and serves a new tenant after its next refresh.
    // It also serves https with a certificate for every tenant host, signed by an authority that only this run trusts.
    const tls = issuePostgresCertificate({ folder, openssl: (...args) => run('openssl', args), name: `*.${tenantDomain}` })
    run('openssl', ['rehash', tls.trustDir])
    const serverPort = await freePort()
    const httpsPort = await freePort()
    const studio = join(folder, 'apps')
    mkdirSync(studio)
    const platform = connection('axis_platform', 'axis_platform', passwords.platform)
    const schema = connection('axis_platform', 'axis_platform_schema', passwords.schema)
    const hostSettings = {
      Tenancy__Source: 'directory',
      Tenancy__RefreshInterval: '00:00:02',
      ConnectionStrings__Platform: platform,
      ConnectionStrings__PlatformSchema: schema,
      Platform__SecretKey: secretKey,
      Modules__Path: join(root, 'artifacts/modules'),
      Templates__Path: join(root, 'artifacts/templates'),
      Studio__ApplicationsPath: studio,
      Studio__AuthorRole: 'author',
      Studio__PublisherRole: 'publisher',
      Studio__Tests: 'true',
    }
    const server = start('server', 'dotnet', [dll('Axis.Server')], {
      cwd: join(root, 'src/Axis.Server'),
      settings: {
        ...hostSettings,
        ASPNETCORE_ENVIRONMENT: 'Testing',
        ASPNETCORE_URLS: `http://127.0.0.1:${serverPort};https://127.0.0.1:${httpsPort}`,
        Kestrel__Certificates__Default__Path: join(tls.serverDir, 'server.crt'),
        Kestrel__Certificates__Default__KeyPath: join(tls.serverDir, 'server.key'),
      },
    })
    const request = (host, path, { method = 'GET', headers = {}, body } = {}) =>
      send(`http://127.0.0.1:${serverPort}${path}`, { method, headers: { host: `${host}:${serverPort}`, ...headers }, body })
    const authority = readFileSync(join(tls.trustDir, 'ca.crt'))
    const secureRequest = (host, path, { method = 'GET', headers = {}, body } = {}) =>
      send(`https://127.0.0.1:${httpsPort}${path}`, { method, headers: { host, ...headers }, body, tls: { ca: authority, servername: host } })
    await waitUntil(
      async () => {
        exitedBefore(server, 'The server')
        return (await request('localhost', '/health/ready')).status === 200 ? true : undefined
      },
      120,
      'The server never answered /health/ready with 200.',
    )

    // 4. The publisher. It exits until the server has created the platform tables, so it starts now, and it prints no ready line.
    const publisher = start('publisher', 'dotnet', [dll('Axis.Admin'), 'publisher'], {
      cwd: join(root, 'src/Axis.Admin'),
      settings: {
        ...hostSettings,
        DOTNET_ENVIRONMENT: 'Development',
        Scenarios__ServerConnectionString: connection('postgres', 'axis_scenario', passwords.scenario),
      },
    })
    await sleep(2000)
    exitedBefore(publisher, 'The publisher')

    // 5. The agent host. It calls `https://<tenant host>/mcp` through a CONNECT proxy, which opens the tunnel to the
    // server's https port, and trusts the run's authority next to the system's roots. Plain http, such as Keycloak's, goes direct.
    const proxyPort = await freePort()
    proxy = http
      .createServer((_, response) => {
        response.writeHead(405)
        response.end()
      })
      .on('connect', (connect, client, head) => {
        const { host, port } = tunnelTarget(connect.url, httpsPort)
        const upstream = net.connect(port, host, () => {
          client.write('HTTP/1.1 200 Connection Established\r\n\r\n')
          upstream.write(head)
          upstream.pipe(client)
          client.pipe(upstream)
        })
        proxySockets.add(client).add(upstream)
        upstream.on('error', () => client.destroy())
        client.on('error', () => upstream.destroy())
        upstream.on('close', () => proxySockets.delete(upstream))
        client.on('close', () => proxySockets.delete(client))
      })
    await new Promise((resolve, reject) => proxy.once('error', reject).listen(proxyPort, '127.0.0.1', resolve))
    const proxyUrl = `http://127.0.0.1:${proxyPort}`
    const agentPort = await freePort()
    const agentHealthUrl = `http://127.0.0.1:${agentPort}`
    const agentHost = start('agent', 'dotnet', [dll('Axis.Agent')], {
      cwd: join(root, 'src/Axis.Agent'),
      settings: {
        DOTNET_ENVIRONMENT: 'Testing',
        Health__Urls: agentHealthUrl,
        Tenancy__Source: 'directory',
        Tenancy__RefreshInterval: '00:00:02',
        ConnectionStrings__Platform: platform,
        Platform__SecretKey: secretKey,
        HTTPS_PROXY: proxyUrl,
        https_proxy: proxyUrl,
        SSL_CERT_DIR: `${tls.trustDir}:${systemCertDir()}`,
        ...agent,
      },
    })
    await waitUntil(
      async () => {
        exitedBefore(agentHost, 'The agent host')
        return (await send(`${agentHealthUrl}/health/ready`)).status === 200 ? true : undefined
      },
      60,
      'The agent host never answered /health/ready with 200.',
    )

    // What `Axis.Admin tenant create` needs, which createTenant reads.
    const adminSettings = {
      DOTNET_ENVIRONMENT: 'Development',
      Tenancy__Source: 'directory',
      Admin__ConnectionString: connection('postgres', 'axis_admin', passwords.admin),
      ConnectionStrings__PlatformSchema: schema,
      Platform__SecretKey: secretKey,
      IdentityProvider__Url: `${keycloakUrl}/`,
      IdentityProvider__AdminClientId: 'axis-admin',
      IdentityProvider__AdminClientSecret: adminSecret,
    }
    return { serverPort, httpsPort, keycloakUrl, agentHealthUrl, request, secureRequest, logs, stop, adminSecret, adminSettings }
  } catch (error) {
    const output = logs()
    await stop()
    error.message = `${error.message}\n${output}`
    throw error
  }
}

/**
 * Gives a case its own tenant, and returns `{ id, host, application, jar, request(path, { method, body }) }`, the signed-in
 * session of the tenant's author. It runs `Axis.Admin tenant create`, adds an author account to the tenant's realm, signs the
 * author in through the server and creates the starting application from its template. `request` sends the session's cookie,
 * and the CSRF header and a JSON body on a write. Each step fails with the tool's output or the HTTP status.
 */
export async function createTenant(stack, { id, start }) {
  const host = tenantHost(id)
  const origin = `http://${host}:${stack.serverPort}`

  const created = spawnSync('dotnet', [dll('Axis.Admin'), 'tenant', 'create', id, '--host', host], {
    cwd: root,
    encoding: 'utf8',
    env: hostEnvironment(stack.adminSettings),
    timeout: 120_000,
  })
  if (created.status !== 0) throw new Error(`tenant create ${id}\n${created.stdout}${created.stderr}`)

  const identity = (url, jar, { method = 'GET', form } = {}) => {
    const body = form ? new URLSearchParams(form).toString() : undefined
    const headers = { ...cookieHeader(jar) }
    if (body !== undefined) headers['content-type'] = 'application/x-www-form-urlencoded'
    return send(url, { method, headers, body })
  }
  const token = await identity(`${stack.keycloakUrl}/realms/master/protocol/openid-connect/token`, new Map(), {
    method: 'POST',
    form: { grant_type: 'client_credentials', client_id: 'axis-admin', client_secret: stack.adminSecret },
  })
  if (token.status !== 200) throw new Error(`The admin token of Keycloak answered ${token.status}: ${token.body}`)
  const accessToken = JSON.parse(token.body).access_token
  const admin = async (method, path, body, ...statuses) => {
    const json = body === undefined ? undefined : JSON.stringify(body)
    const headers = { authorization: `Bearer ${accessToken}` }
    if (json !== undefined) headers['content-type'] = 'application/json'
    const response = await send(`${stack.keycloakUrl}${path}`, { method, headers, body: json })
    if (!statuses.includes(response.status)) throw new Error(`${method} ${path} answered ${response.status}: ${response.body}`)
    return response
  }

  // A new realm has no accounts, no redirect address and no roles mapper.
  const client = await preparePlatformClient({ admin, realm: id, redirectUris: [`${origin}/*`], roles: ['author', 'publisher'] })
  // The agent host's client has no full scope, so the author's roles reach its exchanged token through a roles mapper and a
  // scope mapping of the platform client's roles. The send refuses a token without the author role.
  const agentList = await admin('GET', `/admin/realms/${id}/clients?clientId=axis-agent`, undefined, 200)
  const agentPath = `/admin/realms/${id}/clients/${JSON.parse(agentList.body)[0].id}`
  await admin('POST', `${agentPath}/protocol-mappers/models`, rolesMapper(), 201)
  const platformRoles = await admin('GET', `${client.path}/roles`, undefined, 200)
  await admin('POST', `${agentPath}/scope-mappings/clients/${client.id}`, JSON.parse(platformRoles.body), 204)
  const username = `author@${id}.test`
  const password = secret()
  const otp = newOtpSecret()
  await addAccount({
    admin,
    realm: id,
    client,
    username,
    email: username,
    firstName: 'Author',
    password,
    credential: otpCredential(otp),
    roles: ['author', 'publisher'],
  })

  const { jar } = await signIn({
    app: (path, cookies, headers) => stack.request(host, path, { headers: { ...cookieHeader(cookies), ...headers } }),
    identity,
    realm: id,
    callbackOrigin: origin,
    returnUrl: '/',
    username,
    password,
    secret: otp,
  })

  const session = (path, { method = 'GET', body } = {}) => {
    const headers = { ...cookieHeader(jar) }
    if (method !== 'GET' && method !== 'HEAD') headers['x-axis-csrf'] = '1'
    if (body !== undefined) headers['content-type'] = 'application/json'
    return stack.request(host, path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })
  }
  const application = applicationName(start, id)
  const made = await session('/api/studio/apps', { method: 'POST', body: { name: application, template: startingTemplate(start) } })
  if (made.status !== 201) throw new Error(`Creating the application ${application} answered ${made.status}: ${made.body}`)
  return { id, host, application, jar, request: session }
}
