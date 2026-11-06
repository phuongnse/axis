import { spawnSync } from 'node:child_process'
import { randomBytes } from 'node:crypto'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import AxeBuilder from '@axe-core/playwright'
import { chromium, type Browser, type Page } from '@playwright/test'
import { expect, test } from '../test'
import { runAdmin, startPublisher } from '../admin-tool'
import { startDirectoryServer } from '../directory-server'
import { confirmDialog, waitForDialog } from '../dialogs'
import { submitCode, submitPassword } from '../keycloak'
import { required } from '../server-settings'
import { addRealmAccount, type RealmAccount } from '../tenant-realm'
import { liveTotp, totpSecret } from '../totp'

// tenant create makes a database, two roles and a realm, and the realm step waits on Keycloak.
test.setTimeout(240_000)
test.describe.configure({ mode: 'serial' })

const tenant = 'console-made'
const tenantHost = `${tenant}.localhost`
const consoleUrl = 'https://console.localhost/'

let server: Awaited<ReturnType<typeof startDirectoryServer>>
let publisher: Awaited<ReturnType<typeof startPublisher>>
let browser: Browser
let member: RealmAccount
// The page that the first test signs the operator in on, which the last test reuses: Keycloak refuses a code twice in one period.
let operator: Page
// Holds the backup key and the applications folder that the create job needs.
let publisherFolder: string

const cellSettings = {
  Cell__Id: 'vn-1',
  Cell__Region: 'vn',
  Cell__TenantDomain: 'localhost',
  Cell__ConsoleHost: 'console.localhost',
}

// The export job stores its ZIP in the MinIO that scripts/e2e.sh starts. The server reads it back for the download.
const exportStorage = {
  Cell__ExportStorage__ServiceUrl: required('AXIS_E2E_EXPORT_STORAGE_URL', 'the address of the export storage'),
  Cell__ExportStorage__Region: required('AXIS_E2E_EXPORT_STORAGE_REGION', 'the region of the export storage'),
  Cell__ExportStorage__Bucket: required('AXIS_E2E_EXPORT_STORAGE_BUCKET', 'the bucket of the export storage'),
  Cell__ExportStorage__AccessKeyId: required('AXIS_E2E_EXPORT_STORAGE_ACCESS_KEY', 'the access key of the export storage'),
  Cell__ExportStorage__SecretAccessKey: required('AXIS_E2E_EXPORT_STORAGE_SECRET_KEY', 'the secret key of the export storage'),
}

/** Runs `sql` on the platform database in the E2E PostgreSQL container. */
function psql(sql: string) {
  const container = process.env.AXIS_E2E_POSTGRES_CONTAINER
  if (!container) {
    throw new Error('AXIS_E2E_POSTGRES_CONTAINER is not set. Run the E2E tests with scripts/e2e.sh.')
  }
  const result = spawnSync('docker', ['exec', container, 'psql', '-U', 'axis', '-d', 'axis', '-tAc', sql], {
    encoding: 'utf8',
  })
  if (result.status !== 0) {
    throw new Error(`psql failed with status ${result.status}: ${result.stderr}`)
  }
}

test.beforeAll(async () => {
  const created = await runAdmin(['tenant', 'create', tenant, '--host', tenantHost, '--administrator', `first-admin@${tenant}.test`])
  expect(created.status, `${created.stdout}\n${created.stderr}`).toBe(0)
  member = await addRealmAccount(tenant, { email: `member@${tenant}.test` })
  // A daily archive that is 30 hours old, so the Tenants view marks it.
  psql(
    `INSERT INTO platform.tenant_backups (tenant_id, last_success_at) VALUES ('${tenant}', now() - interval '30 hours')
     ON CONFLICT (tenant_id) DO UPDATE SET last_success_at = EXCLUDED.last_success_at`,
  )

  // Offset 3, because the tenant create spec, the terms spec and the read-only spec serve their tenants on offsets 0 to 2.
  server = await startDirectoryServer(3, {
    ...cellSettings,
    ...exportStorage,
    Cell__ConsoleClientSecret: 'axis-cell-console-development-secret',
    IdentityProvider__Url: `${required('AXIS_E2E_KEYCLOAK_URL', 'the address of Keycloak')}/`,
  })
  // The create job encrypts its first backup and creates the application's folder, so the publisher needs a backup key
  // and an applications folder.
  publisherFolder = mkdtempSync(path.join(os.tmpdir(), 'axis-cell-console-'))
  const backupKeyFile = path.join(publisherFolder, 'backup.key')
  writeFileSync(backupKeyFile, randomBytes(32).toString('base64'))
  const applications = path.join(publisherFolder, 'applications')
  mkdirSync(applications)
  // The run's shared publisher runs no provisioning jobs, so this one runs the jobs that the console queues.
  publisher = await startPublisher({
    ...cellSettings,
    ...exportStorage,
    Cell__BackupKeyFile: backupKeyFile,
    Studio__ApplicationsPath: applications,
    IdentityProvider__Url: `${required('AXIS_E2E_KEYCLOAK_URL', 'the address of Keycloak')}/`,
  })
  browser = await chromium.launch({
    args: [`--host-resolver-rules=MAP *.localhost:443 127.0.0.1:${server.httpsPort}`],
  })
})

test.afterAll(async () => {
  await browser?.close()
  await publisher?.stop()
  await server?.stop()
  if (publisherFolder) {
    rmSync(publisherFolder, { recursive: true, force: true })
  }
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
