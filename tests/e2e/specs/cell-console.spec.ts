import AxeBuilder from '@axe-core/playwright'
import type { Browser, Page } from '@playwright/test'
import { expect, test } from '../test'
import { runAdmin } from '../admin-tool'
import { startCell } from '../cell'
import { confirmDialog, waitForDialog } from '../dialogs'
import { submitCode, submitPassword } from '../keycloak'
import { psql } from '../postgres'
import { addRealmAccount, type RealmAccount } from '../tenant-realm'
import { liveTotp, totpSecret } from '../totp'

// tenant create makes a database, two roles and a realm, and the realm step waits on Keycloak.
test.setTimeout(240_000)
test.describe.configure({ mode: 'serial' })

const tenant = 'console-made'
const tenantHost = `${tenant}.localhost`
const consoleUrl = 'https://console.localhost/'

let cell: Awaited<ReturnType<typeof startCell>>
let browser: Browser
let member: RealmAccount
// The page that the first test signs the operator in on, which the later tests reuse: Keycloak refuses a code twice in one period.
let operator: Page

test.beforeAll(async () => {
  const created = await runAdmin(['tenant', 'create', tenant, '--host', tenantHost, '--administrator', `first-admin@${tenant}.test`])
  expect(created.status, `${created.stdout}\n${created.stderr}`).toBe(0)
  member = await addRealmAccount(tenant, { email: `member@${tenant}.test` })
  // A daily archive that is 30 hours old, so the Tenants view marks it.
  psql(
    'axis',
    `INSERT INTO platform.tenant_backups (tenant_id, last_success_at) VALUES ('${tenant}', now() - interval '30 hours')
     ON CONFLICT (tenant_id) DO UPDATE SET last_success_at = EXCLUDED.last_success_at`,
  )
  // A day three days back, so the worker's sample of today cannot overwrite it.
  psql(
    'axis',
    `INSERT INTO platform.tenant_usage (tenant_id, day, work_items_run) VALUES ('${tenant}', (now() AT TIME ZONE 'UTC')::date - 3, 4321)
     ON CONFLICT (tenant_id, day) DO UPDATE SET work_items_run = EXCLUDED.work_items_run`,
  )

  // Offset 3, because the tenant create spec, the terms spec and the read-only spec serve their tenants on offsets 0 to 2.
  cell = await startCell(3)
  browser = cell.browser
})

test.afterAll(async () => {
  await cell?.stop()
})

async function expectNoViolations(page: Page) {
  const { violations } = await new AxeBuilder({ page }).analyze()
  expect(violations).toEqual([])
}

function navigate(page: Page, name: string) {
  return page.getByRole('navigation').getByRole('link', { name, exact: true }).click()
}

test('a platform operator signs in with a password and a code, sees the tenants and opens one', async () => {
  const page = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
  operator = page

  // The SPA sends a person with no session to the console's sign-in, which sends them to the operator realm.
  await page.goto(consoleUrl)
  await submitPassword(page, 'cell-operator', 'axis')
  await submitCode(page, await liveTotp(totpSecret('cell-operator', 'axis-operators')))
  await page.waitForURL(consoleUrl)
  await expect(page.getByTestId('cell-account')).toHaveText('Cell Operator')

  await expect(page.getByTestId('cell-overview')).toBeVisible()
  await expectNoViolations(page)

  await navigate(page, 'Jobs')
  await expect(page.getByTestId('cell-jobs')).toBeVisible()
  await expectNoViolations(page)

  await navigate(page, 'Tenants')
  await page.getByRole('searchbox', { name: 'Search by ID or host', exact: true }).fill(tenant)
  const link = page.getByTestId('cell-tenants').getByRole('link', { name: tenant, exact: true })
  await expect(link).toBeVisible()
  await expect(
    page.getByTestId('cell-tenants').getByRole('row').filter({ hasText: tenant }).getByTestId('cell-tenants-archive-stale'),
  ).toBeVisible()
  await expectNoViolations(page)

  await link.click()
  await expect(page.getByTestId('cell-tenant')).toContainText(tenantHost)
  await expect(page.getByTestId('cell-tenant-usage').getByRole('row').filter({ hasText: '4,321' })).toBeVisible()
  await expectNoViolations(page)
})

