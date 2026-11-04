// Prototype only (design review 2026-10-30-studio), not to build as is: gives the development realm the Axis email theme
// and Mailpit, the company realm its own theme, and the axis client the SPA's address, which frame.ftl uses for the
// licences link because the realm sync writes https://{host}/licences and the SPA answers over http on its own port. The
// legal links and the terms step are not here: scripts/dev.sh sets the tenant's Legal block and the realm sync writes
// them. Run it as `node scripts/lib/development-legal.mjs <keycloak url> <web url>` once Keycloak answers.
const [keycloak, web] = process.argv.slice(2).map((value) => value.replace(/\/+$/, ''))

async function call(step, method, url, token, body) {
  const headers = token ? { Authorization: `Bearer ${token}` } : {}
  if (body && !(body instanceof URLSearchParams)) headers['Content-Type'] = 'application/json'
  const response = await fetch(url, {
    method,
    headers,
    body: body instanceof URLSearchParams ? body : body && JSON.stringify(body),
  })
  const text = await response.text()
  if (!response.ok) throw new Error(`${step} failed with HTTP ${response.status}: ${text}`)
  return text === '' ? undefined : JSON.parse(text)
}

const form = new URLSearchParams({
  grant_type: 'password',
  client_id: 'admin-cli',
  username: 'admin',
  password: 'admin',
})
const { access_token: token } = await call(
  'get the admin token',
  'POST',
  `${keycloak}/realms/master/protocol/openid-connect/token`,
  undefined,
  form,
)
const admin = `${keycloak}/admin/realms/axis`
const [client] = await call('find the axis client', 'GET', `${admin}/clients?clientId=axis`, token)
client.baseUrl = web
await call('write the address of the axis client', 'PUT', `${admin}/clients/${client.id}`, token, client)

// Prototype only: the realm sends its emails in the Axis email theme, as tenant create is to set it.
const realmSettings = await call('read the realm', 'GET', admin, token)
// Prototype only: the development realm sends its emails to Mailpit, so the Axis emails can be seen at its web page.
const smtpServer = { host: 'mailpit', port: '1025', from: 'no-reply@axis.localhost', fromDisplayName: 'Axis' }
if (realmSettings.emailTheme !== 'axis' || realmSettings.smtpServer?.host !== 'mailpit') {
  await call('use the Axis email theme', 'PUT', admin, token, { ...realmSettings, emailTheme: 'axis', smtpServer })
}

// Prototype only: the sample company realm signs people in with its own theme, as samples/keycloak/company-realm.json
// now names it; a Keycloak container that already holds the realm gets it here.
const companyRealm = await call('read the company realm', 'GET', `${keycloak}/admin/realms/company`, token)
if (companyRealm.loginTheme !== 'example-company') {
  await call('use the company theme', 'PUT', `${keycloak}/admin/realms/company`, token, {
    ...companyRealm,
    loginTheme: 'example-company',
    internationalizationEnabled: true,
    supportedLocales: ['en', 'vi'],
    defaultLocale: 'en',
  })
}
