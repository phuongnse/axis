import type { Browser, Page } from '@playwright/test'
import { expect, test } from '../test'
import { startCell } from '../cell'
import { confirmDialog, waitForDialog } from '../dialogs'
import { addAuthenticator, submitCode, submitPassword } from '../keycloak'
import { psql } from '../postgres'
import { addRealmAccount, clearRequiredActions, giveRealmRole, setRealmPassword } from '../tenant-realm'
import { liveTotp } from '../totp'

// The journey of a cell: the operator creates two tenants from the built-in approvals application, one of them goes over
// its storage quota and is refused while the other still saves, and the operator exports that one, deletes it and sees
// its host answer 404 once it is purged. The tests share the two tenants, so they run in order.
test.describe.configure({ mode: 'serial' })

const full = 'quota-full'
const room = 'quota-room'
const consoleUrl = 'https://console.localhost/'
const storageQuotaTitle = 'This tenant has reached its storage quota.'
const departments = '/approvals/organization/departments'

let cell: Awaited<ReturnType<typeof startCell>>
let browser: Browser
// The pages that the first test signs in on, which the later tests reuse: Keycloak refuses a code twice in one period.
let operator: Page
const administrators: Record<string, Page> = {}

const host = (tenant: string) => `${tenant}.localhost`

test.beforeAll(async () => {
  // Offset 4, because the tenant create spec, the terms spec and the read-only spec serve their tenants on offsets 0 to 2
  // and the cell console spec on 3. The server reads the tenant directory every 2 seconds, so a new tenant and a changed
  // quota take effect within a few seconds.
  cell = await startCell(4, { Tenancy__RefreshInterval: '00:00:02' })
  browser = cell.browser
})

test.afterAll(async () => {
  await cell?.stop()
})

/** Creates `tenant` from the approvals application in the Create tenant dialog and waits for the create job. */
async function createTenant(page: Page, tenant: string) {
  await page.goto(`${consoleUrl}tenants`)
  await page.getByTestId('cell-tenants-create').click()
  const dialog = await waitForDialog(page)
  await dialog.getByLabel('Tenant ID', { exact: true }).fill(tenant)
  await dialog.getByLabel('Application', { exact: true }).fill('approvals')
  await dialog.getByLabel("Administrator's email", { exact: true }).fill(`first-admin@${tenant}.test`)
  await dialog.getByRole('button', { name: 'Create', exact: true }).click()

  const jobs = dialog.getByTestId('cell-jobs')
  await expect(jobs).toContainText('create', { timeout: 60_000 })
  await expect(jobs).toContainText('Succeeded', { timeout: 180_000 })
  await dialog.getByRole('button', { name: 'Close', exact: true }).last().click()
  await expect(dialog).toBeHidden()
}

/** Signs the first administrator of `tenant` in on the tenant's host, once the server has read the tenant. */
async function signInFirstAdministrator(tenant: string): Promise<Page> {
  const page = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
  const signIn = `https://${host(tenant)}/api/auth/sign-in?returnUrl=/`
  // The host answers 404 until the server's next read of the tenant directory.
  await expect(async () => {
    expect((await page.goto(signIn))?.status()).not.toBe(404)
  }).toPass({ timeout: 60_000 })
  const email = `first-admin@${tenant}.test`
  const account = await setRealmPassword(tenant, email)
  await submitPassword(page, account.username, account.password)
  await addAuthenticator(page)
  await page.waitForURL(`https://${host(tenant)}/`)
  await expect(page.getByRole('banner')).toBeVisible()
  return page
}

/** Gives the administrator the `admin` role of the Approvals application, on the users page. */
async function giveApplicationAdmin(page: Page, tenant: string) {
  const email = `first-admin@${tenant}.test`
  await page.goto(`https://${host(tenant)}/users`)
  const main = page.getByRole('main')
  await main.getByRole('searchbox', { name: 'Search by name or email', exact: true }).fill(email)
  await page.keyboard.press('Enter')
  await main.getByRole('row').filter({ hasText: email }).getByRole('link').click()

  const section = page.getByTestId('user-roles').getByRole('region', { name: 'Approvals', exact: true })
  const role = section.getByTestId('user-role').filter({ hasText: 'admin' })
  await role.getByRole('button', { name: 'Give', exact: true }).click()
  const dialog = await waitForDialog(page, 'Give this role?')
  await expect(dialog).toContainText('Role: admin')
  await dialog.getByRole('button', { name: 'Give', exact: true }).click()
  await expect(role.getByText('Given', { exact: true })).toBeVisible()
}

/** Opens the form of a new department on `tenant`'s Departments page, fills it and saves. */
async function openNewDepartment(page: Page, tenant: string, code: string, name: string) {
  await page.goto(`https://${host(tenant)}${departments}`)
  await page.getByRole('button', { name: 'New', exact: true }).click()
  await expect(page).toHaveURL(/\/departmentform\/new$/)
  await page.getByRole('textbox', { name: 'Code', exact: true }).fill(code)
  await page.getByRole('textbox', { name: 'Name', exact: true }).fill(name)
  await page.getByRole('button', { name: 'Save', exact: true }).click()
}

/** Adds a department and waits until the list shows it. */
async function saveDepartment(page: Page, tenant: string, code: string, name: string) {
  await openNewDepartment(page, tenant, code, name)
  await expect(page).toHaveURL(new RegExp(`${departments}$`))
  await expect(page.getByRole('row', { name: new RegExp(name) })).toBeVisible()
}

