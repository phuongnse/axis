import { readFile } from 'node:fs/promises'
import path from 'node:path'
import { expect, test, type Page } from '@playwright/test'
import { compareApplicationFolders } from '../folder-comparison'
import { journeyApplications, journeyURL, serviceDesk } from '../server-settings'
import { useJourneySession } from '../sessions'

// Typing the sample's 37 keys one by one is slow, and the journey server is shared with other specs.
test.setTimeout(300_000)

const built = path.join(journeyApplications, 'service-desk')

test.describe('the author builds the service desk with Studio only', () => {
  // CI retries once, but a retry would meet the ServiceDesk folder from the first attempt and get 409, and removing
  // that folder would be a file write. So retries are off.
  test.describe.configure({ mode: 'serial', retries: 0 })

  async function create(page: Page, kind: string, label: string, value: string) {
    await page.getByRole('button', { name: 'New resource' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('combobox', { name: 'Kind' }).click()
    await page.getByTitle(kind, { exact: true }).click()
    await dialog.getByRole('textbox', { name: label }).fill(value)
    await dialog.getByRole('button', { name: 'Create' }).click()
  }

  async function pick(page: Page, testId: string, option: string, filter?: string) {
    const field = page.getByTestId(testId)
    await field.locator('.ant-select-selector').click()
    if (filter !== undefined) {
      await field.getByRole('combobox').fill(filter)
    }
    await page.locator('.ant-select-dropdown:visible').getByRole('option', { name: option, exact: true }).click()
  }

  async function save(page: Page) {
    const answer = page.waitForResponse(
      (response) =>
        response.request().method() === 'PATCH' && response.url().includes('/api/studio/apps/ServiceDesk/resources/'),
    )
    await page.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByTestId('studio-saved')).toBeVisible()
    const { diagnostics } = (await (await answer).json()) as { diagnostics: { file: string }[] }
    return diagnostics
  }

  async function expectForm(page: Page) {
    await expect(page.getByRole('tab', { name: 'Form' })).toHaveAttribute('aria-selected', 'true')
  }

  async function checkBuilt(page: Page, files: string[]) {
    compareApplicationFolders(serviceDesk, built, { files })
    const response = await page.request.get(`${journeyURL}/api/studio/apps/ServiceDesk/diagnostics`)
    expect(response.status()).toBe(200)
    const { diagnostics } = (await response.json()) as { diagnostics: { severity: string; file: string }[] }
    expect(
      diagnostics.filter((diagnostic) => diagnostic.severity === 'error' && files.includes(diagnostic.file)),
    ).toEqual([])
  }

  test('the author creates ServiceDesk as a blank application', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio`)

    await page.getByRole('button', { name: 'New application' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('textbox', { name: 'Name' }).fill('ServiceDesk')
    await dialog.getByRole('button', { name: 'Create' }).click()
    await expect(page).toHaveURL(/app=ServiceDesk$/)
  })

  test('the author builds the en and vi texts in the text editor', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const starter = page.getByTestId('studio-group-text')
    await starter.getByRole('button').click()
    await expectForm(page)

    await page.getByRole('button', { name: 'Delete' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click()
    await expect(starter.getByRole('button')).toHaveCount(0)

    await create(page, 'text', 'Locale', 'en')
    await expect(page.getByTestId('studio-resource-details')).toContainText('texts/en.json')
    await expectForm(page)

    await create(page, 'text', 'Locale', 'vi')
    await expect(page.getByTestId('studio-resource-details')).toContainText('texts/vi.json')
    await expectForm(page)

    const texts = async (locale: string) => {
      const { texts } = JSON.parse(await readFile(path.join(serviceDesk, 'texts', `${locale}.json`), 'utf8')) as {
        texts: Record<string, string>
      }
      return texts
    }
    const english = await texts('en')
    const vietnamese = await texts('vi')

    const add = page.getByTestId('studio-text-add')
    for (const [key, text] of Object.entries(english)) {
      await add.getByRole('textbox', { name: 'New key', exact: true }).fill(key)
      await add.getByRole('textbox', { name: 'en', exact: true }).fill(text)
      await add.getByRole('textbox', { name: 'vi', exact: true }).fill(vietnamese[key])
      await add.getByRole('button', { name: 'Add key' }).click()
      await expect(add.getByRole('textbox', { name: 'New key', exact: true })).toHaveValue('')
      await expectForm(page)
    }
  })

  test('the built files match the sample', async ({ page }) => {
    // Parts 2 and 3 put their tests before this one, and widen the list of files it checks.
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    await checkBuilt(page, ['application.json', 'texts/en.json', 'texts/vi.json'])
  })
})
