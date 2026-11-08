import { expect, test } from '../test'
import { cp, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import type { Page } from '@playwright/test'
import { startServer } from '../activate'
import { pickOption } from '../select'
import { e2eApp, purchaseRequests } from '../server-settings'
import { useSession } from '../sessions'
import { openListItem, openTab, saveResource } from '../studio-resources'

// A server starts in the test, which takes a few seconds, and a save compiles the application.
test.setTimeout(240_000)

const point = 'Budget/transitions/approve'

async function openFromMenu(page: Page, link: string) {
  await page
    .getByRole('navigation', { name: 'Menu', exact: true })
    .getByRole('link', { name: link, exact: true })
    .click()
}

test.describe('Settings in Studio', () => {
  test.describe.configure({ mode: 'default' })

  let copy: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    // A copy of the sample that relaxes a point of the department budgets contract, so the live sample stays as it is.
    copy = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-settings-'))
    await cp(purchaseRequests, copy, { recursive: true })
    const file = path.join(copy, 'application.json')
    const manifest = JSON.parse(await readFile(file, 'utf8'))
    const budgets = manifest.modules.find((module: { id: string }) => module.id === 'department-budgets')
    budgets.relax = [{ point, reason: 'Small team' }]
    await writeFile(file, JSON.stringify(manifest, null, 2) + '\n')
    server = await startServer({
      ActivateOnStartup__0: e2eApp,
      Studio__Folders__0: copy,
    })
  })

  test.afterAll(async () => {
    await server?.stop()
    if (copy) {
      await rm(copy, { recursive: true, force: true })
    }
  })

  test('an author edits Settings, breaks and repairs the JSON, saves, and opens a release and a relaxed point', async ({
    page,
  }) => {
    await useSession(page, 'author')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)
    await openFromMenu(page, 'Settings')

    // The Modules table says what each module holds in words, and a relaxed point is a link, not an input.
    const modules = page.getByTestId('studio-field-/modules')
    const budgetsRow = modules.getByRole('row').filter({ hasText: 'Department budgets' })
    await expect(budgetsRow).toContainText('1.2.0 and later 1.x')
    const relaxed = budgetsRow.getByTestId('studio-relaxed-point')
    await expect(relaxed).toContainText('Small team')
    const contractLink = relaxed.getByRole('link')
    await expect(contractLink).toHaveAttribute('href', /^\/studio\?app=PurchaseRequests&resource=[0-9a-f-]{36}$/)
    await expect(relaxed.getByRole('textbox')).toHaveCount(0)

    // Edit module holds no relax field.
    const dialog = await openListItem(page, '/modules', 1)
    await expect(dialog.getByTestId('studio-field-/modules/1/version')).toBeVisible()
    await expect(dialog.getByTestId('studio-field-/modules/1/relax')).toHaveCount(0)
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
    await expect(dialog).toHaveCount(0)

    await pickOption(
      page.getByTestId('studio-field-/timeZone').getByRole('combobox'),
      '(UTC+07:00) Asia/Ho Chi Minh',
      'Ho Chi Minh',
    )

    // JSON that does not parse turns the form read-only and turns Save off.
    await openTab(page, 'JSON')
    const json = page.getByRole('textbox', { name: 'JSON', exact: true })
    await json.fill('{ "name": ')
    await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeDisabled()
    await openTab(page, 'Form')
    await expect(page.getByTestId('studio-form-read-only')).toBeVisible()

    // Repaired JSON brings the form back, and Save writes it.
    const manifest = JSON.parse(await readFile(path.join(copy, 'application.json'), 'utf8'))
    await openTab(page, 'JSON')
    await json.fill(JSON.stringify({ ...manifest, timeZone: 'Asia/Ho_Chi_Minh' }, null, 2))
    await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeEnabled()
    await openTab(page, 'Form')
    await expect(page.getByTestId('studio-form-read-only')).toHaveCount(0)
    await saveResource(page)
    // The saved relax entry is still shown.
    await expect(modules.getByTestId('studio-relaxed-point')).toHaveCount(1)

    // A release's number opens its page.
    await openFromMenu(page, 'Releases')
    await expect(page.getByTestId('studio-releases')).toBeVisible()
    const release = page.getByTestId('studio-release-row').first()
    const number = await release.getAttribute('data-number')
    await release.getByRole('link', { name: `${number}`, exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`view=release&release=${number}`))
    await expect(page.getByRole('heading', { name: `Release ${number}`, exact: true })).toBeVisible()

    // The relaxed point's link opens its contract's page.
    await openFromMenu(page, 'Settings')
    const href = await page.getByTestId('studio-relaxed-point').getByRole('link').getAttribute('href')
    await page.getByTestId('studio-relaxed-point').getByRole('link').click()
    await expect(page).toHaveURL(new RegExp(`resource=${new URL(href!, server.url).searchParams.get('resource')}`))
    await expect(page.getByTestId('studio-editor')).toBeVisible()
  })
})
