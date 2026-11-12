import assert from 'node:assert/strict'
import { describe, test } from 'node:test'
import { addAccount, newOtpSecret, otpCredential, preparePlatformClient, signIn, totpCode } from './lib/keycloak-sign-in.mjs'

const page = (context) => `<html><script id="axis-sign-in-context" type="application/json">${JSON.stringify(context)}</script></html>`
const reply = (status, { headers = {}, setCookie = [], body = '' } = {}) => ({ status, headers, setCookie, body })

describe('totpCode', () => {
  test('gives the RFC 6238 SHA-1 code at 59 seconds', () => {
    // The secret is the ASCII string 12345678901234567890. RFC 6238 gives 94287082 for it at 59 seconds, so 287082 in 6 digits.
    assert.equal(totpCode('GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ', 59_000), '287082')
  })

  test('gives the RFC 6238 SHA-1 code at 1111111109 seconds', () => {
    assert.equal(totpCode('GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ', 1_111_111_109_000), '081804')
  })

  test('keeps a code for the whole 30 seconds', () => {
    const secret = newOtpSecret()
    assert.equal(totpCode(secret, 60_000), totpCode(secret, 89_999))
  })
})

describe('the authenticator', () => {
  test('a new secret is 20 random bytes in Base32', () => {
    const secret = newOtpSecret()
    assert.match(secret, /^[A-Z2-7]{32}$/)
    assert.notEqual(secret, newOtpSecret())
  })

  test('the credential holds the secret as a time-based code of 6 digits', () => {
    const credential = otpCredential('GEZDGNBVGY3TQOJQ')
    assert.equal(credential.type, 'otp')
    assert.equal(JSON.parse(credential.secretData).value, 'GEZDGNBVGY3TQOJQ')
    assert.deepEqual(JSON.parse(credential.credentialData), {
      subType: 'totp',
      digits: 6,
      counter: 0,
      period: 30,
      algorithm: 'HmacSHA1',
      secretEncoding: 'BASE32',
    })
  })
})

describe('the realm account setup', () => {
  const recorder = () => {
    const calls = []
    const admin = async (method, path, body, ...statuses) => {
      calls.push({ method, path, body, statuses })
      if (method === 'GET' && path.includes('/clients?clientId=axis')) return reply(200, { body: JSON.stringify([{ id: 'c1', secret: 's', clientId: 'axis' }]) })
      if (method === 'GET' && path.includes('/roles/')) return reply(200, { body: JSON.stringify({ name: path.split('/').pop() }) })
      if (method === 'POST' && path.endsWith('/users')) return reply(201, { headers: { location: 'http://kc/admin/realms/r/users/u1' } })
      return reply(statuses[0])
    }
    return { calls, admin }
  }

  test('the platform client gets its redirect addresses, roles and the roles mapper', async () => {
    const { calls, admin } = recorder()
    const client = await preparePlatformClient({ admin, realm: 'r', redirectUris: ['http://a/*'], roles: ['author', 'publisher'] })
    assert.deepEqual(client, { id: 'c1', path: '/admin/realms/r/clients/c1' })
    const update = calls.find((call) => call.method === 'PUT')
    assert.deepEqual(update.body.redirectUris, ['http://a/*'])
    assert.equal('secret' in update.body, false)
    assert.deepEqual(
      calls.filter((call) => call.path.endsWith('/roles')).map((call) => call.body),
      [{ name: 'author' }, { name: 'publisher' }],
    )
    assert.equal(calls.find((call) => call.path.endsWith('/protocol-mappers/models')).body.name, 'roles')
  })

  test('an account gets a password, an authenticator and its client roles', async () => {
    const { calls, admin } = recorder()
    const credential = otpCredential('GEZDGNBVGY3TQOJQ')
    const id = await addAccount({
      admin,
      realm: 'r',
      client: { id: 'c1', path: '/admin/realms/r/clients/c1' },
      username: 'author@x.test',
      email: 'author@x.test',
      password: 'pw',
      credential,
      roles: ['author'],
    })
    assert.equal(id, 'u1')
    const created = calls.find((call) => call.path.endsWith('/users')).body
    assert.equal(created.username, 'author@x.test')
    assert.deepEqual(created.credentials, [{ type: 'password', value: 'pw', temporary: false }, credential])
    assert.deepEqual(created.requiredActions, [])
    const mapping = calls.find((call) => call.path.endsWith('/role-mappings/clients/c1'))
    assert.deepEqual(mapping.body, [{ name: 'author' }])
  })

  test('the email and the first name default from the username', async () => {
    const { calls, admin } = recorder()
    await addAccount({ admin, realm: 'r', client: { id: 'c1', path: '/p' }, username: 'author', password: 'pw', credential: {}, roles: [] })
    const created = calls.find((call) => call.path.endsWith('/users')).body
    assert.equal(created.email, 'author@axis.local')
    assert.equal(created.firstName, 'Author')
  })
})

