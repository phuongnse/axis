// Adds the `axis first login` flow to the development `axis` realm through the identity provider's admin API, as
// TenantRealm.EnsureFirstLoginFlowAsync does for a tenant realm. The realm sync names that flow on every brokered provider,
// and Keycloak refuses a provider whose flow is missing. The flow is not in samples/keycloak/axis-realm.json, because a
// partial authenticationFlows import drops Keycloak's built-in flows. Its one step is a required "create user if unique"
// step, so a brokered person gets a new user at the first sign-in and is never linked to an existing one.
// Run it as `node scripts/lib/development-realm.mjs <keycloak url>` once Keycloak answers. It signs in to the master realm
// as admin, which Docker Compose and scripts/e2e.sh set up. It only reads when the realm already has the right flow, so
// scripts/dev.sh can run it against a Keycloak container that it reuses. It exits non-zero and names the step and the
// HTTP status of a call that fails.
import { fileURLToPath } from 'node:url'

const realm = 'axis'
const alias = 'axis first login'
const createUserIfUnique = 'idp-create-user-if-unique'

export async function ensureFirstLoginFlow(baseUrl) {
  const base = baseUrl.replace(/\/+$/, '')
  let token = ''

  const call = async (step, method, url, body) => {
    const headers = {}
    if (token) headers.Authorization = `Bearer ${token}`
    let payload
    if (body instanceof URLSearchParams) {
      payload = body
    } else if (body !== undefined) {
      headers['Content-Type'] = 'application/json'
      payload = JSON.stringify(body)
    }
    const response = await fetch(url, { method, headers, body: payload }).catch((error) => {
      throw new Error(`${step} failed: ${error.cause?.message ?? error.message}`)
    })
    const text = await response.text()
    if (!response.ok) throw new Error(`${step} failed with HTTP ${response.status}: ${text}`)
    return text === '' ? undefined : JSON.parse(text)
  }

  const tokenForm = new URLSearchParams({
    grant_type: 'password',
    client_id: 'admin-cli',
    username: 'admin',
    password: 'admin',
  })
  token = (
    await call('get the admin token', 'POST', `${base}/realms/master/protocol/openid-connect/token`, tokenForm)
  ).access_token

  const admin = `${base}/admin/realms/${realm}`
  const flows = await call('list the authentication flows', 'GET', `${admin}/authentication/flows`)
  if (!flows.some((flow) => flow.alias === alias)) {
    await call('create the first-login flow', 'POST', `${admin}/authentication/flows`, {
      alias,
      description: "Creates a new user at a person's first sign-in through a brokered provider and never links one.",
      providerId: 'basic-flow',
      topLevel: true,
      builtIn: false,
    })
  }

  const path = `${admin}/authentication/flows/${encodeURIComponent(alias)}/executions`
  let executions = await call('read the first-login flow', 'GET', path)
  if (!executions.some((execution) => execution.providerId === createUserIfUnique)) {
    await call('add the create-user step', 'POST', `${path}/execution`, { provider: createUserIfUnique })
    executions = await call('read the first-login flow', 'GET', path)
  }

  const create = executions.find((execution) => execution.providerId === createUserIfUnique)
  for (const execution of executions) {
    if (execution === create) continue
    await call(
      'remove another step of the first-login flow',
      'DELETE',
      `${admin}/authentication/executions/${execution.id}`,
    )
  }

  if (create.requirement !== 'REQUIRED') {
    await call('require the create-user step', 'PUT', path, { ...create, requirement: 'REQUIRED' })
  }
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const [baseUrl] = process.argv.slice(2)
  if (!baseUrl) {
    console.error('Usage: node scripts/lib/development-realm.mjs <keycloak url>')
    process.exit(2)
  }
  try {
    await ensureFirstLoginFlow(baseUrl)
  } catch (error) {
    console.error(`development-realm: ${error.message}`)
    process.exitCode = 1
  }
}
