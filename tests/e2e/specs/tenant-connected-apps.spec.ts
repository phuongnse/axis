import { chromium } from '@playwright/test'
import { expect, test } from '../test'
import { runAdmin } from '../admin-tool'
import { startDirectoryServer } from '../directory-server'
import { addAuthenticator, submitCode, submitPassword } from '../keycloak'
import { addRealmAccount, setRealmPassword } from '../tenant-realm'
import { liveTotp } from '../totp'

// tenant create makes a database, two roles and a realm, and the realm step waits on Keycloak.
test.setTimeout(240_000)
test.describe.configure({ mode: 'serial' })

const tenant = 'connected-apps-made'
const host = `${tenant}.localhost`
const signIn = `https://${host}/api/auth/sign-in?returnUrl=/`

test('an administrator allows one outside client in Connected apps and a reload shows it, and a member has no tab', async () => {
  const created = await runAdmin(['tenant', 'create', tenant, '--host', host, '--administrator', `first-admin@${tenant}.test`])
  expect(created.status, `${created.stdout}\n${created.stderr}`).toBe(0)
  const member = await addRealmAccount(tenant, { email: `member@${tenant}.test` })

  // Offset 8, because the other specs that start a server use offsets 0 to 6, and offset 7 is the port of the control
  // plane. The main E2E server's tenant has no directory row, so the Connected apps tab would be hidden there.
  const server = await startDirectoryServer(8)
  const browser = await chromium.launch({
    args: [`--host-resolver-rules=MAP ${host}:443 127.0.0.1:${server.httpsPort}`],
  })
  try {
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
    await addAuthenticator(adminPage)
    await adminPage.waitForURL(`https://${host}/`)
    await adminPage.goto(`https://${host}/users`)

    await adminPage.getByRole('tab', { name: 'Connected apps', exact: true }).click()
    const tab = adminPage.getByTestId('connected-apps')
    await tab.getByRole('radio', { name: 'Allow list', exact: true }).check()
    await tab.getByRole('combobox', { name: 'Allowed client IDs', exact: true }).fill('e2e-client')
    await adminPage.keyboard.press('Enter')
    await tab.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(adminPage.getByTestId('connected-apps-notice')).toHaveText('The setting was saved.')

    await adminPage.reload()
    await adminPage.getByRole('tab', { name: 'Connected apps', exact: true }).click()
    await expect(tab.getByRole('radio', { name: 'Allow list', exact: true })).toBeChecked()
    await expect(tab.getByText('e2e-client', { exact: true })).toBeVisible()
  } finally {
    await browser.close()
    await server.stop()
  }
})
