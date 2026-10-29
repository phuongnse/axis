// Prepares the `load` realm of the stack that scripts/load.sh starts, through the identity provider's admin API, as
// scripts/onprem.test.mjs does for its realms: the redirect addresses of the axis client, the roles mapper and the roles
// of the generated application. It then adds the users load-user-01 to load-user-50 with a password, an authenticator
// and all roles, and writes their names, password and authenticator secrets to <folder>/k6/users.json for k6.
// Run it as `node scripts/lib/load-realm.mjs <folder> <https port> <seed>` after `admin tenant create load`, and before
// the activation, which copies the redirect addresses and mappers of the axis client to the client of the site.
// Every request goes through curl, so that a test can replace it with a stub.
import { spawnSync } from 'node:child_process'
import { createHmac, randomBytes } from 'node:crypto'
import { chmodSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { identityHost, tenantHost } from './load-stack.mjs'

const root = fileURLToPath(new URL('../..', import.meta.url))

export const loadUserCount = 50

// The realm of the tenant `load`, and how long the token of the master realm is used. The token lasts 60 seconds.
const realm = 'load'
const tokenSeconds = 30

const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
// Base32 (RFC 4648, no padding) of the bytes. 20 bytes give 32 characters.
const base32 = (bytes) => {
  const bits = [...bytes].map((byte) => byte.toString(2).padStart(8, '0')).join('')
  return bits.match(/.{1,5}/g).map((chunk) => alphabet[parseInt(chunk.padEnd(5, '0'), 2)]).join('')
}

// [{ username: 'load-user-01', password, secret }]. The secret is the Base32 of the first 20 bytes of
// HMAC-SHA256(`axis-load-<seed>`, username), so the same seed gives the same secrets.
export const loadUsers = (seed, password) =>
  Array.from({ length: loadUserCount }, (_, index) => {
    const username = `load-user-${String(index + 1).padStart(2, '0')}`
    const secret = base32(createHmac('sha256', `axis-load-${seed}`).update(username).digest().subarray(0, 20))
    return { username, password, secret }
  })

export const prepareLoadRealm = ({ folder: target, port, seed }) => {
  const folder = resolve(target)
  const base = `https://${identityHost}:${port}`
  const realmFolder = join(folder, 'realm')
  mkdirSync(realmFolder, { recursive: true, mode: 0o700 })
  const authorization = join(realmFolder, 'authorization')
  const adminSecret = readFileSync(join(folder, 'secrets/IdentityProvider__AdminClientSecret'), 'utf8').trim()

  // curl <options> <url>: the URL comes last. The body goes through stdin, so no secret is on a command line.
  const curl = (method, path, { headers = [], body, output = [] } = {}) => {
    const args = [
      '-fsS',
      '--cacert', join(folder, 'ca/ca.crt'),
      '--resolve', `${identityHost}:${port}:127.0.0.1`,
      '-X', method,
      ...headers,
      ...(body === undefined ? [] : ['--data-binary', '@-']),
      ...output,
      `${base}${path}`,
    ]
    const result = spawnSync('curl', args, { encoding: 'utf8', input: body })
    if (result.status !== 0) throw new Error(`${method} ${path} failed with status ${result.status}\n${result.stdout}${result.stderr}`)
    return result.stdout
  }

  // The bearer token of the admin client, in a file that curl reads, replaced when it is older than tokenSeconds.
  let issued = 0
  const authenticate = () => {
    if (Date.now() - issued < tokenSeconds * 1000) return
    const form = new URLSearchParams({ grant_type: 'client_credentials', client_id: 'axis-admin', client_secret: adminSecret })
    const token = JSON.parse(curl('POST', '/realms/master/protocol/openid-connect/token', { body: form.toString() })).access_token
    writeFileSync(authorization, `Authorization: Bearer ${token}\n`, { mode: 0o600 })
    issued = Date.now()
  }
  const admin = (method, path, body, output) => {
    authenticate()
    const json = body === undefined ? undefined : JSON.stringify(body)
    const headers = ['-H', `@${authorization}`, ...(json === undefined ? [] : ['-H', 'Content-Type: application/json'])]
    return curl(method, `/admin/realms/${realm}${path}`, { headers, body: json, output })
  }

  const roleNames = readdirSync(join(folder, 'apps/load/roles'))
    .filter((name) => name.endsWith('.json'))
    .sort()
    .map((name) => JSON.parse(readFileSync(join(folder, 'apps/load/roles', name), 'utf8')).name)

  const platformClient = JSON.parse(admin('GET', '/clients?clientId=axis'))[0]
  if (!platformClient) throw new Error(`the realm ${realm} has no axis client`)
  const clientPath = `/clients/${platformClient.id}`
  const update = { ...platformClient, redirectUris: [`https://${tenantHost}/*`, `https://${tenantHost}:${port}/*`] }
  delete update.secret
  admin('PUT', clientPath, update)
  for (const name of roleNames) admin('POST', `${clientPath}/roles`, { name })
  const developmentRealm = JSON.parse(readFileSync(join(root, 'samples/keycloak/axis-realm.json'), 'utf8'))
  const mapper = developmentRealm.clients.find((client) => client.clientId === 'axis')?.protocolMappers.find((item) => item.name === 'roles')
  if (!mapper) throw new Error('the development realm has no roles mapper on the axis client')
  admin('POST', `${clientPath}/protocol-mappers/models`, mapper)
  const roles = JSON.parse(admin('GET', `${clientPath}/roles`))

  const users = loadUsers(seed, randomBytes(18).toString('hex'))
  for (const { username, password, secret } of users) {
    // -D - prints the response headers, and the Location header names the new user.
    const created = admin(
      'POST',
      '/users',
      {
        username,
        enabled: true,
        emailVerified: true,
        email: `${username}@axis.local`,
        firstName: 'Load',
        lastName: `User ${username.slice(-2)}`,
        requiredActions: [],
        credentials: [
          { type: 'password', value: password, temporary: false },
          {
            type: 'otp',
            userLabel: 'Load',
            secretData: JSON.stringify({ value: secret }),
            credentialData: JSON.stringify({ subType: 'totp', digits: 6, counter: 0, period: 30, algorithm: 'HmacSHA1', secretEncoding: 'BASE32' }),
          },
        ],
      },
      ['-D', '-', '-o', '/dev/null'],
    )
    const location = created.split(/\r?\n/).find((line) => /^location:/i.test(line))
    if (!location) throw new Error(`creating ${username} gave no Location header\n${created}`)
    const userId = location.slice(location.indexOf(':') + 1).trim().split('/').pop()
    admin('POST', `/users/${userId}/role-mappings/clients/${platformClient.id}`, roles)
  }

  const output = join(folder, 'k6/users.json')
  writeFileSync(output, JSON.stringify(users, null, 2))
  chmodSync(output, 0o644)
  return users
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const [folder, port, seed] = process.argv.slice(2)
  if (!folder || !port || seed === undefined) {
    console.error('Usage: node scripts/lib/load-realm.mjs <folder> <https port> <seed>')
    process.exit(2)
  }
  prepareLoadRealm({ folder, port, seed })
}
