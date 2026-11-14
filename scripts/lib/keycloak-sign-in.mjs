// The Keycloak side of a smoke test that signs people in: the one-time code of an authenticator, the accounts and the
// roles mapper that a new realm lacks, and the browser's sign-in steps on the Axis theme. scripts/onprem.test.mjs and
// scripts/lib/ai-eval-stack.mjs share it, so one place holds the steps. scripts/ cannot import tests/e2e/tenant-realm.ts,
// whose addRealmAccount makes the same account, see docs/development.md#smoke-tests.
//
// The functions take their transports from the caller, because each test reaches Keycloak and the server its own way:
//   admin(method, path, body, ...statuses)  calls the Keycloak admin API as the admin client and returns { status, headers, body }.
//                                           It throws unless the status is one of `statuses`.
//   app(path, jar, headers)                 sends a request to the server on the tenant's host and returns { status, headers, setCookie, body }.
//   identity(url, jar, { method, form })    sends a request to a Keycloak address and returns the same shape.
import assert from 'node:assert/strict'
import { createHmac, randomBytes } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('../..', import.meta.url))
const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

/** The 6-digit time-based one-time password (RFC 6238, SHA-1, 30 seconds) of the Base32 `secret` at `now`, in milliseconds. */
export function totpCode(secret, now = Date.now()) {
  const bits = [...secret].map((c) => alphabet.indexOf(c).toString(2).padStart(5, '0')).join('')
  const key = Buffer.from(bits.match(/.{8}/g).map((byte) => parseInt(byte, 2)))
  const counter = Buffer.alloc(8)
  counter.writeBigUInt64BE(BigInt(Math.floor(now / 30000)))
  const hash = createHmac('sha1', key).update(counter).digest()
  const offset = hash[hash.length - 1] & 0x0f
  return String((hash.readUInt32BE(offset) & 0x7fffffff) % 1000000).padStart(6, '0')
}

/** A new Base32 authenticator secret of 20 random bytes. */
export function newOtpSecret() {
  let result = ''
  let buffer = 0
  let bits = 0
  for (const byte of randomBytes(20)) {
    buffer = (buffer << 8) | byte
    bits += 8
    while (bits >= 5) {
      bits -= 5
      result += alphabet[(buffer >> bits) & 31]
    }
    buffer &= (1 << bits) - 1
  }
  return bits > 0 ? result + alphabet[(buffer << (5 - bits)) & 31] : result
}

/** The `otp` credential that Keycloak stores for the Base32 `secret`. */
export function otpCredential(secret) {
  return {
    type: 'otp',
    userLabel: 'Test',
    secretData: JSON.stringify({ value: secret }),
    credentialData: JSON.stringify({ subType: 'totp', digits: 6, counter: 0, period: 30, algorithm: 'HmacSHA1', secretEncoding: 'BASE32' }),
  }
}

// A cookie jar is a map from a cookie's name to its value.
export function remember(jar, response) {
  for (const line of response.setCookie) {
    const [pair] = line.split(';')
    const at = pair.indexOf('=')
    const name = pair.slice(0, at).trim()
    const value = pair.slice(at + 1).trim()
    if (value === '') jar.delete(name)
    else jar.set(name, value)
  }
}

export const cookieHeader = (jar) => ({ cookie: [...jar].map(([name, value]) => `${name}=${value}`).join('; ') })

/** The `roles` mapper of the `axis` client in the development realm, which puts the client's roles into the token. */
export function rolesMapper() {
  const realm = JSON.parse(readFileSync(`${root}/samples/keycloak/axis-realm.json`, 'utf8'))
  const mapper = realm.clients.find((client) => client.clientId === 'axis').protocolMappers.find((item) => item.name === 'roles')
  assert.ok(mapper, 'the development realm has no roles mapper on the axis client')
  return mapper
}

/**
 * Sets up the `axis` client of a new realm, which has no redirect address, no roles and no roles mapper: sets its redirect
 * addresses, adds the client roles and the mapper. Returns the client's `id` and its admin API `path`.
 */
export async function preparePlatformClient({ admin, realm, redirectUris, roles }) {
  const listed = await admin('GET', `/admin/realms/${realm}/clients?clientId=axis`, undefined, 200)
  const client = JSON.parse(listed.body)[0]
  assert.ok(client, `realm ${realm} has no axis client`)
  const path = `/admin/realms/${realm}/clients/${client.id}`
  const update = { ...client, redirectUris }
  delete update.secret
  await admin('PUT', path, update, 204)
  for (const name of roles) await admin('POST', `${path}/roles`, { name }, 201)
  await admin('POST', `${path}/protocol-mappers/models`, rolesMapper(), 201)
  return { id: client.id, path }
}

