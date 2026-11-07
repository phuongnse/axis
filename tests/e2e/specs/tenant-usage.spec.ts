import { chromium } from '@playwright/test'
import { expect, test } from '../test'
import { runAdmin } from '../admin-tool'
import { startDirectoryServer } from '../directory-server'
import { addAuthenticator, submitCode, submitPassword } from '../keycloak'
import { psql } from '../postgres'
import { addRealmAccount, setRealmPassword } from '../tenant-realm'
import { liveTotp } from '../totp'

// tenant create makes a database, two roles and a realm, and the realm step waits on Keycloak.
test.setTimeout(240_000)
test.describe.configure({ mode: 'serial' })

const tenant = 'usage-made'
const host = `${tenant}.localhost`
const signIn = `https://${host}/api/auth/sign-in?returnUrl=/`

test('an administrator of a cell tenant sees the storage quota and its use in the Usage section, and a member does not', async () => {
  const created = await runAdmin(['tenant', 'create', tenant, '--host', host, '--administrator', `first-admin@${tenant}.test`])
  expect(created.status, `${created.stdout}\n${created.stderr}`).toBe(0)
  const member = await addRealmAccount(tenant, { email: `member@${tenant}.test` })
  // 1 GiB of database and 0.5 GiB of files, which the Usage section shows as 1.5 GB of the 2 GB limit set on the server.
  psql(
    'axis',
    `INSERT INTO platform.tenant_usage_latest (tenant_id, database_bytes, file_bytes, sampled_at)
     VALUES ('${tenant}', 1073741824, 536870912, now())
     ON CONFLICT (tenant_id) DO UPDATE SET database_bytes = EXCLUDED.database_bytes, file_bytes = EXCLUDED.file_bytes, sampled_at = EXCLUDED.sampled_at`,
  )

  // Offset 5, because the other specs that start a server use offsets 0 to 4.
  const server = await startDirectoryServer(5, {
    Cell__Id: 'vn-1',
    Cell__Region: 'vn',
    Cell__TenantDomain: 'localhost',
    Quotas__Defaults__storage: '2147483648',
  })
  const browser = await chromium.launch({
    args: [`--host-resolver-rules=MAP ${host}:443 127.0.0.1:${server.httpsPort}`],
  })
  try {
    const memberPage = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
    const adminPage = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()

    // A member is refused the users page, so it has no Usage section and the usage route answers 403.
    await memberPage.goto(signIn)
    await submitPassword(memberPage, member.username, member.password)
    await submitCode(memberPage, await liveTotp(member.secret))
    await memberPage.waitForURL(`https://${host}/`)
    await memberPage.goto(`https://${host}/users`)
    await expect(memberPage.getByTestId('users-forbidden')).toBeVisible()
    await expect(memberPage.getByTestId('tenant-usage')).toHaveCount(0)
    const answer = await memberPage.goto(`https://${host}/api/tenant/usage`)
    expect(answer?.status()).toBe(403)

    // The administrator that tenant create invited sees the storage quota and its use under Usage.
    const firstAdmin = await setRealmPassword(tenant, `first-admin@${tenant}.test`)
    await adminPage.goto(signIn)
    await submitPassword(adminPage, firstAdmin.username, firstAdmin.password)
    await addAuthenticator(adminPage)
    await adminPage.waitForURL(`https://${host}/`)
    await adminPage.goto(`https://${host}/users`)

    const usage = adminPage.getByTestId('tenant-usage')
    await expect(usage.getByRole('heading', { name: 'Usage', exact: true })).toBeVisible()
    const storage = usage.getByTestId('tenant-usage-quota-storage')
    await expect(storage).toContainText('Storage')
    await expect(storage).toContainText('1.5 GB of 2 GB')
    await expect(storage.getByRole('progressbar', { name: 'Storage', exact: true })).toBeVisible()
  } finally {
    await browser.close()
    await server.stop()
  }
})
