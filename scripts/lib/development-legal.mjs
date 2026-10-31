// Prototype only (design review 2026-10-30-studio), not to build as is: gives the development realm's `axis` client
// the legal links that the realm sync is to write on each site's client (axis.privacyUrl, axis.termsUrl,
// axis.licencesUrl), so the sign-in pages show every link under scripts/dev.sh. Privacy and terms point at example.com,
// since the organization writes its own policy. Run it as
// `node scripts/lib/development-legal.mjs <keycloak url> <web url>` once Keycloak answers.
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
client.attributes = {
  ...client.attributes,
  'axis.privacyUrl': 'https://example.com/privacy',
  'axis.termsUrl': 'https://example.com/terms',
  'axis.licencesUrl': `${web}/licences`,
}
await call('write the legal links', 'PUT', `${admin}/clients/${client.id}`, token, client)

// Prototype only: turns on Keycloak's "Terms and Conditions" required action and asks it of every user who has not
// accepted yet (no terms_and_conditions attribute), so each development user sees the terms page once.
const actions = await call('read the required actions', 'GET', `${admin}/authentication/required-actions`, token)
const termsAction = actions.find((action) => action.alias === 'TERMS_AND_CONDITIONS')
if (termsAction && !termsAction.enabled) {
  await call(
    'turn on the terms action',
    'PUT',
    `${admin}/authentication/required-actions/TERMS_AND_CONDITIONS`,
    token,
    {
      ...termsAction,
      enabled: true,
    },
  )
}
const users = await call('list the users', 'GET', `${admin}/users?max=500&briefRepresentation=false`, token)
for (const user of users) {
  const accepted = Boolean(user.attributes?.terms_and_conditions)
  if (!accepted && !user.requiredActions?.includes('TERMS_AND_CONDITIONS')) {
    await call(`ask ${user.username} for the terms`, 'PUT', `${admin}/users/${user.id}`, token, {
      ...user,
      requiredActions: [...(user.requiredActions ?? []), 'TERMS_AND_CONDITIONS'],
    })
  }
}

// Prototype only: the tenant's links, which a person accepts, on every client of the realm, as the realm sync is to
// write them, so the terms page names them whichever client the sign-in comes from.
const clients = await call('list the clients', 'GET', `${admin}/clients`, token)
for (const each of clients) {
  each.attributes = {
    ...each.attributes,
    'axis.tenantPrivacyUrl': 'https://example.com/privacy',
    'axis.tenantTermsUrl': 'https://example.com/terms',
  }
  // A client whose policies refuse the update, such as the FAPI test client, is not one people sign in through.
  await call(`write the tenant's links on ${each.clientId}`, 'PUT', `${admin}/clients/${each.id}`, token, each).catch(
    (error) => console.warn(`Skipped ${each.clientId}: ${error.message.slice(0, 80)}`),
  )
}

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
