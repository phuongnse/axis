import { mkdtemp, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { chromium, type Browser, type Page } from '@playwright/test'
import { expect, test } from '../test'
import { runAdmin } from '../admin-tool'
import { confirmDialog, waitForDialog } from '../dialogs'
import { startDirectoryServer } from '../directory-server'
import { addAuthenticator, submitCode, submitPassword } from '../keycloak'
import { pickOption } from '../select'
import { addRealmAccount, setRealmPassword } from '../tenant-realm'
import { liveTotp } from '../totp'

// tenant create makes a database, two roles and a realm, and the realm step waits on Keycloak.
test.setTimeout(240_000)

const tenant = 'packages-made'
const host = `${tenant}.localhost`
const signIn = `https://${host}/api/auth/sign-in?returnUrl=/`
const application = 'PackageTools'
const packageId = 'purchasing-tools'

/** Whether `GET /api/me` in `page` says the signed-in person may write application packages. */
function studioDeveloper(page: Page): Promise<boolean> {
  return page.evaluate(async () => (await (await fetch('/api/me')).json()).studioDeveloper as boolean)
}

test('a developer creates a package in Studio and opens its file list', async () => {
  const created = await runAdmin([
    'tenant',
    'create',
    tenant,
    '--host',
    host,
    '--administrator',
    `first-admin@${tenant}.test`,
  ])
  expect(created.status, `${created.stdout}\n${created.stderr}`).toBe(0)
  const developer = await addRealmAccount(tenant, { email: `developer@${tenant}.test` })

  // Offset 11, because offsets 0 to 6 and 8 to 10 are taken, and offset 7 is the port of the control plane. The main
  // E2E server's tenant has no directory row, so it cannot store the application packages switch.
  const applications = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-new-package-'))
  const server = await startDirectoryServer(11, {
    Studio__AuthorRole: 'author',
    Studio__DeveloperRole: 'developer',
    Studio__ApplicationsPath: applications,
    // The switch reaches the server at its next read of the directory.
    Tenancy__RefreshInterval: '00:00:02',
  })
  let browser: Browser | undefined
  try {
    browser = await chromium.launch({
      args: [`--host-resolver-rules=MAP ${host}:443 127.0.0.1:${server.httpsPort}`],
    })

    // The developer signs in first, with no role, so the denied case is checked before anything is given. The session
    // reads the given roles at every request, so this page is used again after the roles are given.
    const developerPage = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
    await developerPage.goto(signIn)
    await submitPassword(developerPage, developer.username, developer.password)
    await submitCode(developerPage, await liveTotp(developer.secret))
    await developerPage.waitForURL(`https://${host}/`)
    expect(await studioDeveloper(developerPage)).toBe(false)

    const administratorPage = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
    const firstAdmin = await setRealmPassword(tenant, `first-admin@${tenant}.test`)
    await administratorPage.goto(signIn)
    await submitPassword(administratorPage, firstAdmin.username, firstAdmin.password)
    await addAuthenticator(administratorPage)
    await administratorPage.waitForURL(`https://${host}/`)

    await administratorPage.goto(`https://${host}/users`)
    const switchSection = administratorPage.getByRole('region', { name: 'Application packages', exact: true })
    await switchSection.getByRole('button', { name: 'Turn on…', exact: true }).click()
    await confirmDialog(administratorPage, 'Turn on')
    await expect(switchSection.getByText('Application packages are on.', { exact: true })).toBeVisible()

    await administratorPage.goto(`https://${host}/users/${developer.id}`)
    const platform = administratorPage.getByTestId('user-roles').getByRole('region', { name: 'Platform', exact: true })
    for (const role of ['author', 'developer']) {
      const row = platform.getByTestId('user-role').filter({ has: administratorPage.getByText(role, { exact: true }) })
      await row.getByRole('button', { name: 'Give', exact: true }).click()
      await confirmDialog(administratorPage, 'Give')
      await expect(row.getByText('Given', { exact: true })).toBeVisible()
    }

    // The switch and the roles reach the server within a few seconds, so the answer is polled, not waited for in time.
    await expect.poll(() => studioDeveloper(developerPage), { timeout: 30_000 }).toBe(true)

    await developerPage.goto(`https://${host}/studio`)
    await developerPage.getByRole('button', { name: 'New application…', exact: true }).click()
    const newApplication = await waitForDialog(developerPage)
    await newApplication.getByRole('textbox', { name: 'Name', exact: true }).fill(application)
    await newApplication.getByRole('button', { name: 'Create', exact: true }).click()
    await expect(developerPage).toHaveURL(new RegExp(`app=${application}$`))

    await developerPage
      .getByRole('navigation', { name: 'Menu', exact: true })
      .getByRole('link', { name: 'Packages', exact: true })
      .click()
    await expect(developerPage).toHaveURL(/[?&]view=packages(&|$)/)

    await developerPage.getByRole('button', { name: 'New package…', exact: true }).click()
    const newPackage = await waitForDialog(developerPage, 'New package')
    await newPackage.getByRole('textbox', { name: 'ID', exact: true }).fill(packageId)
    await pickOption(newPackage.getByRole('combobox', { name: 'Language', exact: true }), 'TypeScript')
    await newPackage.getByRole('button', { name: 'Create', exact: true }).click()
    // The dialog closes once the last file is written, so the package is complete by then.
    await expect(newPackage).toBeHidden({ timeout: 30_000 })

    const row = developerPage
      .getByTestId('studio-packages')
      .getByRole('row')
      .filter({ has: developerPage.getByTestId(`studio-package-${packageId}`) })
    for (const cell of ['Purchasing tools', packageId, 'TypeScript', '0.1.0']) {
      await expect(row.getByRole('cell', { name: cell, exact: true })).toBeVisible()
    }

    await row.getByRole('link', { name: 'Purchasing tools', exact: true }).click()
    await expect(developerPage).toHaveURL(new RegExp(`[?&]package=${packageId}(&|$)`))
    await expect(developerPage.getByRole('heading', { name: 'Purchasing tools', exact: true })).toBeVisible()
    await expect(
      developerPage.getByTestId('studio-package-files').getByRole('cell', { name: 'axis-package.json', exact: true }),
    ).toBeVisible()
  } finally {
    await browser?.close()
    await server.stop()
    await rm(applications, { recursive: true, force: true })
  }
})
