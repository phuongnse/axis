import { createHash, randomBytes } from 'node:crypto'
import { mkdtemp, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { chromium, type Browser, type Page } from '@playwright/test'
import { expect, test } from '../test'
import { runAdmin } from '../admin-tool'
import { startDirectoryServer } from '../directory-server'
import { confirmDialog } from '../dialogs'
import { addAuthenticator, submitCode, submitPassword } from '../keycloak'
import { addConsentClient, addRealmAccount, setRealmPassword } from '../tenant-realm'
import { liveTotp, nextTotp, totp } from '../totp'

// tenant create makes a database, two roles and a realm, and the realm step waits on Keycloak.
test.setTimeout(240_000)
test.describe.configure({ mode: 'serial' })

const tenant = 'connected-apps-made'
const host = `${tenant}.localhost`
const signIn = `https://${host}/api/auth/sign-in?returnUrl=/`
const clientId = 'connected-apps-client'
const redirectUri = `https://${tenant}.test/callback`

let applications: string
let server: Awaited<ReturnType<typeof startDirectoryServer>>
let browser: Browser
// The first administrator signs in again in the second test, with a code that differs from the one the first test used.
let administrator: { username: string; password: string; secret: string; usedCode: string }

test.beforeAll(async () => {
  const created = await runAdmin(['tenant', 'create', tenant, '--host', host, '--administrator', `first-admin@${tenant}.test`])
  expect(created.status, `${created.stdout}\n${created.stderr}`).toBe(0)
  // The role gives the client's tokens the author role that /mcp asks for, see addConsentClient.
  await addConsentClient(tenant, { clientId, name: 'Draft helper', baseUrl: `https://${tenant}.test/home`, redirectUri, role: 'author' })

  // Offset 8, because the other specs that start a server use offsets 0 to 6, and offset 7 is the port of the control
  // plane. The main E2E server's tenant has no directory row, so the Connected apps tab would be hidden there. /mcp
  // needs Studio on and the author role.
  applications = await mkdtemp(path.join(os.tmpdir(), 'axis-connected-apps-'))
  server = await startDirectoryServer(8, { Studio__AuthorRole: 'author', Studio__ApplicationsPath: applications })
  browser = await chromium.launch({
    args: [`--host-resolver-rules=MAP ${host}:443 127.0.0.1:${server.httpsPort}`],
  })
})

test.afterAll(async () => {
  await browser?.close()
  await server?.stop()
  if (applications) {
    await rm(applications, { recursive: true, force: true })
  }
})

/** Opens the Connected apps tab of the users page in `page` and returns the tab. */
async function openConnectedApps(page: Page) {
  await page.goto(`https://${host}/users`)
  await page.getByRole('tab', { name: 'Connected apps', exact: true }).click()
  return page.getByTestId('connected-apps')
}

/** The status that `/mcp` of the tenant answers to an `initialize` call with `token`. The page calls it, because Node's fetch refuses the self-signed certificate. */
async function mcpStatus(page: Page, token: string): Promise<number> {
  await page.goto(`https://${host}/.well-known/oauth-protected-resource/mcp`)
  return page.evaluate(async (accessToken) => {
    const response = await fetch('/mcp', {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${accessToken}`,
        'Content-Type': 'application/json',
        Accept: 'application/json, text/event-stream',
      },
      body: JSON.stringify({
        jsonrpc: '2.0',
        id: 1,
        method: 'initialize',
        params: { protocolVersion: '2025-11-25', capabilities: {}, clientInfo: { name: 'e2e', version: '1' } },
      }),
    })
    return response.status
  }, token)
}

test('an administrator allows one outside client in Connected apps and a reload shows it, and a member has no tab', async () => {
  const member = await addRealmAccount(tenant, { email: `member@${tenant}.test` })
  const memberPage = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
  const adminPage = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()

  // A member is refused the users page, so it has no Connected apps tab and the route answers 403.
  await memberPage.goto(signIn)
  await submitPassword(memberPage, member.username, member.password)
  await submitCode(memberPage, await liveTotp(member.secret))
  await memberPage.waitForURL(`https://${host}/`)
  await memberPage.goto(`https://${host}/users`)
  await expect(memberPage.getByTestId('users-forbidden')).toBeVisible()
  await expect(memberPage.getByRole('tab', { name: 'Connected apps', exact: true })).toHaveCount(0)
  const answer = await memberPage.goto(`https://${host}/api/tenant/outside-clients`)
  expect(answer?.status()).toBe(403)

  // The administrator that tenant create invited allows one client, and the choice survives a reload.
  const firstAdmin = await setRealmPassword(tenant, `first-admin@${tenant}.test`)
  await adminPage.goto(signIn)
  await submitPassword(adminPage, firstAdmin.username, firstAdmin.password)
  const secret = await addAuthenticator(adminPage)
  administrator = { ...firstAdmin, secret, usedCode: totp(secret) }
  await adminPage.waitForURL(`https://${host}/`)

  const tab = await openConnectedApps(adminPage)
  await tab.getByRole('radio', { name: 'Allow list', exact: true }).check()
  await tab.getByRole('combobox', { name: 'Allowed client IDs', exact: true }).fill('e2e-client')
  await adminPage.keyboard.press('Enter')
  await tab.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(adminPage.getByTestId('connected-apps-notice')).toHaveText('The setting was saved.')

  await adminPage.reload()
  await adminPage.getByRole('tab', { name: 'Connected apps', exact: true }).click()
  await expect(tab.getByRole('radio', { name: 'Allow list', exact: true })).toBeChecked()
  await expect(tab.getByText('e2e-client', { exact: true })).toBeVisible()
})

test('a person consents to an allowed client and calls /mcp, then the administrator revokes the consent and the next call answers 401', async () => {
  const adminPage = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
  await adminPage.goto(signIn)
  await submitPassword(adminPage, administrator.username, administrator.password)
  await submitCode(adminPage, await nextTotp(administrator.secret, administrator.usedCode))
  await adminPage.waitForURL(`https://${host}/`)

  // The administrator adds the client to the allow list, which already holds e2e-client.
  const tab = await openConnectedApps(adminPage)
  await tab.getByRole('radio', { name: 'Allow list', exact: true }).check()
  await tab.getByRole('combobox', { name: 'Allowed client IDs', exact: true }).fill(clientId)
  await adminPage.keyboard.press('Enter')
  await tab.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(adminPage.getByTestId('connected-apps-notice')).toHaveText('The setting was saved.')

  // A person signs in through the client with PKCE and allows it, and the client gets a code.
  const author = await addRealmAccount(tenant, { email: `author@${tenant}.test` })
  const verifier = randomBytes(32).toString('base64url')
  const query = new URLSearchParams({
    client_id: clientId,
    redirect_uri: redirectUri,
    response_type: 'code',
    scope: 'openid axis:authoring',
    state: 's',
    code_challenge: createHash('sha256').update(verifier).digest('base64url'),
    code_challenge_method: 'S256',
  })
  const personPage = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
  const realmUrl = `${process.env.AXIS_E2E_KEYCLOAK_URL}/realms/${tenant}/protocol/openid-connect`
  await personPage.goto(`${realmUrl}/auth?${query}`)
  await submitPassword(personPage, author.username, author.password)
  await submitCode(personPage, await liveTotp(author.secret))
  // Nothing serves the redirect address, so the page fails to load and the test reads the request.
  const request = personPage.waitForRequest((candidate) => candidate.url().startsWith(redirectUri))
  await personPage.getByRole('button', { name: 'Allow', exact: true }).click()
  const code = new URL((await request).url()).searchParams.get('code')
  expect(code).toBeTruthy()

  const exchanged = await fetch(`${realmUrl}/token`, {
    method: 'POST',
    body: new URLSearchParams({
      grant_type: 'authorization_code',
      client_id: clientId,
      code: code!,
      redirect_uri: redirectUri,
      code_verifier: verifier,
    }),
  })
  expect(exchanged.status).toBe(200)
  const token = ((await exchanged.json()) as { access_token: string }).access_token
  const clientPage = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
  expect(await mcpStatus(clientPage, token)).toBe(200)

  // The administrator sees the client with that person and revokes it.
  await openConnectedApps(adminPage)
  const row = adminPage
    .getByTestId('connected-apps-list')
    .getByRole('row')
    .filter({ has: adminPage.getByText(clientId, { exact: true }) })
  await expect(row).toContainText(author.username)
  await row.getByRole('button', { name: 'Revoke…', exact: true }).click()
  await confirmDialog(adminPage, 'Revoke')
  await expect(row).toHaveCount(0)

  expect(await mcpStatus(clientPage, token)).toBe(401)
})