test('the operator creates two tenants from the approvals application and their first administrators sign in', async () => {
  // Each create job makes a database, a realm, an activation and an invitation email, one after another.
  test.setTimeout(600_000)
  const account = await addRealmAccount('axis-operators', { email: 'journey-operator@axis.test' })
  await giveRealmRole('axis-operators', account.id, 'cell-operator')
  // The operator realm asks every new account to set up an authenticator, and this one has it already.
  await clearRequiredActions('axis-operators', account.id)

  // This operator is not the development operator, because the cell console spec may sign that one in at the same moment.
  operator = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
  await operator.goto(consoleUrl)
  await submitPassword(operator, account.username, account.password)
  await submitCode(operator, await liveTotp(account.secret))
  await operator.waitForURL(consoleUrl)
  await expect(operator.getByTestId('cell-overview')).toBeVisible()

  await createTenant(operator, full)
  await createTenant(operator, room)

  for (const tenant of [full, room]) {
    administrators[tenant] = await signInFirstAdministrator(tenant)
    await giveApplicationAdmin(administrators[tenant], tenant)
    // Both tenants save a department while neither is over its quota.
    await saveDepartment(administrators[tenant], tenant, 'FIRST', `First department of ${tenant}`)
  }
})

test('the operator lowers a quota under the use of a tenant, its save is refused and the other tenant still saves', async () => {
  // No worker samples the directory tenants of this run, and the hourly sample is the only one the storage check reads.
  for (const tenant of [full, room]) {
    psql(
      'axis',
      `INSERT INTO platform.tenant_usage_latest (tenant_id, database_bytes, file_bytes, sampled_at)
       SELECT '${tenant}', pg_database_size('axis_${tenant}'), 0, now()
       ON CONFLICT (tenant_id) DO UPDATE SET database_bytes = EXCLUDED.database_bytes, file_bytes = 0, sampled_at = EXCLUDED.sampled_at`,
    )
  }

  await operator.goto(`${consoleUrl}tenants/${full}`)
  await operator.getByTestId('cell-tenant-set-quotas').click()
  const dialog = await waitForDialog(operator)
  await expect(dialog).toContainText(full)
  await dialog.getByRole('radiogroup', { name: 'Storage (bytes)', exact: true }).getByLabel('A value', { exact: true }).check()
  await dialog.getByRole('spinbutton', { name: 'Storage (bytes) value', exact: true }).fill('1000000')
  await confirmDialog(operator, 'Save')
  const storage = operator.getByTestId('cell-tenant-quotas').getByRole('row').filter({ hasText: 'Storage (bytes)' })
  await expect(storage).toContainText('1,000,000')
  await expect(storage).toContainText('Set for this tenant')

  // page.goto, not the context's request, because Node requests ignore the host resolver rules. The server applies the
  // quota at its next read of the tenant directory.
  const page = administrators[full]
  await expect
    .poll(async () => {
      const response = await page.goto(`https://${host(full)}/api/tenant/usage`)
      return response?.ok() ? ((await response.json()) as { quotas: { storage: { limit: number | null } } }).quotas.storage.limit : null
    }, { timeout: 60_000 })
    .toBe(1_000_000)

  await openNewDepartment(page, full, 'SECOND', 'Second department of quota-full')
  await expect(page.getByTestId('form-error')).toHaveText(storageQuotaTitle)
  await expect(page).toHaveURL(/\/departmentform\/new$/)

  await saveDepartment(administrators[room], room, 'SECOND', 'Second department of quota-room')
})

test('the operator exports the tenant over its quota, downloads the ZIP after the warning and deletes the tenant', async () => {
  test.setTimeout(420_000)
  const page = operator
  await page.goto(`${consoleUrl}tenants/${full}`)

  await page.getByTestId('cell-tenant-export').click()
  await expect(await waitForDialog(page)).toContainText(full)
  await confirmDialog(page, 'Export')
  const jobs = page.getByTestId('cell-jobs')
  await expect(jobs.getByRole('row').nth(1)).toContainText('export', { timeout: 60_000 })
  await expect(jobs.getByRole('row').nth(1)).toContainText('Succeeded', { timeout: 120_000 })

  await page.getByTestId('cell-tenant-download').click()
  const warning = await waitForDialog(page)
  await expect(warning).toContainText('restricted data')
  const download = page.waitForEvent('download')
  await confirmDialog(page, 'Download')
  expect((await download).suggestedFilename()).toMatch(new RegExp(`^${full}-[0-9a-f-]{36}\\.zip$`))

  await page.getByTestId('cell-tenant-delete').click()
  const dialog = await waitForDialog(page)
  await dialog.getByTestId('cell-tenant-delete-id').fill(full)
  await confirmDialog(page, 'Delete')
  await expect(jobs.getByRole('row').nth(1)).toContainText('delete', { timeout: 60_000 })
  await expect(jobs.getByRole('row').nth(1)).toContainText('Succeeded', { timeout: 60_000 })
  await expect(page.getByTestId('cell-tenant')).toContainText('Deleting', { timeout: 60_000 })
})

test('after the purge the host of the deleted tenant answers 404 and the other tenant still answers', async () => {
  test.setTimeout(420_000)
  // The grace period is 7 days, so the test moves the date back for the publisher to purge the tenant at once.
  psql('axis', `UPDATE platform.tenants SET delete_after = now() - interval '1 minute' WHERE id = '${full}' OR preview_of = '${full}'`)
  await expect
    .poll(() => psql('axis', `SELECT state FROM platform.jobs WHERE tenant_id = '${full}' AND kind = 'purge'`)[0], {
      timeout: 180_000,
    })
    .toBe('succeeded')

  const page = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
  await expect
    .poll(async () => (await page.goto(`https://${host(full)}/`))?.status(), { timeout: 60_000 })
    .toBe(404)
  expect((await administrators[room].goto(`https://${host(room)}/`))?.status()).toBe(200)
})