/**
 * Adds an account with a password and an authenticator to `realm`, and gives it the client roles `roles` of `client`, which
 * preparePlatformClient returns. Returns the user's id. Keycloak gives a new account a random id. A caller whose seed names the
 * user by id passes `id`, and the account is then added by a partial import, which keeps the id it is given.
 */
export async function addAccount({
  admin,
  realm,
  client,
  username,
  password,
  credential,
  roles,
  id,
  email = `${username}@axis.local`,
  firstName = username[0].toUpperCase() + username.slice(1),
}) {
  const user = {
    username,
    enabled: true,
    emailVerified: true,
    email,
    firstName,
    lastName: 'User',
    requiredActions: [],
    credentials: [{ type: 'password', value: password, temporary: false }, credential],
  }
  let userId = id
  if (id === undefined) {
    const created = await admin('POST', `/admin/realms/${realm}/users`, user, 201)
    userId = created.headers.location.split('/').pop()
  } else {
    await admin('POST', `/admin/realms/${realm}/partialImport`, { ifResourceExists: 'FAIL', users: [{ id, ...user }] }, 200)
  }
  const representations = []
  for (const name of roles) {
    const role = await admin('GET', `${client.path}/roles/${name}`, undefined, 200)
    representations.push(JSON.parse(role.body))
  }
  await admin('POST', `/admin/realms/${realm}/users/${userId}/role-mappings/clients/${client.id}`, representations, 204)
  return userId
}

const signInContext = (page) => page.body.match(/<script[^>]*id="axis-sign-in-context"[^>]*>([\s\S]*?)<\/script>/)

/**
 * Signs a user of `realm` in through the server and Keycloak, and returns the cookie `jar` with the `signIn` and `done`
 * responses. `startHeaders` go on the sign-in start only. The start is tried `attempts` times, `retryDelayMs` apart,
 * because the host serves a new tenant only after its next refresh. Every assertion about the redirects and the cookies
 * stays here.
 */
export async function signIn({
  app,
  identity,
  realm,
  callbackOrigin,
  returnUrl,
  username,
  password,
  secret,
  startHeaders = {},
  attempts = 15,
  retryDelayMs = 1000,
}) {
  const jar = new Map()
  let start
  for (let i = 0; i < attempts; i++) {
    if (i > 0) await sleep(retryDelayMs)
    start = await app(`/api/auth/sign-in?returnUrl=${encodeURIComponent(returnUrl)}`, jar, startHeaders)
    if (start.status === 302) break
  }
  assert.equal(start.status, 302, `sign-in: ${start.body}`)
  remember(jar, start)
  const authorize = new URL(start.headers.location)
  assert.equal(authorize.pathname, `/realms/${realm}/protocol/openid-connect/auth`)
  assert.equal(authorize.searchParams.get('redirect_uri'), `${callbackOrigin}/api/auth/callback`)

  // Sign the user in at the identity provider: the login page of the Axis theme, then its form. The theme puts
  // the form's action in the JSON of the axis-sign-in-context script.
  const keycloakJar = new Map()
  const loginPage = await identity(authorize.href, keycloakJar)
  assert.equal(loginPage.status, 200, `the Keycloak login page: ${loginPage.body}`)
  remember(keycloakJar, loginPage)
  const context = signInContext(loginPage)
  assert.ok(context, 'the sign-in page does not show the Axis theme')
  const action = JSON.parse(context[1]).loginAction
  assert.ok(action, 'the sign-in context has no loginAction')
  const passwordStep = await identity(action, keycloakJar, { method: 'POST', form: { username, password } })
  assert.equal(passwordStep.status, 200, `the Keycloak password step: ${passwordStep.body}`)
  remember(keycloakJar, passwordStep)

  // The password is followed by the two-step page of the Axis theme, which carries the code form's action too.
  const codeContext = signInContext(passwordStep)
  assert.ok(codeContext, 'the Keycloak password step does not show the Axis theme')
  const codePage = JSON.parse(codeContext[1])
  assert.equal(codePage.page, 'twoStep', 'the Keycloak password step is not the two-step page')
  const login = await identity(codePage.loginAction, keycloakJar, {
    method: 'POST',
    form: { otp: totpCode(secret), login: 'Sign In' },
  })
  assert.equal(login.status, 302, `the Keycloak login: ${login.body}`)
  const callback = new URL(login.headers.location)
  assert.equal(`${callback.origin}${callback.pathname}`, `${callbackOrigin}/api/auth/callback`)

  // The callback creates the session.
  const done = await app(`${callback.pathname}${callback.search}`, jar)
  assert.equal(done.status, 302, `the callback: ${done.body}`)
  remember(jar, done)
  return { jar, signIn: start, done }
}