test('a user of a tenant is refused on the console host', async () => {
  const page = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()

  // The member signs in on the tenant's own host first, so a tenant session exists in this browser.
  await page.goto(`https://${tenantHost}/api/auth/sign-in?returnUrl=/`)
  await submitPassword(page, member.username, member.password)
  await submitCode(page, await liveTotp(member.secret))
  await page.waitForURL(`https://${tenantHost}/`)

  // The console host does not count that session, and the operator realm holds no account of the member.
  await page.goto(consoleUrl)
  await page.waitForURL((url) => url.pathname.includes('/realms/axis-operators/'))
  const keycloakOrigin = new URL(page.url()).origin
  await submitPassword(page, member.username, member.password)
  await expect(page.getByRole('alert')).toBeVisible()
  expect(new URL(page.url()).origin).toBe(keycloakOrigin)

  // page.goto, not the context's request, because Node requests ignore the host resolver rules.
  const session = await page.goto('https://console.localhost/api/cell/session')
  expect(session?.status()).toBe(401)
})

// This test runs after the one that signs a member in on the tenant host, which leaves the tenant active.
test('the platform operator suspends a tenant, follows the job and resumes it', async () => {
  const page = operator
  await page.goto(`https://console.localhost/tenants/${tenant}`)
  await expect(page.getByTestId('cell-tenant')).toContainText('Active')

  await page.getByTestId('cell-tenant-suspend').click()
  await expect(await waitForDialog(page)).toContainText(tenant)
  await expectNoViolations(page)
  await confirmDialog(page, 'Suspend')

  const newestJob = page.getByTestId('cell-jobs').getByRole('row').nth(1)
  await expect(newestJob).toContainText('suspend', { timeout: 60_000 })
  await expect(newestJob).toContainText('Succeeded', { timeout: 60_000 })
  await expect(page.getByTestId('cell-tenant')).toContainText('Suspended', { timeout: 60_000 })

  await page.getByTestId('cell-tenant-resume').click()
  await confirmDialog(page, 'Resume')
  await expect(page.getByTestId('cell-jobs').getByRole('row').nth(1)).toContainText('resume', { timeout: 60_000 })
  await expect(page.getByTestId('cell-tenant')).toContainText('Active', { timeout: 60_000 })
})

test('the platform operator sets a quota of a tenant', async () => {
  const page = operator
  await page.goto(`https://console.localhost/tenants/${tenant}`)
  await expect(page.getByTestId('cell-tenant')).toContainText('Active')

  await page.getByTestId('cell-tenant-set-quotas').click()
  const dialog = await waitForDialog(page)
  await expect(dialog).toContainText(tenant)
  await dialog.getByRole('radiogroup', { name: 'Users', exact: true }).getByLabel('A value', { exact: true }).check()
  await dialog.getByRole('spinbutton', { name: 'Users value', exact: true }).fill('25')
  await expectNoViolations(page)
  await confirmDialog(page, 'Save')

  const users = page.getByTestId('cell-tenant-quotas').getByRole('row').filter({ hasText: 'Users' })
  await expect(users).toContainText('25')
  await expect(users).toContainText('Set for this tenant')
})

test('the platform operator adds a host to a tenant and sees the records to add', async () => {
  const page = operator
  const host = `portal.${tenant}.test`
  await page.goto(`https://console.localhost/tenants/${tenant}`)
  await expect(page.getByTestId('cell-tenant')).toContainText('Active')

  await page.getByTestId('cell-tenant-add-host').click()
  const dialog = await waitForDialog(page)
  await expect(dialog).toContainText(tenant)
  await dialog.getByRole('textbox', { name: 'Host', exact: true }).fill(host)
  await dialog.getByRole('button', { name: 'Add', exact: true }).click()

  // The TXT token is random, so the test reads the record names and the CNAME target.
  const records = dialog.getByTestId('cell-tenant-host-records')
  await expect(records).toContainText(`_axis-verify.${host}`)
  await expect(records).toContainText(tenantHost)
  await expectNoViolations(page)
})

test('the platform operator exports a tenant, follows the job and downloads the ZIP after the warning', async () => {
  // The export job reads the tenant's database and writes the ZIP to the export storage.
  const page = operator
  await page.goto(`https://console.localhost/tenants/${tenant}`)

  await page.getByTestId('cell-tenant-export').click()
  await expect(await waitForDialog(page)).toContainText(tenant)
  await expectNoViolations(page)
  await confirmDialog(page, 'Export')

  const newestJob = page.getByTestId('cell-jobs').getByRole('row').nth(1)
  await expect(newestJob).toContainText('export', { timeout: 60_000 })
  await expect(newestJob).toContainText('Succeeded', { timeout: 120_000 })

  await page.getByTestId('cell-tenant-download').click()
  const warning = await waitForDialog(page)
  await expect(warning).toContainText('restricted data')
  await expectNoViolations(page)
  const download = page.waitForEvent('download')
  await confirmDialog(page, 'Download')
  expect((await download).suggestedFilename()).toMatch(new RegExp(`^${tenant}-[0-9a-f-]{36}\\.zip$`))
})

