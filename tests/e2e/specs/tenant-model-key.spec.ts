import { chromium } from '@playwright/test'
import { expect, test } from '../test'
import { runAdmin } from '../admin-tool'
import { waitForDialog } from '../dialogs'
import { startDirectoryServer } from '../directory-server'
import { addAuthenticator, submitPassword } from '../keycloak'
import { setRealmPassword } from '../tenant-realm'

// tenant create makes a database, two roles and a realm, and the realm step waits on Keycloak.
test.setTimeout(240_000)

const tenant = 'model-key-made'
const host = `${tenant}.localhost`
const signIn = `https://${host}/api/auth/sign-in?returnUrl=/`
const key = 'sk-test-key-1234567890'

test('an administrator sets the tenant model key and sees it masked with only its last four characters', async () => {
  const created = await runAdmin(['tenant', 'create', tenant, '--host', host, '--administrator', `first-admin@${tenant}.test`])
  expect(created.status, `${created.stdout}\n${created.stderr}`).toBe(0)

  // Offset 6, because the other specs that start a server use offsets 0 to 5. The main E2E server has no setting for
  // tenant keys and its tenant has no directory row, so the Model key section would be hidden there.
  const server = await startDirectoryServer(6, { Ai__AllowTenantKeys: 'true' })
  const browser = await chromium.launch({
    args: [`--host-resolver-rules=MAP ${host}:443 127.0.0.1:${server.httpsPort}`],
  })
  try {
    const page = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
    const firstAdmin = await setRealmPassword(tenant, `first-admin@${tenant}.test`)
    await page.goto(signIn)
    await submitPassword(page, firstAdmin.username, firstAdmin.password)
    await addAuthenticator(page)
    await page.waitForURL(`https://${host}/`)
    await page.goto(`https://${host}/users`)

    const section = page.getByTestId('tenant-model-key')
    await expect(section.getByRole('heading', { name: 'Model key', exact: true })).toBeVisible()
    await expect(section.getByText('No key is set.', { exact: true })).toBeVisible()

    await section.getByRole('button', { name: 'Set key…', exact: true }).click()
    const dialog = await waitForDialog(page, 'Set the model key')
    const field = dialog.getByLabel('Key', { exact: true })
    await expect(field).toHaveAttribute('type', 'password')
    await field.fill(key)
    await dialog.getByRole('button', { name: 'Save', exact: true }).click()

    const masked = section.getByTestId('tenant-model-key-masked')
    await expect(masked).toHaveText('••••7890')
    await expect(page.getByText(key, { exact: true })).toHaveCount(0)

    await page.reload()
    await expect(section.getByTestId('tenant-model-key-masked')).toHaveText('••••7890')
    await expect(page.getByText(key, { exact: true })).toHaveCount(0)
  } finally {
    await browser.close()
    await server.stop()
  }
})