describe('signIn', () => {
  const secret = 'GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ'
  const authorize = 'http://kc/realms/r/protocol/openid-connect/auth?client_id=axis&redirect_uri=http%3A%2F%2Ft.localhost%3A1%2Fapi%2Fauth%2Fcallback'
  const callbackUrl = 'http://t.localhost:1/api/auth/callback?code=abc&state=xyz'

  const fakes = ({ passwordPage = page({ page: 'twoStep', loginAction: 'http://kc/code' }) } = {}) => {
    const appCalls = []
    const identityCalls = []
    let starts = 0
    const app = async (path, jar, headers) => {
      appCalls.push({ path, cookie: [...jar], headers })
      if (path.startsWith('/api/auth/sign-in')) {
        starts++
        return starts === 1 ? reply(404, { body: 'no tenant yet' }) : reply(302, { headers: { location: authorize }, setCookie: ['axis-state=s1; Path=/; HttpOnly'] })
      }
      return reply(302, { headers: { location: '/' }, setCookie: ['axis-session=session1; Path=/; HttpOnly'] })
    }
    const identity = async (url, jar, options = {}) => {
      identityCalls.push({ url, cookie: [...jar], ...options })
      if (url === authorize) return reply(200, { body: page({ loginAction: 'http://kc/login' }), setCookie: ['KC=k1; Path=/'] })
      if (url === 'http://kc/login') return reply(200, { body: passwordPage })
      return reply(302, { headers: { location: callbackUrl } })
    }
    return { app, identity, appCalls, identityCalls }
  }

  const options = (transports) => ({
    ...transports,
    realm: 'r',
    callbackOrigin: 'http://t.localhost:1',
    returnUrl: '/leave',
    username: 'author',
    password: 'pw',
    secret,
    retryDelayMs: 0,
  })

  test('retries the start, signs in on the two-step page and returns the session', async () => {
    const transports = fakes()
    const result = await signIn({ ...options(transports), startHeaders: { 'x-forwarded-proto': 'https' } })

    assert.equal(transports.appCalls.filter((call) => call.path.startsWith('/api/auth/sign-in')).length, 2)
    assert.equal(transports.appCalls[0].path, '/api/auth/sign-in?returnUrl=%2Fleave')
    assert.deepEqual(transports.appCalls[0].headers, { 'x-forwarded-proto': 'https' })
    // The start headers go on the start only.
    assert.deepEqual(transports.appCalls.at(-1).headers, undefined)

    const [, password, code] = transports.identityCalls
    assert.deepEqual(password.form, { username: 'author', password: 'pw' })
    assert.equal(password.url, 'http://kc/login')
    assert.deepEqual(password.cookie, [['KC', 'k1']])
    assert.equal(code.url, 'http://kc/code')
    assert.deepEqual(code.form, { otp: totpCode(secret), login: 'Sign In' })

    // The callback goes to the server with the cookie of the start, and the session joins the jar.
    const callback = transports.appCalls.at(-1)
    assert.equal(callback.path, '/api/auth/callback?code=abc&state=xyz')
    assert.deepEqual(callback.cookie, [['axis-state', 's1']])
    assert.equal(result.jar.get('axis-session'), 'session1')
    assert.equal(result.signIn.status, 302)
    assert.equal(result.done.status, 302)
  })

  test('fails when the start never answers 302', async () => {
    const transports = fakes()
    transports.app = async () => reply(404, { body: 'no tenant' })
    await assert.rejects(signIn({ ...options(transports), attempts: 2 }), /sign-in: no tenant/)
  })

  test('fails when the login page does not show the Axis theme', async () => {
    const transports = fakes()
    const identity = transports.identity
    transports.identity = async (url, jar, request) =>
      url === authorize ? reply(200, { body: '<html>Keycloak default</html>' }) : identity(url, jar, request)
    await assert.rejects(signIn(options(transports)), /does not show the Axis theme/)
  })

  test('fails when the password step is not the two-step page', async () => {
    const transports = fakes({ passwordPage: page({ page: 'login', loginAction: 'http://kc/login' }) })
    await assert.rejects(signIn(options(transports)), /not the two-step page/)
  })
})
