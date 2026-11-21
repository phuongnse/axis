import { expect, type Page } from '@playwright/test'
import { confirmDialog, waitForDialog } from './dialogs'
import { addAuthenticator, submitCode, submitPassword } from './keycloak'
import { pickOption } from './select'
import type { RealmAccount } from './tenant-realm'
import { liveTotp } from './totp'

// The steps that the Studio package specs share: the sign-ins, the switch and roles of a tenant, and a new application
// and package. See the new package spec and the package run spec in docs/testing.md.

/** Signs `account` in on the tenant's `host` with its password and a code, and waits for the tenant's home. */
export async function signInWithCode(page: Page, host: string, account: RealmAccount) {
  await page.goto(`https://${host}/api/auth/sign-in?returnUrl=/`)
  await submitPassword(page, account.username, account.password)
  await submitCode(page, await liveTotp(account.secret))
  await page.waitForURL(`https://${host}/`)
}

/** Signs the first administrator of the tenant's `host` in with its password, and adds the authenticator for its codes. */
export async function signInAdministrator(page: Page, host: string, account: { username: string; password: string }) {
  await page.goto(`https://${host}/api/auth/sign-in?returnUrl=/`)
  await submitPassword(page, account.username, account.password)
  await addAuthenticator(page)
  await page.waitForURL(`https://${host}/`)
}

/** Whether `GET /api/me` in `page` says the signed-in person may write application packages. */
export function studioDeveloper(page: Page): Promise<boolean> {
  return page.evaluate(async () => (await (await fetch('/api/me')).json()).studioDeveloper as boolean)
}

/** Turns on application packages for the tenant on `host`, from the users page of its administrator, and waits until it says so. */
export async function turnOnApplicationPackages(administratorPage: Page, host: string) {
  await administratorPage.goto(`https://${host}/users`)
  const switchSection = administratorPage.getByRole('region', { name: 'Application packages', exact: true })
  await switchSection.getByRole('button', { name: 'Turn on…', exact: true }).click()
  await confirmDialog(administratorPage, 'Turn on')
  await expect(switchSection.getByText('Application packages are on.', { exact: true })).toBeVisible()
}

/** Gives the user `userId` of the tenant on `host` each role in `roles` in the Platform region, and waits until each reads Given. */
export async function giveRoles(administratorPage: Page, host: string, userId: string, roles: string[]) {
  await administratorPage.goto(`https://${host}/users/${userId}`)
  const platform = administratorPage.getByTestId('user-roles').getByRole('region', { name: 'Platform', exact: true })
  for (const role of roles) {
    const row = platform.getByTestId('user-role').filter({ has: administratorPage.getByText(role, { exact: true }) })
    await row.getByRole('button', { name: 'Give', exact: true }).click()
    await confirmDialog(administratorPage, 'Give')
    await expect(row.getByText('Given', { exact: true })).toBeVisible()
  }
}

/** Makes the application `name` from the New application… button of the Studio on `host`, and expects it open. */
export async function newApplication(page: Page, host: string, name: string) {
  await page.goto(`https://${host}/studio`)
  await page.getByRole('button', { name: 'New application…', exact: true }).click()
  const dialog = await waitForDialog(page)
  await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill(name)
  await dialog.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page).toHaveURL(new RegExp(`app=${name}$`))
}

/** Makes the package `id` in `language` in the open application, from its Packages view, and returns once the dialog closes. */
export async function newPackage(page: Page, id: string, language: string) {
  await page
    .getByRole('navigation', { name: 'Menu', exact: true })
    .getByRole('link', { name: 'Packages', exact: true })
    .click()
  await expect(page).toHaveURL(/[?&]view=packages(&|$)/)

  await page.getByRole('button', { name: 'New package…', exact: true }).click()
  const dialog = await waitForDialog(page, 'New package')
  await dialog.getByRole('textbox', { name: 'ID', exact: true }).fill(id)
  await pickOption(dialog.getByRole('combobox', { name: 'Language', exact: true }), language)
  await dialog.getByRole('button', { name: 'Create', exact: true }).click()
  // The dialog closes once the last file is written, so the package is complete by then.
  await expect(dialog).toBeHidden({ timeout: 30_000 })
}