test('cancelling the download warning downloads nothing', async () => {
  const page = operator
  await page.goto(`https://console.localhost/tenants/${tenant}`)
  // The previous test's export is still among the jobs of the tenant.
  await expect(page.getByTestId('cell-jobs')).toContainText('export')

  const zipRequests: string[] = []
  let downloads = 0
  page.on('request', (request) => {
    if (request.url().includes('/exports/') && (request.headers().accept ?? '').includes('application/zip')) {
      zipRequests.push(request.url())
    }
  })
  page.on('download', () => downloads++)

  await page.getByTestId('cell-tenant-download').click()
  const warning = await waitForDialog(page)
  await warning.getByRole('button', { name: 'Cancel', exact: true }).click()
  await expect(warning).toBeHidden()
  await page.waitForTimeout(1_000)

  expect(zipRequests).toEqual([])
  expect(downloads).toBe(0)
})

test('the platform operator creates a tenant, follows the create job and opens the new tenant', async () => {
  // The create job makes a database, a realm, an activation and an invitation email, one after another.
  test.setTimeout(420_000)
  const page = operator
  const created = 'console-created'
  await page.goto('https://console.localhost/tenants')
  await page.getByTestId('cell-tenants-create').click()
  const dialog = await waitForDialog(page)
  await dialog.getByLabel('Tenant ID', { exact: true }).fill(created)
  await dialog.getByLabel('Application', { exact: true }).fill('approvals')
  await dialog.getByLabel("Administrator's email", { exact: true }).fill(`first-admin@${created}.test`)
  await expectNoViolations(page)
  await dialog.getByRole('button', { name: 'Create', exact: true }).click()

  const jobs = dialog.getByTestId('cell-jobs')
  await expect(jobs).toContainText('create', { timeout: 60_000 })
  await expect(jobs).toContainText('Succeeded', { timeout: 180_000 })
  await dialog.getByRole('button', { name: 'Close', exact: true }).last().click()
  await expect(dialog).toBeHidden()

  await page.getByRole('searchbox', { name: 'Search by ID or host', exact: true }).fill(created)
  await page.getByTestId('cell-tenants').getByRole('link', { name: created, exact: true }).click()
  await expect(page.getByTestId('cell-tenant')).toContainText(`${created}.localhost`)
})

test("the create dialog shows the title of the cell's refusal when a tenant already has the id", async () => {
  const page = operator
  await page.goto('https://console.localhost/tenants')
  await page.getByTestId('cell-tenants-create').click()
  const dialog = await waitForDialog(page)
  await dialog.getByLabel('Tenant ID', { exact: true }).fill(tenant)
  await dialog.getByLabel('Application', { exact: true }).fill('approvals')
  await dialog.getByLabel("Administrator's email", { exact: true }).fill(`first-admin@${tenant}.test`)
  await dialog.getByRole('button', { name: 'Create', exact: true }).click()

  await expect(dialog.getByTestId('cell-create-error')).toContainText('A tenant of the cell already has this id.')
  await expect(dialog).toBeVisible()
})

// This test runs last: the 7-day grace period keeps the publisher from purging the tenant, and the cancelled deletion
// leaves the tenant suspended.
test('the platform operator deletes a tenant by typing its id and cancels the deletion', async () => {
  const page = operator
  await page.goto(`https://console.localhost/tenants/${tenant}`)
  await expect(page.getByTestId('cell-tenant')).toContainText('Active')

  await page.getByTestId('cell-tenant-delete').click()
  const dialog = await waitForDialog(page)
  await expect(dialog).toContainText(tenant)
  await expect(dialog.getByRole('button', { name: 'Delete', exact: true })).toBeDisabled()
  await dialog.getByTestId('cell-tenant-delete-id').fill(tenant)
  await expect(dialog.getByRole('button', { name: 'Delete', exact: true })).toBeEnabled()
  await expectNoViolations(page)
  await confirmDialog(page, 'Delete')

  const jobs = page.getByTestId('cell-jobs')
  await expect(jobs.getByRole('row').nth(1)).toContainText('delete', { timeout: 60_000 })
  await expect(jobs.getByRole('row').nth(1)).toContainText('Succeeded', { timeout: 60_000 })
  await expect(page.getByTestId('cell-tenant')).toContainText('Deleting', { timeout: 60_000 })
  await expect(page.getByTestId('cell-tenant-suspend')).toHaveCount(0)

  await page.getByTestId('cell-tenant-cancel-deletion').click()
  await confirmDialog(page, 'Cancel deletion')
  await expect(jobs.getByRole('row').nth(1)).toContainText('cancel-deletion', { timeout: 60_000 })
  await expect(jobs.getByRole('row').nth(1)).toContainText('Succeeded', { timeout: 60_000 })
  await expect(page.getByTestId('cell-tenant')).toContainText('Suspended', { timeout: 60_000 })
})
