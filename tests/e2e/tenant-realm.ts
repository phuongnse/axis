import { randomBytes } from 'node:crypto'
import { required } from './server-settings'

const base32Alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'

function base32(bytes: Buffer): string {
  let result = ''
  let buffer = 0
  let bits = 0
  for (const byte of bytes) {
    buffer = (buffer << 8) | byte
    bits += 8
    while (bits >= 5) {
      bits -= 5
      result += base32Alphabet[(buffer >> bits) & 31]
    }
    buffer &= (1 << bits) - 1
  }
  return bits > 0 ? result + base32Alphabet[(buffer << (5 - bits)) & 31] : result
}

/** A token of the `axis-admin` client, which the E2E Keycloak creates in its master realm. */
async function adminToken(): Promise<string> {
  const keycloakUrl = required('AXIS_E2E_KEYCLOAK_URL', 'the address of Keycloak')
  const response = await fetch(`${keycloakUrl}/realms/master/protocol/openid-connect/token`, {
    method: 'POST',
    body: new URLSearchParams({
      grant_type: 'client_credentials',
      client_id: 'axis-admin',
      client_secret: required('AXIS_E2E_IDENTITY_ADMIN_SECRET', 'the secret of the axis-admin client'),
    }),
  })
  if (!response.ok) throw new Error(`Signing in as axis-admin failed with HTTP ${response.status}.`)
  return ((await response.json()) as { access_token: string }).access_token
}

/** Calls the admin API of Keycloak. A failure names the step and the HTTP status. */
async function admin(step: string, method: string, apiPath: string, body?: unknown): Promise<Response> {
  const keycloakUrl = required('AXIS_E2E_KEYCLOAK_URL', 'the address of Keycloak')
  const response = await fetch(`${keycloakUrl}/admin/realms${apiPath}`, {
    method,
    headers: {
      Authorization: `Bearer ${await adminToken()}`,
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) throw new Error(`${step} failed with HTTP ${response.status}.`)
  return response
}

/** The `clientId` of every client in `realm`. */
export async function realmClientIds(realm: string): Promise<string[]> {
  const response = await admin(`Listing the clients of ${realm}`, 'GET', `/${realm}/clients`)
  return ((await response.json()) as { clientId: string }[]).map((client) => client.clientId)
}

/** The type of every credential that the user `userId` of `realm` has, such as `password` and `otp`. */
export async function userCredentialTypes(realm: string, userId: string): Promise<string[]> {
  const response = await admin(`Listing the credentials of ${userId}`, 'GET', `/${realm}/users/${userId}/credentials`)
  return ((await response.json()) as { type: string }[]).map((credential) => credential.type)
}

export interface RealmAccount {
  id: string
  username: string
  password: string
  /** The Base32 secret of the authenticator. Pass it to `totp` in `tests/e2e/totp.ts` for a code. */
  secret: string
}

/** Adds an account with a password and an authenticator to `realm`. The email is the username. */
export async function addRealmAccount(
  realm: string,
  { email, firstName = 'Test', lastName = 'Account' }: { email: string; firstName?: string; lastName?: string },
): Promise<RealmAccount> {
  // 36 hex characters pass the realm's length rule and are not on the breached-password list.
  const password = randomBytes(18).toString('hex')
  const secret = base32(randomBytes(20))
  const response = await admin(`Creating ${email} in ${realm}`, 'POST', `/${realm}/users`, {
    username: email,
    email,
    firstName,
    lastName,
    enabled: true,
    emailVerified: true,
    requiredActions: [],
    credentials: [
      { type: 'password', value: password, temporary: false },
      {
        type: 'otp',
        userLabel: 'Test',
        secretData: JSON.stringify({ value: secret }),
        credentialData: JSON.stringify({ subType: 'totp', digits: 6, counter: 0, period: 30, algorithm: 'HmacSHA1', secretEncoding: 'BASE32' }),
      },
    ],
  })
  const id = response.headers.get('Location')?.split('/').pop()
  if (!id) throw new Error(`Creating ${email} in ${realm} returned no Location header.`)
  return { id, username: email, password, secret }
}
